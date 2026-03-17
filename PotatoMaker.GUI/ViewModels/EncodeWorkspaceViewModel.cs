using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PotatoMaker.Core;
using PotatoMaker.GUI.Services;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Coordinates file analysis and encoding for the main screen.
/// </summary>
public partial class EncodeWorkspaceViewModel : ViewModelBase, IDisposable
{
    private readonly IVideoAnalysisService _analysisService;
    private readonly IVideoEncodingService _encodingService;
    private readonly IEncoderCapabilityService _encoderCapabilityService;
    private readonly IEncodeCompletionNotifier _encodeCompletionNotifier;
    private readonly IProcessedVideoTracker _processedVideoTracker;
    private readonly IAppSettingsCoordinator? _settingsCoordinator;
    private readonly bool _initializeEncoderSupport;
    private readonly TimeSpan _cancelledStatusDuration;
    private CancellationTokenSource? _encodeCts;
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _statusResetCts;
    private Stopwatch? _encodeStopwatch;
    private int _previewVersion;
    private bool _isApplyingSettings;
    private bool _isCropDetectionPending;
    private string? _detectedCropFilter;

    public EncodeWorkspaceViewModel()
        : this(
            new VideoAnalysisService(),
            new VideoEncodingService(),
            new VideoPlayerViewModel(initializePlayer: false),
            new EncoderCapabilityService(),
            null,
            initializeEncoderSupport: true,
            NoOpEncodeCompletionNotifier.Instance,
            null,
            DisabledProcessedVideoTracker.Instance)
    {
    }

    public EncodeWorkspaceViewModel(
        IVideoAnalysisService analysisService,
        IVideoEncodingService encodingService,
        IEncoderCapabilityService encoderCapabilityService,
        IAppSettingsCoordinator? settingsCoordinator,
        bool initializeEncoderSupport = true,
        IEncodeCompletionNotifier? encodeCompletionNotifier = null,
        TimeSpan? cancelledStatusDuration = null,
        IProcessedVideoTracker? processedVideoTracker = null)
        : this(
            analysisService,
            encodingService,
            new VideoPlayerViewModel(initializePlayer: false),
            encoderCapabilityService,
            settingsCoordinator,
            initializeEncoderSupport,
            encodeCompletionNotifier,
            cancelledStatusDuration,
            processedVideoTracker)
    {
    }

    public EncodeWorkspaceViewModel(
        IVideoAnalysisService analysisService,
        IVideoEncodingService encodingService,
        VideoPlayerViewModel videoPlayer,
        IEncoderCapabilityService encoderCapabilityService,
        IAppSettingsCoordinator? settingsCoordinator,
        bool initializeEncoderSupport = true,
        IEncodeCompletionNotifier? encodeCompletionNotifier = null,
        TimeSpan? cancelledStatusDuration = null,
        IProcessedVideoTracker? processedVideoTracker = null)
    {
        _analysisService = analysisService;
        _encodingService = encodingService;
        VideoPlayer = videoPlayer;
        _encoderCapabilityService = encoderCapabilityService;
        _encodeCompletionNotifier = encodeCompletionNotifier ?? NoOpEncodeCompletionNotifier.Instance;
        _processedVideoTracker = processedVideoTracker ?? DisabledProcessedVideoTracker.Instance;
        _settingsCoordinator = settingsCoordinator;
        _initializeEncoderSupport = initializeEncoderSupport;
        _cancelledStatusDuration = cancelledStatusDuration ?? TimeSpan.FromSeconds(5);

        FileInput.FileSelected += OnFileSelected;
        FileInput.FileCleared += OnFileCleared;
        FileInput.PropertyChanged += OnEncodePrerequisiteChanged;
        FileInput.PropertyChanged += OnResetStateChanged;
        ClipRange.SelectionChanged += OnClipRangeSelectionChanged;
        ClipRange.PropertyChanged += OnResetStateChanged;
        VideoPlayer.TrimBoundaryRequested += OnTrimBoundaryRequested;
        VideoPlayer.PropertyChanged += OnResetStateChanged;
        VideoPlayer.PropertyChanged += OnVideoPlayerSettingChanged;
        VideoSummary.PropertyChanged += OnEncodePrerequisiteChanged;
        ConversionLog.PropertyChanged += OnEncodePrerequisiteChanged;
        OutputSettings.PropertyChanged += OnOutputSettingsChanged;

        ApplyInitialSettings();
        UpdateSourceSelectionState();
        UpdateConversionIdlePrompt();
        if (_initializeEncoderSupport)
            _ = InitializeEncoderSupportAsync();
    }

    public FileInputViewModel FileInput { get; } = new();

    public VideoSummaryViewModel VideoSummary { get; } = new();

    public VideoPlayerViewModel VideoPlayer { get; }

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

        EncodeSettings settings = BuildEncodeSettings();
        var encodeCts = new CancellationTokenSource();
        _encodeCts = encodeCts;
        _encodeStopwatch = Stopwatch.StartNew();
        CancelPendingStatusReset();
        ConversionLog.BeginEncoding(
            settings.Encoder == EncoderChoice.SvtAv1 ? ConversionStatus.Analysing : ConversionStatus.Encoding,
            settings.Encoder == EncoderChoice.SvtAv1 ? "1/2" : null);
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
                settings,
                ClipRange.Selection);

            await _encodingService.RunAsync(
                request,
                new ViewModelLogger(ConversionLog),
                new ViewModelProgressHandler(ConversionLog),
                encodeCts.Token);

            _encodeStopwatch?.Stop();
            ConversionLog.MarkDone(_encodeStopwatch?.Elapsed);
            await TryMarkCurrentVideoAsProcessedAsync(path);
            _encodeCompletionNotifier.NotifyEncodeSucceeded();
        }
        catch (OperationCanceledException)
        {
            _encodeStopwatch?.Stop();
            ConversionLog.MarkCancelled();
            ScheduleCancelledStatusReset();
        }
        catch (Exception)
        {
            _encodeStopwatch?.Stop();
            CancelPendingStatusReset();
            ConversionLog.MarkError();
        }
        finally
        {
            _encodeStopwatch = null;
            if (ReferenceEquals(_encodeCts, encodeCts))
                _encodeCts = null;

            encodeCts.Dispose();
            NotifyEncodeStateChanged();
        }
    }

    private bool CanStartEncode() =>
        FileInput.HasFile && VideoSummary.HasData && VideoSummary.HasStrategy && !ConversionLog.IsProcessing;

    [RelayCommand(CanExecute = nameof(CanCancelEncode))]
    private void CancelEncode()
    {
        CancellationTokenSource? encodeCts = _encodeCts;
        if (encodeCts is null || encodeCts.IsCancellationRequested)
            return;

        encodeCts.Cancel();
        NotifyEncodeStateChanged();
    }

    private bool CanCancelEncode() =>
        ConversionLog.IsProcessing && _encodeCts is not null && !_encodeCts.IsCancellationRequested;

    [RelayCommand(CanExecute = nameof(CanResetPreview))]
    private void ResetPreview()
    {
        VideoPlayer.ResetPlayback();

        if (ClipRange.CanResetSelection)
            ClipRange.ResetSelectionCommand.Execute(null);
    }

    private bool CanResetPreview() => FileInput.HasFile && (VideoPlayer.CanResetPlayback || ClipRange.CanResetSelection);

    private async void OnFileSelected(string path)
    {
        await LoadSelectedFileAsync(path);
    }

    private async Task LoadSelectedFileAsync(string path)
    {
        CancelPendingPreview();
        CancelPendingStatusReset();
        var previewCts = new CancellationTokenSource();
        _previewCts = previewCts;
        int previewVersion = Interlocked.Increment(ref _previewVersion);
        ConversionLog.BeginAnalysis();

        try
        {
            OutputSettings.SetSourceFolder(Path.GetDirectoryName(Path.GetFullPath(path)));

            _detectedCropFilter = null;
            _isCropDetectionPending = false;
            VideoSummary.Clear();
            VideoPlayer.Clear();
            ClipRange.Clear();
            VideoInfo info = await _analysisService.ProbeAsync(path, previewCts.Token);
            if (ShouldIgnorePreviewResult(previewCts, previewVersion, path))
                return;

            VideoSummary.SetProbeResult(path, info);
            ClipRange.SetSourceDuration(info.Duration);
            VideoPlayer.LoadSource(path, info.Duration, ClipRange.Selection);
            VideoSummary.SetSelectedRange(ClipRange.Selection, info.Duration);
            ResetPreviewCommand.NotifyCanExecuteChanged();

            VideoSummary.SetStrategyPending();
            _isCropDetectionPending = true;
            _detectedCropFilter = await _analysisService.DetectCropAsync(path, info, previewCts.Token);
            if (ShouldIgnorePreviewResult(previewCts, previewVersion, path))
                return;

            _isCropDetectionPending = false;
            await RefreshStrategyPreviewAsync(path, info, _detectedCropFilter, previewCts, previewVersion, showPendingState: false);
        }
        catch (OperationCanceledException) when (previewCts.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (previewVersion != _previewVersion)
                return;

            if (VideoSummary.Info is null)
            {
                VideoSummary.Clear();
                ConversionLog.MarkAnalysisError();
            }
            else
            {
                _isCropDetectionPending = false;
                VideoSummary.ClearStrategy();
                ConversionLog.MarkAnalysisError();
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
        CancelPendingStatusReset();
        _detectedCropFilter = null;
        _isCropDetectionPending = false;
        OutputSettings.SetSourceFolder(null);
        ClipRange.Clear();
        VideoPlayer.Clear();
        VideoSummary.Clear();
        ConversionLog.Clear();
        ResetPreviewCommand.NotifyCanExecuteChanged();
        NotifyEncodeStateChanged();
    }

    private void OnClipRangeSelectionChanged()
    {
        string? path = FileInput.InputFilePath;
        VideoInfo? info = VideoSummary.Info;
        ResetPreviewCommand.NotifyCanExecuteChanged();
        if (path is null || info is null)
            return;

        VideoPlayer.SetSelection(ClipRange.Selection);
        VideoSummary.SetSelectedRange(ClipRange.Selection, info.Duration);
        if (_isCropDetectionPending)
            return;

        _ = RefreshStrategyPreviewForCurrentStateAsync(path, info, showPendingState: false);
    }

    private void OnTrimBoundaryRequested(ClipBoundary boundary)
    {
        ClipRange.SetBoundary(boundary, VideoPlayer.CurrentPosition);
    }

    public void BeginTrimBoundaryPreview()
    {
        VideoPlayer.BeginTrimPreview();
    }

    public void PreviewTrimBoundary(ClipBoundary boundary, TimeSpan position)
    {
        ClipRange.SetBoundary(boundary, position);
        TimeSpan previewPosition = boundary == ClipBoundary.Start
            ? ClipRange.Start
            : ClipRange.End;
        VideoPlayer.PreviewTrimPosition(previewPosition);
    }

    public void EndTrimBoundaryPreview()
    {
        VideoPlayer.EndTrimPreview();
    }

    private void OnResetStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is FileInputViewModel && e.PropertyName == nameof(FileInputViewModel.HasFile))
        {
            ResetPreviewCommand.NotifyCanExecuteChanged();
            return;
        }

        if (sender is ClipRangeViewModel && e.PropertyName is nameof(ClipRangeViewModel.CanResetSelection) or nameof(ClipRangeViewModel.HasDuration))
        {
            ResetPreviewCommand.NotifyCanExecuteChanged();
            return;
        }

        if (sender is VideoPlayerViewModel && e.PropertyName is nameof(VideoPlayerViewModel.CanResetPlayback)
            or nameof(VideoPlayerViewModel.CanControlPlayback))
        {
            ResetPreviewCommand.NotifyCanExecuteChanged();
        }
    }

    private void CancelPendingPreview()
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;
    }

    private void CancelPendingStatusReset()
    {
        _statusResetCts?.Cancel();
        _statusResetCts?.Dispose();
        _statusResetCts = null;
    }

    private bool ShouldIgnorePreviewResult(CancellationTokenSource previewCts, int previewVersion, string path) =>
        previewCts.IsCancellationRequested ||
        previewVersion != _previewVersion ||
        !string.Equals(FileInput.InputFilePath, path, StringComparison.OrdinalIgnoreCase);

    private async Task RefreshStrategyPreviewAsync(
        string path,
        VideoInfo info,
        string? cropFilter,
        CancellationTokenSource previewCts,
        int previewVersion,
        bool showPendingState)
    {
        ConversionLog.BeginAnalysis();
        VideoSummary.SetSelectedRange(ClipRange.Selection, info.Duration);
        if (showPendingState)
            VideoSummary.SetStrategyPending();

        StrategyAnalysis strategy = await _analysisService.AnalyzeStrategyAsync(
            path,
            info,
            BuildEncodeSettings(),
            cropFilter,
            ClipRange.Selection,
            previewCts.Token);

        if (ShouldIgnorePreviewResult(previewCts, previewVersion, path))
            return;

        VideoSummary.SetStrategyResult(strategy);
        ConversionLog.CompleteAnalysis();
        NotifyEncodeStateChanged();
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
            or nameof(OutputSettingsViewModel.CustomOutputFolder)
            or nameof(OutputSettingsViewModel.SelectedCpuEncodePreset)
            or nameof(OutputSettingsViewModel.SelectedFrameRateOption)
            or nameof(OutputSettingsViewModel.OutputNamePrefix)
            or nameof(OutputSettingsViewModel.OutputNameSuffix))
        {
            PersistWorkspaceSettingsSafely();
        }

        if (!_isApplyingSettings &&
            e.PropertyName == nameof(OutputSettingsViewModel.SelectedFrameRateOption) &&
            FileInput.InputFilePath is { } path &&
            VideoSummary.Info is { } info &&
            !_isCropDetectionPending)
        {
            _ = RefreshStrategyPreviewForCurrentStateAsync(path, info, showPendingState: false);
        }
    }

    private void OnVideoPlayerSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideoPlayerViewModel.VolumePercent))
            PersistWorkspaceSettingsSafely();
    }

    private void ApplyInitialSettings()
    {
        AppSettings settings = _settingsCoordinator?.Current ?? new AppSettings();

        _isApplyingSettings = true;
        try
        {
            OutputSettings.CustomOutputFolder = settings.LastOutputFolder;
            OutputSettings.UseNvencEncoder = settings.UseNvencEncoder;
            OutputSettings.OutputNamePrefix = settings.OutputNamePrefix;
            OutputSettings.OutputNameSuffix = settings.OutputNameSuffix;
            OutputSettings.SetFrameRateMode(settings.FrameRateMode);
            OutputSettings.SetCpuEncodePreset(settings.SvtAv1Preset);
            VideoPlayer.VolumePercent = settings.PreviewVolumePercent;
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
            : EncoderChoice.SvtAv1,
        OutputNamePrefix = EncodeSettings.NormalizeOutputNameAffix(OutputSettings.OutputNamePrefix),
        OutputNameSuffix = EncodeSettings.NormalizeOutputNameAffix(OutputSettings.OutputNameSuffix),
        FrameRateMode = OutputSettings.FrameRateMode,
        SvtAv1Preset = OutputSettings.CpuEncodePreset
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
                OutputNamePrefix = EncodeSettings.NormalizeOutputNameAffix(OutputSettings.OutputNamePrefix),
                OutputNameSuffix = EncodeSettings.NormalizeOutputNameAffix(OutputSettings.OutputNameSuffix),
                FrameRateMode = OutputSettings.FrameRateMode,
                PreviewVolumePercent = VideoPlayer.VolumePercent,
                SvtAv1Preset = OutputSettings.CpuEncodePreset,
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
        UpdateSourceSelectionState();
        UpdateConversionIdlePrompt();
        StartEncodeCommand.NotifyCanExecuteChanged();
        CancelEncodeCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsEncodeInProgress));
        OnPropertyChanged(nameof(IsEncodeIdle));
        OnPropertyChanged(nameof(EncodeButtonText));
        OnPropertyChanged(nameof(EncodeButtonCommand));
    }

    private void UpdateSourceSelectionState()
    {
        FileInput.IsSourceSelectionLocked = ConversionLog.IsProcessing;
    }

    private void ScheduleCancelledStatusReset()
    {
        CancelPendingStatusReset();

        if (_cancelledStatusDuration <= TimeSpan.Zero)
        {
            ResetTerminalStatusIfCurrent(ConversionStatus.Cancelled, null);
            return;
        }

        var resetCts = new CancellationTokenSource();
        _statusResetCts = resetCts;
        _ = ReturnTerminalStatusToIdleAsync(ConversionStatus.Cancelled, resetCts);
    }

    private async Task ReturnTerminalStatusToIdleAsync(ConversionStatus expectedStatus, CancellationTokenSource resetCts)
    {
        try
        {
            await Task.Delay(_cancelledStatusDuration, resetCts.Token);
            if (resetCts.IsCancellationRequested)
                return;

            if (Avalonia.Application.Current is null)
            {
                ResetTerminalStatusIfCurrent(expectedStatus, resetCts);
                return;
            }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                ResetTerminalStatusIfCurrent(expectedStatus, resetCts));
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_statusResetCts, resetCts))
                _statusResetCts = null;

            resetCts.Dispose();
        }
    }

    private void ResetTerminalStatusIfCurrent(ConversionStatus expectedStatus, CancellationTokenSource? resetCts)
    {
        if ((resetCts is not null && resetCts.IsCancellationRequested) ||
            (resetCts is not null && !ReferenceEquals(_statusResetCts, resetCts)) ||
            ConversionLog.Status != expectedStatus ||
            ConversionLog.IsProcessing)
        {
            return;
        }

        ConversionLog.ReturnToIdle();
        UpdateConversionIdlePrompt();
        NotifyEncodeStateChanged();
    }

    private void UpdateConversionIdlePrompt()
    {
        if (ConversionLog.Status != ConversionStatus.Idle)
            return;

        if (!FileInput.HasFile)
        {
            ConversionLog.SetIdleText("Choose a video");
            return;
        }

        ConversionLog.SetIdleText(CanStartEncode() ? "Ready" : "Getting ready");
    }

    private async Task TryMarkCurrentVideoAsProcessedAsync(string path)
    {
        try
        {
            await _processedVideoTracker.MarkProcessedAsync(path);
        }
        catch
        {
            // Ignore persistence failures and keep the successful encode state.
        }
    }

    private async Task RefreshStrategyPreviewForCurrentStateAsync(string path, VideoInfo info)
        => await RefreshStrategyPreviewForCurrentStateAsync(path, info, showPendingState: true);

    private async Task RefreshStrategyPreviewForCurrentStateAsync(string path, VideoInfo info, bool showPendingState)
    {
        try
        {
            ConversionLog.BeginAnalysis();
            VideoSummary.SetSelectedRange(ClipRange.Selection, info.Duration);
            if (showPendingState)
                VideoSummary.SetStrategyPending();

            StrategyAnalysis strategy = await _analysisService.AnalyzeStrategyAsync(
                path,
                info,
                BuildEncodeSettings(),
                _detectedCropFilter,
                ClipRange.Selection);

            if (!string.Equals(FileInput.InputFilePath, path, StringComparison.OrdinalIgnoreCase) ||
                !ReferenceEquals(VideoSummary.Info, info))
            {
                return;
            }

            VideoSummary.SetStrategyResult(strategy);
            ConversionLog.CompleteAnalysis();
            NotifyEncodeStateChanged();
        }
        catch (Exception)
        {
            VideoSummary.ClearStrategy();
            ConversionLog.MarkAnalysisError();
        }
    }

    public void Dispose()
    {
        CancelPendingPreview();
        CancelPendingStatusReset();
        CancellationTokenSource? encodeCts = _encodeCts;
        _encodeCts = null;
        encodeCts?.Cancel();
        encodeCts?.Dispose();
        FileInput.FileSelected -= OnFileSelected;
        FileInput.FileCleared -= OnFileCleared;
        FileInput.PropertyChanged -= OnEncodePrerequisiteChanged;
        FileInput.PropertyChanged -= OnResetStateChanged;
        ClipRange.SelectionChanged -= OnClipRangeSelectionChanged;
        ClipRange.PropertyChanged -= OnResetStateChanged;
        VideoPlayer.TrimBoundaryRequested -= OnTrimBoundaryRequested;
        VideoPlayer.PropertyChanged -= OnResetStateChanged;
        VideoPlayer.PropertyChanged -= OnVideoPlayerSettingChanged;
        VideoSummary.PropertyChanged -= OnEncodePrerequisiteChanged;
        ConversionLog.PropertyChanged -= OnEncodePrerequisiteChanged;
        OutputSettings.PropertyChanged -= OnOutputSettingsChanged;
        VideoPlayer.Dispose();
    }
}
