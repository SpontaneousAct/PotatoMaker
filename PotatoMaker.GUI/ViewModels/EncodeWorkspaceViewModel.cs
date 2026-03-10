using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PotatoMaker.Core;
using PotatoMaker.GUI.Services;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Coordinates file analysis and encoding for the main screen.
/// </summary>
public partial class EncodeWorkspaceViewModel : ViewModelBase
{
    private readonly IVideoAnalysisService _analysisService;
    private readonly IVideoEncodingService _encodingService;
    private readonly IVideoFramePreviewService _framePreviewService;
    private readonly IEncoderCapabilityService _encoderCapabilityService;
    private readonly IAppSettingsCoordinator? _settingsCoordinator;
    private readonly bool _initializeEncoderSupport;
    private CancellationTokenSource? _encodeCts;
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _startFramePreviewCts;
    private CancellationTokenSource? _endFramePreviewCts;
    private int _previewVersion;
    private int _startFramePreviewVersion;
    private int _endFramePreviewVersion;
    private bool _isApplyingSettings;

    private static readonly TimeSpan FramePreviewDebounce = TimeSpan.FromMilliseconds(200);

    public EncodeWorkspaceViewModel()
        : this(
            new VideoAnalysisService(),
            new VideoEncodingService(),
            new VideoFramePreviewService(),
            new EncoderCapabilityService(),
            null,
            initializeEncoderSupport: true)
    {
    }

    public EncodeWorkspaceViewModel(
        IVideoAnalysisService analysisService,
        IVideoEncodingService encodingService,
        IVideoFramePreviewService framePreviewService,
        IEncoderCapabilityService encoderCapabilityService,
        IAppSettingsCoordinator? settingsCoordinator,
        bool initializeEncoderSupport = true)
    {
        _analysisService = analysisService;
        _encodingService = encodingService;
        _framePreviewService = framePreviewService;
        _encoderCapabilityService = encoderCapabilityService;
        _settingsCoordinator = settingsCoordinator;
        _initializeEncoderSupport = initializeEncoderSupport;

        FileInput.FileSelected += OnFileSelected;
        FileInput.FileCleared += OnFileCleared;
        FileInput.PropertyChanged += OnEncodePrerequisiteChanged;
        ClipRange.SelectionChanged += OnClipRangeSelectionChanged;
        ClipRange.PreviewCommitRequested += OnClipPreviewCommitRequested;
        VideoSummary.PropertyChanged += OnEncodePrerequisiteChanged;
        ConversionLog.PropertyChanged += OnEncodePrerequisiteChanged;
        OutputSettings.PropertyChanged += OnOutputSettingsChanged;

        ApplyInitialSettings();
        if (_initializeEncoderSupport)
            _ = InitializeEncoderSupportAsync();
    }

    public FileInputViewModel FileInput { get; } = new();

    public VideoSummaryViewModel VideoSummary { get; } = new();

    public ClipRangeViewModel ClipRange { get; } = new();

    public OutputSettingsViewModel OutputSettings { get; } = new();

    public ConversionLogViewModel ConversionLog { get; } = new();

    public bool IsEncodeInProgress => ConversionLog.IsProcessing;

    public bool IsEncodeIdle => !ConversionLog.IsProcessing;

    public string EncodeButtonText => IsEncodeInProgress ? "Cancel Compression" : "Start Compression";

    public ICommand EncodeButtonCommand => IsEncodeInProgress ? CancelEncodeCommand : StartEncodeCommand;

    [RelayCommand(CanExecute = nameof(CanStartEncode))]
    private async Task StartEncode()
    {
        VideoInfo? info = VideoSummary.Info;
        string? path = FileInput.InputFilePath;
        StrategyAnalysis? strategy = VideoSummary.StrategyAnalysis;
        if (info is null || path is null || strategy is null)
            return;

        _encodeCts = new CancellationTokenSource();
        ConversionLog.Clear();
        ConversionLog.IsProcessing = true;
        NotifyEncodeStateChanged();

        try
        {
            string outputFolder = OutputSettings.OutputFolderPath
                ?? Path.GetDirectoryName(Path.GetFullPath(path))
                ?? ".";

            var request = new EncodeRequest(
                path,
                outputFolder,
                info,
                strategy,
                BuildEncodeSettings(),
                ClipRange.Selection);

            await _encodingService.RunAsync(
                request,
                new ViewModelLogger(ConversionLog),
                new ViewModelProgressHandler(ConversionLog),
                _encodeCts.Token);

            ConversionLog.AddLog("Done!");
        }
        catch (OperationCanceledException)
        {
            ConversionLog.AddLog("Cancelled by user.");
        }
        catch (Exception ex)
        {
            ConversionLog.AddLog($"Error: {ex.Message}");
        }
        finally
        {
            ConversionLog.IsProcessing = false;
            ConversionLog.ProgressPercent = 0;
            ConversionLog.ProgressLabel = null;
            _encodeCts.Dispose();
            _encodeCts = null;
            NotifyEncodeStateChanged();
        }
    }

    private bool CanStartEncode() =>
        FileInput.HasFile && VideoSummary.HasData && VideoSummary.HasStrategy && !ConversionLog.IsProcessing;

    [RelayCommand(CanExecute = nameof(CanCancelEncode))]
    private void CancelEncode()
    {
        if (_encodeCts is null || _encodeCts.IsCancellationRequested)
            return;

        ConversionLog.AddLog("Cancellation requested...");
        _encodeCts.Cancel();
        NotifyEncodeStateChanged();
    }

    private bool CanCancelEncode() =>
        ConversionLog.IsProcessing && _encodeCts is not null && !_encodeCts.IsCancellationRequested;

    private async void OnFileSelected(string path)
    {
        await LoadSelectedFileAsync(path);
    }

    private async Task LoadSelectedFileAsync(string path)
    {
        CancelPendingPreview();
        var previewCts = new CancellationTokenSource();
        _previewCts = previewCts;
        int previewVersion = Interlocked.Increment(ref _previewVersion);

        try
        {
            OutputSettings.SetSourceFolder(Path.GetDirectoryName(Path.GetFullPath(path)));

            VideoSummary.Clear();
            ClipRange.Clear();
            VideoInfo info = await _analysisService.ProbeAsync(path, previewCts.Token);
            if (ShouldIgnorePreviewResult(previewCts, previewVersion, path))
                return;

            VideoSummary.SetProbeResult(path, info);
            ClipRange.SetSourceDuration(info.Duration);
            VideoSummary.SetSelectedRange(ClipRange.Selection, info.Duration);
            RefreshFramePreviews(ClipPreviewTarget.Start | ClipPreviewTarget.End, immediate: true);

            await RefreshStrategyPreviewAsync(path, info, previewCts, previewVersion);
        }
        catch (OperationCanceledException) when (previewCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (previewVersion != _previewVersion)
                return;

            if (VideoSummary.Info is null)
            {
                VideoSummary.Clear();
                ConversionLog.AddLog($"Error probing file: {ex.Message}");
            }
            else
            {
                VideoSummary.ClearStrategy();
                ConversionLog.AddLog($"Error building strategy preview: {ex.Message}");
            }
        }
        finally
        {
            if (ReferenceEquals(_previewCts, previewCts))
                _previewCts = null;

            previewCts.Dispose();
        }
    }

    private void OnFileCleared()
    {
        CancelPendingPreview();
        CancelFramePreviewRequests();
        OutputSettings.SetSourceFolder(null);
        ClipRange.Clear();
        VideoSummary.Clear();
        ConversionLog.Clear();
        NotifyEncodeStateChanged();
    }

    private async void OnClipRangeSelectionChanged(ClipPreviewTarget changedTargets)
    {
        string? path = FileInput.InputFilePath;
        VideoInfo? info = VideoSummary.Info;
        if (path is null || info is null)
            return;

        RefreshFramePreviews(changedTargets, immediate: false);

        CancelPendingPreview();
        var previewCts = new CancellationTokenSource();
        _previewCts = previewCts;
        int previewVersion = Interlocked.Increment(ref _previewVersion);

        try
        {
            await RefreshStrategyPreviewAsync(path, info, previewCts, previewVersion);
        }
        catch (OperationCanceledException) when (previewCts.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_previewCts, previewCts))
                _previewCts = null;

            previewCts.Dispose();
        }
    }

    private void OnClipPreviewCommitRequested(ClipPreviewTarget target)
    {
        RefreshFramePreviews(target, immediate: true);
    }

    private void CancelPendingPreview()
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;
    }

    private void CancelFramePreviewRequests()
    {
        CancelFramePreviewRequest(ClipPreviewTarget.Start);
        CancelFramePreviewRequest(ClipPreviewTarget.End);
    }

    private void CancelFramePreviewRequest(ClipPreviewTarget target)
    {
        CancellationTokenSource? cts = target == ClipPreviewTarget.Start
            ? _startFramePreviewCts
            : _endFramePreviewCts;

        cts?.Cancel();
        cts?.Dispose();

        if (target == ClipPreviewTarget.Start)
            _startFramePreviewCts = null;
        else if (target == ClipPreviewTarget.End)
            _endFramePreviewCts = null;
    }

    private bool ShouldIgnorePreviewResult(CancellationTokenSource previewCts, int previewVersion, string path) =>
        previewCts.IsCancellationRequested ||
        previewVersion != _previewVersion ||
        !string.Equals(FileInput.InputFilePath, path, StringComparison.OrdinalIgnoreCase);

    private async Task RefreshStrategyPreviewAsync(
        string path,
        VideoInfo info,
        CancellationTokenSource previewCts,
        int previewVersion)
    {
        VideoSummary.SetSelectedRange(ClipRange.Selection, info.Duration);
        VideoSummary.SetStrategyPending();

        StrategyAnalysis strategy = await _analysisService.AnalyzeStrategyAsync(
            path,
            info,
            BuildEncodeSettings(),
            ClipRange.Selection,
            previewCts.Token);

        if (ShouldIgnorePreviewResult(previewCts, previewVersion, path))
            return;

        VideoSummary.SetStrategyResult(strategy);
    }

    private void RefreshFramePreviews(ClipPreviewTarget targets, bool immediate)
    {
        if ((targets & ClipPreviewTarget.Start) != 0)
            QueueFramePreviewRefresh(ClipPreviewTarget.Start, immediate);

        if ((targets & ClipPreviewTarget.End) != 0)
            QueueFramePreviewRefresh(ClipPreviewTarget.End, immediate);
    }

    private void QueueFramePreviewRefresh(ClipPreviewTarget target, bool immediate)
    {
        string? path = FileInput.InputFilePath;
        if (path is null || !ClipRange.HasDuration)
        {
            ClipRange.SetPreview(target, null, "Load a video to see a preview frame.");
            return;
        }

        CancelFramePreviewRequest(target);

        var cts = new CancellationTokenSource();
        int version;
        if (target == ClipPreviewTarget.Start)
        {
            _startFramePreviewCts = cts;
            version = Interlocked.Increment(ref _startFramePreviewVersion);
        }
        else
        {
            _endFramePreviewCts = cts;
            version = Interlocked.Increment(ref _endFramePreviewVersion);
        }

        ClipRange.SetPreviewLoading(target, true);
        _ = RunFramePreviewRefreshAsync(
            path,
            target,
            ClipRange.ResolvePreviewPosition(target),
            cts,
            version,
            immediate);
    }

    private async Task RunFramePreviewRefreshAsync(
        string path,
        ClipPreviewTarget target,
        TimeSpan position,
        CancellationTokenSource cts,
        int version,
        bool immediate)
    {
        try
        {
            if (!immediate)
                await Task.Delay(FramePreviewDebounce, cts.Token);

            VideoFramePreviewResult result = await _framePreviewService.GenerateAsync(path, position, cts.Token);

            if (ShouldIgnoreFramePreviewResult(target, cts, version, path))
            {
                result.Bitmap?.Dispose();
                return;
            }

            ClipRange.SetPreview(target, result.Bitmap, result.ErrorMessage);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (ShouldIgnoreFramePreviewResult(target, cts, version, path))
                return;

            ClipRange.SetPreview(target, null, ex.Message);
        }
        finally
        {
            if (target == ClipPreviewTarget.Start)
            {
                if (ReferenceEquals(_startFramePreviewCts, cts))
                    _startFramePreviewCts = null;
            }
            else if (ReferenceEquals(_endFramePreviewCts, cts))
            {
                _endFramePreviewCts = null;
            }

            cts.Dispose();
        }
    }

    private bool ShouldIgnoreFramePreviewResult(
        ClipPreviewTarget target,
        CancellationTokenSource cts,
        int version,
        string path)
    {
        if (cts.IsCancellationRequested ||
            !string.Equals(FileInput.InputFilePath, path, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return target == ClipPreviewTarget.Start
            ? version != _startFramePreviewVersion
            : version != _endFramePreviewVersion;
    }

    private void OnEncodePrerequisiteChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileInputViewModel.HasFile)
            or nameof(VideoSummaryViewModel.HasData)
            or nameof(VideoSummaryViewModel.HasStrategy)
            or nameof(ConversionLogViewModel.IsProcessing))
        {
            NotifyEncodeStateChanged();
        }
    }

    private void OnOutputSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OutputSettingsViewModel.UseNvencEncoder)
            or nameof(OutputSettingsViewModel.CustomOutputFolder))
        {
            PersistWorkspaceSettingsSafely();
        }
    }

    private void ApplyInitialSettings()
    {
        AppSettings settings = _settingsCoordinator?.Current ?? new AppSettings();

        _isApplyingSettings = true;
        try
        {
            OutputSettings.CustomOutputFolder = settings.LastOutputFolder;
            OutputSettings.UseNvencEncoder = settings.UseNvencEncoder;
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private EncodeSettings BuildEncodeSettings() => new()
    {
        Encoder = OutputSettings.UseNvencEncoder && OutputSettings.CanUseNvenc
            ? EncoderChoice.Nvenc
            : EncoderChoice.SvtAv1
    };

    private async Task InitializeEncoderSupportAsync()
    {
        bool supportsNvencAv1;

        try
        {
            supportsNvencAv1 = await _encoderCapabilityService.IsAv1NvencSupportedAsync().ConfigureAwait(false);
        }
        catch
        {
            supportsNvencAv1 = false;
        }

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            bool wasUsingNvenc = OutputSettings.UseNvencEncoder;
            OutputSettings.SetNvencSupport(supportsNvencAv1);

            if (wasUsingNvenc && !OutputSettings.UseNvencEncoder)
                PersistWorkspaceSettingsSafely();
        });
    }

    private void PersistWorkspaceSettingsSafely()
    {
        if (_isApplyingSettings || _settingsCoordinator is null)
            return;

        _ = PersistWorkspaceSettingsAsync();
    }

    private async Task PersistWorkspaceSettingsAsync()
    {
        try
        {
            await _settingsCoordinator!.UpdateAsync(settings => settings with
            {
                UseNvencEncoder = OutputSettings.UseNvencEncoder,
                LastOutputFolder = OutputSettings.CustomOutputFolder
            }).ConfigureAwait(false);
        }
        catch
        {
            // Ignore persistence failures and keep the in-memory values.
        }
    }

    private void NotifyEncodeStateChanged()
    {
        StartEncodeCommand.NotifyCanExecuteChanged();
        CancelEncodeCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsEncodeInProgress));
        OnPropertyChanged(nameof(IsEncodeIdle));
        OnPropertyChanged(nameof(EncodeButtonText));
        OnPropertyChanged(nameof(EncodeButtonCommand));
    }
}
