using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging.Abstractions;
using PotatoMaker.Core;
using PotatoMaker.GUI.Services;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Coordinates the persisted compression queue and executes queued items sequentially.
/// </summary>
public partial class CompressionQueueViewModel : ViewModelBase, IDisposable
{
    public const int MaxQueueSizeLimit = 10;

    private readonly IAppSettingsCoordinator? _settingsCoordinator;
    private readonly IVideoEncodingService _encodingService;
    private readonly EncodeExecutionCoordinator _executionCoordinator;
    private readonly IEncodeCompletionNotifier _encodeCompletionNotifier;
    private readonly IProcessedVideoTracker _processedVideoTracker;
    private readonly ObservableCollection<CompressionQueueItemViewModel> _items = [];
    private readonly ReadOnlyObservableCollection<CompressionQueueItemViewModel> _readonlyItems;

    public CompressionQueueViewModel()
        : this(
            null,
            new VideoEncodingService(),
            new EncodeExecutionCoordinator(),
            NoOpEncodeCompletionNotifier.Instance,
            DisabledProcessedVideoTracker.Instance)
    {
    }

    public CompressionQueueViewModel(
        IAppSettingsCoordinator? settingsCoordinator,
        IVideoEncodingService encodingService,
        EncodeExecutionCoordinator executionCoordinator,
        IEncodeCompletionNotifier? encodeCompletionNotifier = null,
        IProcessedVideoTracker? processedVideoTracker = null)
    {
        ArgumentNullException.ThrowIfNull(encodingService);
        ArgumentNullException.ThrowIfNull(executionCoordinator);

        _settingsCoordinator = settingsCoordinator;
        _encodingService = encodingService;
        _executionCoordinator = executionCoordinator;
        _encodeCompletionNotifier = encodeCompletionNotifier ?? NoOpEncodeCompletionNotifier.Instance;
        _processedVideoTracker = processedVideoTracker ?? DisabledProcessedVideoTracker.Instance;
        _readonlyItems = new ReadOnlyObservableCollection<CompressionQueueItemViewModel>(_items);

        _items.CollectionChanged += OnItemsCollectionChanged;
        _executionCoordinator.PropertyChanged += OnExecutionCoordinatorPropertyChanged;
        LoadPersistedQueueItems();
        NotifyQueueStateChanged();
    }

    public ReadOnlyObservableCollection<CompressionQueueItemViewModel> Items => _readonlyItems;

    public int QueueCount => Items.Count;

    public int ActiveItemCount => Items.Count(item => item.PersistsAcrossSessions);

    public int WaitingItemCount => Items.Count(item => item.Status == CompressionQueueItemStatus.Queued);

    public bool HasItems => QueueCount > 0;

    public bool IsEmpty => !HasItems;

    public int MaxQueueSize => MaxQueueSizeLimit;

    public bool IsBlockedByAnotherEncode => _executionCoordinator.IsBusy && !IsQueueProcessing;

    public string QueueCountText => QueueCount == 1
        ? "1 video"
        : $"{QueueCount} videos";

    public string QueueSummaryText => HasItems
        ? $"{WaitingItemCount} waiting, up to {MaxQueueSize} active items"
        : $"Queue up to {MaxQueueSize} prepared videos";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCompressAll))]
    private bool _isQueueProcessing;

    public bool CanCompressAll =>
        !IsQueueProcessing &&
        !_executionCoordinator.IsBusy &&
        Items.Any(item => item.Status == CompressionQueueItemStatus.Queued);

    public bool CanClearQueue => HasItems && !IsQueueProcessing;

    public async Task<QueueEnqueueResult> AddAsync(QueuedCompressionItemDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (ActiveItemCount >= MaxQueueSize)
            return new QueueEnqueueResult(false, $"Queue is full. Remove an item before adding more than {MaxQueueSize} videos.");

        string duplicateKey = CompressionQueueItemViewModel.BuildDuplicateKey(draft);
        if (Items.Any(item => item.BlocksDuplicateEntries &&
                              string.Equals(item.DuplicateKey, duplicateKey, StringComparison.Ordinal)))
        {
            return new QueueEnqueueResult(false, "This exact video selection is already in the queue.");
        }

        var item = CompressionQueueItemViewModel.Create(draft);
        RegisterItem(item);
        _items.Add(item);
        await PersistQueueSafelyAsync();
        return new QueueEnqueueResult(true, $"Added to queue ({ActiveItemCount}/{MaxQueueSize}).");
    }

    [RelayCommand(CanExecute = nameof(CanCompressAll), AllowConcurrentExecutions = false)]
    private async Task CompressAllAsync()
    {
        IDisposable? lease = _executionCoordinator.TryAcquire();
        if (lease is null)
        {
            NotifyQueueStateChanged();
            return;
        }

        IsQueueProcessing = true;
        NotifyQueueStateChanged();
        bool completedAny = false;

        try
        {
            while (TryGetNextQueuedItem(out CompressionQueueItemViewModel? item))
            {
                if (item is null)
                    break;

                completedAny |= await RunQueueItemAsync(item);
            }
        }
        finally
        {
            IsQueueProcessing = false;
            lease.Dispose();
            NotifyQueueStateChanged();
        }

        if (completedAny)
            _encodeCompletionNotifier.NotifyEncodeSucceeded();
    }

    [RelayCommand(CanExecute = nameof(CanClearQueue), AllowConcurrentExecutions = false)]
    private async Task ClearQueueAsync()
    {
        if (!CanClearQueue)
            return;

        foreach (CompressionQueueItemViewModel item in Items.ToArray())
            UnregisterItem(item);

        _items.Clear();
        await PersistQueueSafelyAsync();
    }

    [RelayCommand]
    private void CancelItem(CompressionQueueItemViewModel? item)
    {
        if (item is null)
            return;

        if (item.CanCancel)
            item.CancelActiveEncode();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task RemoveItemAsync(CompressionQueueItemViewModel? item)
    {
        if (item is null || !item.CanRemove)
            return;

        UnregisterItem(item);
        _items.Remove(item);
        await PersistQueueSafelyAsync();
    }

    public void Dispose()
    {
        _items.CollectionChanged -= OnItemsCollectionChanged;
        _executionCoordinator.PropertyChanged -= OnExecutionCoordinatorPropertyChanged;

        foreach (CompressionQueueItemViewModel item in Items)
        {
            UnregisterItem(item);
            item.CancelActiveEncode();
        }
    }

    private void LoadPersistedQueueItems()
    {
        if (_settingsCoordinator?.Current.CompressionQueueItems is not { Length: > 0 } records)
            return;

        foreach (QueuedCompressionItemRecord record in records)
        {
            CompressionQueueItemViewModel item = CompressionQueueItemViewModel.FromRecord(record);
            RegisterItem(item);
            _items.Add(item);
        }
    }

    private bool TryGetNextQueuedItem(out CompressionQueueItemViewModel? item)
    {
        item = Items.FirstOrDefault(candidate => candidate.Status == CompressionQueueItemStatus.Queued);
        return item is not null;
    }

    private async Task<bool> RunQueueItemAsync(CompressionQueueItemViewModel item)
    {
        if (!File.Exists(item.InputPath))
        {
            item.MarkFailed("Source file was not found.");
            await PersistQueueSafelyAsync();
            return false;
        }

        using var encodeCts = new CancellationTokenSource();
        Stopwatch stopwatch = Stopwatch.StartNew();
        item.AttachCancellationSource(encodeCts);
        item.MarkEncoding();
        await PersistQueueSafelyAsync();

        try
        {
            ProcessingPipelineResult result = await _encodingService.RunAsync(
                item.BuildEncodeRequest(),
                NullLogger<ProcessingPipeline>.Instance,
                new QueueProgressHandler(item),
                encodeCts.Token);

            stopwatch.Stop();
            item.MarkCompleted(result, stopwatch.Elapsed);
            await TryMarkVideoAsProcessedAsync(item.InputPath);
            return true;
        }
        catch (OperationCanceledException) when (encodeCts.IsCancellationRequested)
        {
            stopwatch.Stop();
            item.MarkCancelled();
            return false;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            item.MarkFailed(NormalizeFailureMessage(ex));
            return false;
        }
        finally
        {
            item.DetachCancellationSource(encodeCts);
            await PersistQueueSafelyAsync();
        }
    }

    private async Task TryMarkVideoAsProcessedAsync(string inputPath)
    {
        try
        {
            await _processedVideoTracker.MarkProcessedAsync(inputPath);
        }
        catch
        {
            // Ignore persistence failures and keep the successful queue state.
        }
    }

    private async Task PersistQueueSafelyAsync()
    {
        if (_settingsCoordinator is null)
            return;

        try
        {
            QueuedCompressionItemRecord[] queueItems = Items
                .Where(item => item.PersistsAcrossSessions)
                .Select(item => item.ToRecord())
                .ToArray();

            await _settingsCoordinator.UpdateAsync(settings => settings with
            {
                CompressionQueueItems = queueItems
            }).ConfigureAwait(false);
        }
        catch
        {
            // Ignore persistence failures and keep the in-memory queue.
        }
    }

    private void RegisterItem(CompressionQueueItemViewModel item)
    {
        item.AttachActions(CancelItemCore, RemoveItemAsync);
        item.PropertyChanged += OnQueueItemPropertyChanged;
    }

    private void UnregisterItem(CompressionQueueItemViewModel item)
    {
        item.PropertyChanged -= OnQueueItemPropertyChanged;
        item.AttachActions(null, null);
    }

    private void CancelItemCore(CompressionQueueItemViewModel item)
    {
        if (item.CanCancel)
            item.CancelActiveEncode();
    }

    private void NotifyQueueStateChanged()
    {
        CompressAllCommand.NotifyCanExecuteChanged();
        ClearQueueCommand.NotifyCanExecuteChanged();
        CancelItemCommand.NotifyCanExecuteChanged();
        RemoveItemCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(QueueCount));
        OnPropertyChanged(nameof(ActiveItemCount));
        OnPropertyChanged(nameof(WaitingItemCount));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(QueueCountText));
        OnPropertyChanged(nameof(QueueSummaryText));
        OnPropertyChanged(nameof(CanCompressAll));
        OnPropertyChanged(nameof(CanClearQueue));
        OnPropertyChanged(nameof(IsBlockedByAnotherEncode));
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => NotifyQueueStateChanged();

    private void OnQueueItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CompressionQueueItemViewModel.Status))
            NotifyQueueStateChanged();
    }

    private void OnExecutionCoordinatorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EncodeExecutionCoordinator.IsBusy))
            NotifyQueueStateChanged();
    }

    private static string NormalizeFailureMessage(Exception ex) =>
        string.IsNullOrWhiteSpace(ex.Message)
            ? "Compression failed."
            : ex.Message.Trim();

    private sealed class QueueProgressHandler(CompressionQueueItemViewModel item) : IProgress<EncodeProgress>
    {
        public void Report(EncodeProgress value)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                item.UpdateProgress(value);
                return;
            }

            Dispatcher.UIThread.Post(() => item.UpdateProgress(value));
        }
    }
}
