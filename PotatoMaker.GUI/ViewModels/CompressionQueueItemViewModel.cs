using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PotatoMaker.Core;
using PotatoMaker.GUI.Services;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Represents one row on the compression queue screen.
/// </summary>
public partial class CompressionQueueItemViewModel : ViewModelBase
{
    private const string WaitingInQueueText = "Waiting in queue";
    private CancellationTokenSource? _encodeCts;
    private Action<CompressionQueueItemViewModel>? _cancelAction;
    private Func<CompressionQueueItemViewModel, Task>? _removeAction;

    private CompressionQueueItemViewModel(
        string id,
        string inputPath,
        string outputDirectory,
        VideoInfo info,
        StrategyAnalysis strategy,
        EncodeSettings settings,
        VideoClipRange clipRange,
        long selectedSizeBytes,
        CompressionQueueItemStatus status,
        int progressPercent,
        string progressStateText,
        long? outputSizeBytes,
        string? failureMessage,
        DateTimeOffset addedAtUtc)
    {
        Id = id;
        InputPath = Path.GetFullPath(inputPath);
        OutputDirectory = Path.GetFullPath(outputDirectory);
        Info = info;
        Strategy = strategy;
        Settings = settings;
        ClipRange = clipRange.Normalize(info.Duration);
        SelectedSizeBytes = Math.Max(0, selectedSizeBytes);
        AddedAtUtc = addedAtUtc;
        _status = status;
        _progressPercent = Math.Clamp(progressPercent, 0, 100);
        _progressStateText = string.IsNullOrWhiteSpace(progressStateText)
            ? ResolveDefaultProgressState(status, failureMessage)
            : progressStateText;
        _outputSizeBytes = outputSizeBytes;
        _failureMessage = failureMessage;
        CancelCommand = new RelayCommand(ExecuteCancel, CanExecuteCancel);
        RemoveCommand = new AsyncRelayCommand(ExecuteRemoveAsync, CanExecuteRemove);
    }

    public string Id { get; }

    public string InputPath { get; }

    public string OutputDirectory { get; }

    public VideoInfo Info { get; }

    public StrategyAnalysis Strategy { get; }

    public EncodeSettings Settings { get; }

    public VideoClipRange ClipRange { get; }

    public long SelectedSizeBytes { get; }

    public DateTimeOffset AddedAtUtc { get; }

    public string FileName => Path.GetFileName(InputPath);

    public string SelectedSizeText => FormatFileSize(SelectedSizeBytes);

    public string OutputSizeText => OutputSizeBytes is long bytes
        ? FormatFileSize(bytes)
        : "--";

    public string ElapsedText => string.IsNullOrWhiteSpace(ElapsedDisplay)
        ? "--"
        : ElapsedDisplay;

    public string ProgressText => ProgressStateText;

    public bool IsWaiting => Status == CompressionQueueItemStatus.Queued;

    public bool IsEncoding => Status == CompressionQueueItemStatus.Encoding;

    public bool IsCompleted => Status == CompressionQueueItemStatus.Completed;

    public bool IsCancelled => Status == CompressionQueueItemStatus.Cancelled;

    public bool IsFailed => Status == CompressionQueueItemStatus.Failed;

    public bool CanCancel => Status == CompressionQueueItemStatus.Encoding;

    public bool CanRemove => Status != CompressionQueueItemStatus.Encoding;

    public bool BlocksDuplicateEntries => Status is CompressionQueueItemStatus.Queued or CompressionQueueItemStatus.Encoding;

    public bool PersistsAcrossSessions => Status is CompressionQueueItemStatus.Queued or CompressionQueueItemStatus.Encoding;

    public RelayCommand CancelCommand { get; }

    public AsyncRelayCommand RemoveCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaiting))]
    [NotifyPropertyChangedFor(nameof(IsEncoding))]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(IsCancelled))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    [NotifyPropertyChangedFor(nameof(PersistsAcrossSessions))]
    private CompressionQueueItemStatus _status;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputSizeText))]
    private long? _outputSizeBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private string _progressStateText;

    [ObservableProperty]
    private string? _failureMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ElapsedText))]
    private string? _elapsedDisplay;

    public static CompressionQueueItemViewModel Create(QueuedCompressionItemDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return new CompressionQueueItemViewModel(
            Guid.NewGuid().ToString("N"),
            draft.InputPath,
            draft.OutputDirectory,
            draft.Info,
            draft.Strategy,
            draft.Settings,
            draft.ClipRange,
            draft.SelectedSizeBytes,
            CompressionQueueItemStatus.Queued,
            0,
            WaitingInQueueText,
            null,
            null,
            DateTimeOffset.UtcNow);
    }

    public static CompressionQueueItemViewModel FromRecord(QueuedCompressionItemRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        CompressionQueueItemStatus status = record.Status == CompressionQueueItemStatus.Encoding
            ? CompressionQueueItemStatus.Queued
            : record.Status;
        int progressPercent = status == CompressionQueueItemStatus.Queued
            ? 0
            : Math.Clamp(record.ProgressPercent, 0, 100);
        string progressStateText = status == CompressionQueueItemStatus.Queued
            ? WaitingInQueueText
            : record.ProgressStateText;

        return new CompressionQueueItemViewModel(
            record.Id,
            record.InputPath,
            record.OutputDirectory,
            record.Info,
            record.Strategy,
            record.Settings,
            new VideoClipRange(
                TimeSpan.FromTicks(record.ClipStartTicks),
                TimeSpan.FromTicks(record.ClipEndTicks)),
            record.SelectedSizeBytes,
            status,
            progressPercent,
            progressStateText,
            record.OutputSizeBytes,
            record.FailureMessage,
            record.AddedAtUtc);
    }

    public static string BuildDuplicateKey(QueuedCompressionItemDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return BuildDuplicateKey(
            draft.InputPath,
            draft.OutputDirectory,
            draft.ClipRange.Normalize(draft.Info.Duration),
            draft.Settings);
    }

    public string DuplicateKey => BuildDuplicateKey(InputPath, OutputDirectory, ClipRange, Settings);

    public EncodeRequest BuildEncodeRequest() =>
        new(
            InputPath,
            OutputDirectory,
            Info,
            Strategy,
            Settings,
            ClipRange);

    public QueuedCompressionItemRecord ToRecord() =>
        new(
            Id,
            InputPath,
            OutputDirectory,
            Info,
            Strategy,
            Settings,
            ClipRange.Start.Ticks,
            ClipRange.End.Ticks,
            SelectedSizeBytes,
            Status == CompressionQueueItemStatus.Encoding
                ? CompressionQueueItemStatus.Queued
                : Status,
            Status == CompressionQueueItemStatus.Encoding ? 0 : ProgressPercent,
            Status == CompressionQueueItemStatus.Encoding ? WaitingInQueueText : ProgressStateText,
            OutputSizeBytes,
            FailureMessage,
            AddedAtUtc);

    public void MarkEncoding()
    {
        FailureMessage = null;
        OutputSizeBytes = null;
        ElapsedDisplay = null;
        ProgressPercent = 0;
        ProgressStateText = "Preparing...";
        Status = CompressionQueueItemStatus.Encoding;
    }

    public void UpdateProgress(EncodeProgress value)
    {
        if (Status != CompressionQueueItemStatus.Encoding)
            return;

        Status = CompressionQueueItemStatus.Encoding;
        ProgressPercent = Math.Clamp(value.Percent, 0, 100);
        ProgressStateText = BuildProgressText(value.Label, ProgressPercent);
    }

    public void MarkCompleted(ProcessingPipelineResult result, TimeSpan? elapsed = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        FailureMessage = null;
        ProgressPercent = 100;
        OutputSizeBytes = result.TotalOutputBytes;
        ElapsedDisplay = elapsed is { } value ? FormatElapsed(value) : null;
        ProgressStateText = "Done";
        Status = CompressionQueueItemStatus.Completed;
    }

    public void MarkCancelled()
    {
        FailureMessage = null;
        ProgressPercent = 0;
        OutputSizeBytes = null;
        ElapsedDisplay = null;
        ProgressStateText = "Cancelled";
        Status = CompressionQueueItemStatus.Cancelled;
    }

    public void MarkFailed(string? message)
    {
        string normalizedMessage = string.IsNullOrWhiteSpace(message)
            ? "Compression failed."
            : message.Trim();

        FailureMessage = normalizedMessage;
        ProgressPercent = 0;
        OutputSizeBytes = null;
        ElapsedDisplay = null;
        ProgressStateText = normalizedMessage;
        Status = CompressionQueueItemStatus.Failed;
    }

    public void CancelActiveEncode() => _encodeCts?.Cancel();

    internal void AttachActions(
        Action<CompressionQueueItemViewModel>? cancelAction,
        Func<CompressionQueueItemViewModel, Task>? removeAction)
    {
        _cancelAction = cancelAction;
        _removeAction = removeAction;
        NotifyActionCommandStateChanged();
    }

    internal void AttachCancellationSource(CancellationTokenSource encodeCts) => _encodeCts = encodeCts;

    internal void DetachCancellationSource(CancellationTokenSource encodeCts)
    {
        if (ReferenceEquals(_encodeCts, encodeCts))
            _encodeCts = null;
    }

    partial void OnStatusChanged(CompressionQueueItemStatus value) => NotifyActionCommandStateChanged();

    private static string BuildDuplicateKey(
        string inputPath,
        string outputDirectory,
        VideoClipRange clipRange,
        EncodeSettings settings)
    {
        string normalizedInputPath = Path.GetFullPath(inputPath);
        string normalizedOutputDirectory = Path.GetFullPath(outputDirectory);

        return string.Join(
            "|",
            normalizedInputPath.ToUpperInvariant(),
            normalizedOutputDirectory.ToUpperInvariant(),
            clipRange.Start.Ticks,
            clipRange.End.Ticks,
            settings.Encoder,
            settings.FrameRateMode,
            EncodeSettings.NormalizeOutputNameAffix(settings.OutputNamePrefix),
            EncodeSettings.NormalizeOutputNameAffix(settings.OutputNameSuffix),
            EncodeSettings.NormalizeSvtAv1Preset(settings.SvtAv1Preset));
    }

    private static string ResolveDefaultProgressState(CompressionQueueItemStatus status, string? failureMessage) => status switch
    {
        CompressionQueueItemStatus.Encoding => "Preparing...",
        CompressionQueueItemStatus.Completed => "Done",
        CompressionQueueItemStatus.Cancelled => "Cancelled",
        CompressionQueueItemStatus.Failed => string.IsNullOrWhiteSpace(failureMessage) ? "Compression failed." : failureMessage,
        _ => WaitingInQueueText
    };

    private static string BuildProgressText(string? label, int progressPercent)
    {
        if (!string.IsNullOrWhiteSpace(label) &&
            label.Contains("analy", StringComparison.OrdinalIgnoreCase))
        {
            return progressPercent > 0
                ? $"Analyzing... {progressPercent}%"
                : "Analyzing...";
        }

        return progressPercent > 0
            ? $"Encoding... {progressPercent}%"
            : "Encoding...";
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B"
    };

    private static string FormatElapsed(TimeSpan value)
    {
        int roundedSeconds = Math.Max(1, (int)Math.Round(value.TotalSeconds, MidpointRounding.AwayFromZero));
        var rounded = TimeSpan.FromSeconds(roundedSeconds);

        return rounded.TotalHours >= 1
            ? rounded.ToString(@"h\:mm\:ss")
            : rounded.ToString(@"m\:ss");
    }

    private bool CanExecuteCancel() => CanCancel && _cancelAction is not null;

    private void ExecuteCancel()
    {
        if (CanExecuteCancel())
            _cancelAction!(this);
    }

    private bool CanExecuteRemove() => CanRemove && _removeAction is not null;

    private Task ExecuteRemoveAsync() =>
        CanExecuteRemove()
            ? _removeAction!(this)
            : Task.CompletedTask;

    private void NotifyActionCommandStateChanged()
    {
        CancelCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
    }
}
