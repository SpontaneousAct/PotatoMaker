using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PotatoMaker.Core;
using PotatoMaker.GUI.DependencyInjection;
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
    private readonly CompressionQueueViewModel _compressionQueue;
    private readonly EncodeExecutionCoordinator _executionCoordinator;
    private readonly bool _initializeEncoderSupport;
    private readonly TimeSpan _cancelledStatusDuration;
    private readonly TimeSpan _queueCelebrationPulseDuration = TimeSpan.FromMilliseconds(180);
    private readonly TimeSpan _queueCelebrationVisibleDuration = TimeSpan.FromMilliseconds(650);
    private CancellationTokenSource? _encodeCts;
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _queueCelebrationCts;
    private CancellationTokenSource? _statusResetCts;
    private Stopwatch? _encodeStopwatch;
    private int _previewVersion;
    private bool _isApplyingSettings;
    private bool _isCropDetectionPending;
    private string? _detectedCropFilter;

    public EncodeWorkspaceViewModel()
        : this(CreateDefaultWorkspaceGraph())
    {
    }

    private EncodeWorkspaceViewModel(DefaultGuiComposition.WorkspaceGraph graph)
        : this(
            graph.AnalysisService,
            graph.EncodingService,
            graph.VideoPlayer,
            graph.EncoderCapabilityService,
            null,
            graph.CompressionQueue,
            graph.ExecutionCoordinator,
            initializeEncoderSupport: true,
            graph.EncodeCompletionNotifier,
            null,
            graph.ProcessedVideoTracker)
    {
    }

    public EncodeWorkspaceViewModel(
        IVideoAnalysisService analysisService,
        IVideoEncodingService encodingService,
        IEncoderCapabilityService encoderCapabilityService,
        IAppSettingsCoordinator? settingsCoordinator,
        bool initializeEncoderSupport,
        IEncodeCompletionNotifier? encodeCompletionNotifier,
        TimeSpan? cancelledStatusDuration = null,
        IProcessedVideoTracker? processedVideoTracker = null)
        : this(
            analysisService,
            encodingService,
            new VideoPlayerViewModel(initializePlayer: false),
            encoderCapabilityService,
            settingsCoordinator,
            null,
            null,
            initializeEncoderSupport,
            encodeCompletionNotifier,
            cancelledStatusDuration,
            processedVideoTracker)
    {
    }

    public EncodeWorkspaceViewModel(
        IVideoAnalysisService analysisService,
        IVideoEncodingService encodingService,
        IEncoderCapabilityService encoderCapabilityService,
        IAppSettingsCoordinator? settingsCoordinator,
        CompressionQueueViewModel? compressionQueue = null,
        EncodeExecutionCoordinator? executionCoordinator = null,
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
            compressionQueue,
            executionCoordinator,
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
        bool initializeEncoderSupport,
        IEncodeCompletionNotifier? encodeCompletionNotifier,
        TimeSpan? cancelledStatusDuration = null,
        IProcessedVideoTracker? processedVideoTracker = null)
        : this(
            analysisService,
            encodingService,
            videoPlayer,
            encoderCapabilityService,
            settingsCoordinator,
            null,
            null,
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
        CompressionQueueViewModel? compressionQueue = null,
        EncodeExecutionCoordinator? executionCoordinator = null,
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
        _compressionQueue = compressionQueue ?? new CompressionQueueViewModel();
        _executionCoordinator = executionCoordinator ?? new EncodeExecutionCoordinator();
        _initializeEncoderSupport = initializeEncoderSupport;
        _cancelledStatusDuration = cancelledStatusDuration ?? TimeSpan.FromSeconds(5);
        OutputSettings = new OutputSettingsViewModel();
        VideoSummary = new VideoSummaryViewModel(OutputSettings);

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
        VideoSummary.PropertyChanged += OnVideoSummaryChanged;
        ConversionLog.PropertyChanged += OnEncodePrerequisiteChanged;
        OutputSettings.PropertyChanged += OnOutputSettingsChanged;
        _compressionQueue.PropertyChanged += OnCompressionQueueChanged;
        _executionCoordinator.PropertyChanged += OnExecutionCoordinatorChanged;

        ApplyInitialSettings();
        UpdateSourceSelectionState();
        UpdateConversionIdlePrompt();
        if (_initializeEncoderSupport)
            _ = InitializeEncoderSupportAsync();
    }

    public FileInputViewModel FileInput { get; } = new();

    public VideoSummaryViewModel VideoSummary { get; }

    public VideoPlayerViewModel VideoPlayer { get; }

    public ClipRangeViewModel ClipRange { get; } = new();

    public OutputSettingsViewModel OutputSettings { get; }

    public ConversionLogViewModel ConversionLog { get; } = new();

    public CompressionQueueViewModel CompressionQueue => _compressionQueue;

    public bool IsEncodeInProgress => ConversionLog.IsProcessing;

    public bool IsEncodeIdle => !ConversionLog.IsProcessing;

    public string EncodeButtonText => IsEncodeInProgress ? "Cancel" : "Compress";

    public ICommand EncodeButtonCommand => IsEncodeInProgress ? CancelEncodeCommand : StartEncodeCommand;

    [ObservableProperty]
    private bool _isQueueButtonCelebrating;

    [ObservableProperty]
    private double _queueButtonScale = 1d;

    public bool IsCurrentSelectionQueued =>
        TryBuildQueueDraft(out QueuedCompressionItemDraft? draft) &&
        draft is not null &&
        _compressionQueue.ContainsDraft(draft);

    public double QueueButtonAddOpacity => IsCurrentSelectionQueued ? 0d : 1d;

    public double QueueButtonAddOffsetY => IsCurrentSelectionQueued ? -5d : 0d;

    public double QueueButtonAddedOpacity => IsCurrentSelectionQueued ? 1d : 0d;

    public double QueueButtonAddedOffsetY => IsCurrentSelectionQueued ? 0d : 5d;

    public string? AddToQueueToolTip =>
        _compressionQueue.ActiveItemCount >= _compressionQueue.MaxQueueSize
            ? $"Queue is full. Remove an item before adding more than {_compressionQueue.MaxQueueSize} videos."
            : null;

    [RelayCommand(CanExecute = nameof(CanStartEncode))]
    private async Task StartEncode()
    {
        using IDisposable operation = CrashReportService.Shared.BeginOperation("Compressing video");
        IDisposable? executionLease = _executionCoordinator.TryAcquire();
        if (executionLease is null)
        {
            UpdateConversionIdlePrompt();
            NotifyEncodeStateChanged();
            return;
        }

        VideoInfo? info = VideoSummary.Info;
        string? path = FileInput.InputFilePath;
        StrategyAnalysis? strategy = VideoSummary.StrategyAnalysis;
        if (info is null || path is null || strategy is null)
        {
            executionLease.Dispose();
            return;
        }

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
            executionLease.Dispose();
            NotifyEncodeStateChanged();
        }
    }

    private bool CanStartEncode() =>
        FileInput.HasFile &&
        VideoSummary.HasData &&
        VideoSummary.HasStrategy &&
        !ConversionLog.IsProcessing &&
        !_executionCoordinator.IsBusy;

    [RelayCommand(CanExecute = nameof(CanAddToQueue), AllowConcurrentExecutions = false)]
    private async Task AddToQueueAsync()
    {
        if (!TryBuildQueueDraft(out QueuedCompressionItemDraft? draft))
            return;

        QueueEnqueueResult result = await _compressionQueue.AddAsync(draft!);
        ConversionLog.SetIdleText(result.Message);
        if (result.Succeeded)
            TriggerQueueButtonCelebration();
        NotifyEncodeStateChanged();
    }

    private bool CanAddToQueue() =>
        TryBuildQueueDraft(out QueuedCompressionItemDraft? draft) &&
        draft is not null &&
        _compressionQueue.ActiveItemCount < _compressionQueue.MaxQueueSize &&
        !_compressionQueue.ContainsDraft(draft);

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
        using IDisposable operation = CrashReportService.Shared.BeginOperation("Loading and analyzing video");
        CancelPendingPreview();
        CancelPendingStatusReset();
        var previewCts = new CancellationTokenSource();
        _previewCts = previewCts;
        int previewVersion = Interlocked.Increment(ref _previewVersion);
        ConversionLog.BeginAnalysis();

        try
        {
            OutputSettings.SetSourceFolder(Path.GetDirectoryName(Path.GetFullPath(path)));
            ResetFrameRateModeToSourceDefault();

            _detectedCropFilter = null;
            _isCropDetectionPending = false;
            VideoSummary.Clear();
            VideoPlayer.Clear();
            ClipRange.Clear();
            _isCropDetectionPending = true;
            VideoInfo info = await _analysisService.ProbeAsync(path, previewCts.Token);
            if (ShouldIgnorePreviewResult(previewCts, previewVersion, path))
                return;

            VideoSummary.SetProbeResult(path, info);
            VideoSummary.SetCropDetectionPending();
            ClipRange.SetSourceDuration(info.Duration);
            VideoPlayer.LoadSource(path, info.Duration, ClipRange.Selection);
            VideoSummary.SetSelectedRange(ClipRange.Selection, info.Duration);
            ResetPreviewCommand.NotifyCanExecuteChanged();

            VideoSummary.SetStrategyPending();
            _detectedCropFilter = await _analysisService.DetectCropAsync(path, info, previewCts.Token);
            if (ShouldIgnorePreviewResult(previewCts, previewVersion, path))
                return;

            _isCropDetectionPending = false;
            VideoSummary.SetCropDetectionResult(_detectedCropFilter);
            await RefreshStrategyPreviewAsync(path, info, previewCts, previewVersion, showPendingState: false);
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

    private void OnVideoSummaryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VideoSummaryViewModel.SelectedCropOption) ||
            FileInput.InputFilePath is not { } path ||
            VideoSummary.Info is not { } info ||
            _isCropDetectionPending)
        {
            return;
        }

        _ = RefreshStrategyPreviewForCurrentStateAsync(path, info, showPendingState: false);
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
            GetEffectiveCropFilter(info),
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

    private string? GetEffectiveCropFilter(VideoInfo info)
    {
        CropModeOption cropMode = VideoSummary.SelectedCropOption ?? new CropModeOption("auto", "Auto");
        return cropMode.IsAuto
            ? _detectedCropFilter
            : EncodePlanner.BuildCenteredCropFilterForAspectRatio(
                info.Width,
                info.Height,
                cropMode.AspectRatioWidth!.Value,
                cropMode.AspectRatioHeight!.Value);
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

    private void ResetFrameRateModeToSourceDefault()
    {
        bool wasApplyingSettings = _isApplyingSettings;
        _isApplyingSettings = true;
        try
        {
            OutputSettings.SetFrameRateMode(EncodeFrameRateMode.Original);
        }
        finally
        {
            _isApplyingSettings = wasApplyingSettings;
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
        AddToQueueCommand.NotifyCanExecuteChanged();
        NotifyQueueButtonStateChanged();
        OnPropertyChanged(nameof(IsEncodeInProgress));
        OnPropertyChanged(nameof(IsEncodeIdle));
        OnPropertyChanged(nameof(EncodeButtonText));
        OnPropertyChanged(nameof(EncodeButtonCommand));
    }

    private void TriggerQueueButtonCelebration()
    {
        _queueCelebrationCts?.Cancel();
        _queueCelebrationCts?.Dispose();

        var celebrationCts = new CancellationTokenSource();
        _queueCelebrationCts = celebrationCts;
        _ = RunQueueButtonCelebrationAsync(celebrationCts);
    }

    private async Task RunQueueButtonCelebrationAsync(CancellationTokenSource celebrationCts)
    {
        try
        {
            IsQueueButtonCelebrating = true;
            QueueButtonScale = 0.98d;

            await Task.Yield();
            if (celebrationCts.IsCancellationRequested || !ReferenceEquals(_queueCelebrationCts, celebrationCts))
                return;

            QueueButtonScale = 1.04d;

            await Task.Delay(_queueCelebrationPulseDuration, celebrationCts.Token);
            QueueButtonScale = 1d;

            await Task.Delay(_queueCelebrationVisibleDuration, celebrationCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_queueCelebrationCts, celebrationCts))
            {
                QueueButtonScale = 1d;
                IsQueueButtonCelebrating = false;
                _queueCelebrationCts = null;
            }

            celebrationCts.Dispose();
        }
    }

    private void NotifyQueueButtonStateChanged()
    {
        OnPropertyChanged(nameof(IsCurrentSelectionQueued));
        OnPropertyChanged(nameof(QueueButtonAddOpacity));
        OnPropertyChanged(nameof(QueueButtonAddOffsetY));
        OnPropertyChanged(nameof(QueueButtonAddedOpacity));
        OnPropertyChanged(nameof(QueueButtonAddedOffsetY));
        OnPropertyChanged(nameof(AddToQueueToolTip));
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

        if (_executionCoordinator.IsBusy && !ConversionLog.IsProcessing)
        {
            ConversionLog.SetIdleText("Another compression is running");
            return;
        }

        ConversionLog.SetIdleText(CanStartEncode() ? "Ready" : "Getting ready");
    }

    private bool TryBuildQueueDraft(out QueuedCompressionItemDraft? draft)
    {
        draft = null;

        VideoInfo? info = VideoSummary.Info;
        StrategyAnalysis? strategy = VideoSummary.StrategyAnalysis;
        string? inputPath = FileInput.InputFilePath;
        if (info is null || strategy is null || inputPath is null)
            return false;

        string outputDirectory = OutputSettings.OutputFolderPath
            ?? Path.GetDirectoryName(Path.GetFullPath(inputPath))
            ?? ".";
        VideoClipRange clipRange = ClipRange.Selection.Normalize(info.Duration);
        long selectedSizeBytes = EstimateSelectedSizeBytes(inputPath, info, clipRange);

        draft = new QueuedCompressionItemDraft(
            inputPath,
            outputDirectory,
            info,
            strategy,
            BuildEncodeSettings(),
            clipRange,
            selectedSizeBytes);

        return true;
    }

    private static long EstimateSelectedSizeBytes(string inputPath, VideoInfo info, VideoClipRange clipRange)
    {
        long fileSizeBytes = new FileInfo(Path.GetFullPath(inputPath)).Length;
        if (info.Duration <= TimeSpan.Zero)
            return fileSizeBytes;

        double ratio = clipRange.Duration.TotalMilliseconds / info.Duration.TotalMilliseconds;
        ratio = Math.Clamp(ratio, 0d, 1d);
        return (long)Math.Round(fileSizeBytes * ratio, MidpointRounding.AwayFromZero);
    }

    private void OnCompressionQueueChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CompressionQueueViewModel.QueueCount)
            or nameof(CompressionQueueViewModel.ActiveItemCount)
            or nameof(CompressionQueueViewModel.CanCompressAll))
        {
            AddToQueueCommand.NotifyCanExecuteChanged();
            NotifyQueueButtonStateChanged();
        }
    }

    private void OnExecutionCoordinatorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EncodeExecutionCoordinator.IsBusy))
            NotifyEncodeStateChanged();
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
        using IDisposable operation = CrashReportService.Shared.BeginOperation("Refreshing encode strategy");
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
                GetEffectiveCropFilter(info),
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
        _queueCelebrationCts?.Cancel();
        _queueCelebrationCts?.Dispose();
        _queueCelebrationCts = null;
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
        VideoSummary.PropertyChanged -= OnVideoSummaryChanged;
        ConversionLog.PropertyChanged -= OnEncodePrerequisiteChanged;
        OutputSettings.PropertyChanged -= OnOutputSettingsChanged;
        _compressionQueue.PropertyChanged -= OnCompressionQueueChanged;
        _executionCoordinator.PropertyChanged -= OnExecutionCoordinatorChanged;
        VideoPlayer.Dispose();
    }

    private static DefaultGuiComposition.WorkspaceGraph CreateDefaultWorkspaceGraph() =>
        DefaultGuiComposition.CreateWorkspaceGraph();
}
