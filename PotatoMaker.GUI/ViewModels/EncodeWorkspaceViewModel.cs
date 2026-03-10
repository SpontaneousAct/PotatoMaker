using System.ComponentModel;
using System.Windows.Input;
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
    private readonly IEncoderCapabilityService _encoderCapabilityService;
    private readonly IAppSettingsCoordinator? _settingsCoordinator;
    private readonly bool _initializeEncoderSupport;
    private CancellationTokenSource? _encodeCts;
    private CancellationTokenSource? _previewCts;
    private int _previewVersion;
    private bool _isApplyingSettings;

    public EncodeWorkspaceViewModel()
        : this(
            new VideoAnalysisService(),
            new VideoEncodingService(),
            new EncoderCapabilityService(),
            null,
            initializeEncoderSupport: true)
    {
    }

    public EncodeWorkspaceViewModel(
        IVideoAnalysisService analysisService,
        IVideoEncodingService encodingService,
        IEncoderCapabilityService encoderCapabilityService,
        IAppSettingsCoordinator? settingsCoordinator,
        bool initializeEncoderSupport = true)
    {
        _analysisService = analysisService;
        _encodingService = encodingService;
        _encoderCapabilityService = encoderCapabilityService;
        _settingsCoordinator = settingsCoordinator;
        _initializeEncoderSupport = initializeEncoderSupport;

        FileInput.FileSelected += OnFileSelected;
        FileInput.FileCleared += OnFileCleared;
        FileInput.PropertyChanged += OnEncodePrerequisiteChanged;
        VideoSummary.PropertyChanged += OnEncodePrerequisiteChanged;
        ConversionLog.PropertyChanged += OnEncodePrerequisiteChanged;
        OutputSettings.PropertyChanged += OnOutputSettingsChanged;

        ApplyInitialSettings();
        if (_initializeEncoderSupport)
            _ = InitializeEncoderSupportAsync();
    }

    public FileInputViewModel FileInput { get; } = new();

    public VideoSummaryViewModel VideoSummary { get; } = new();

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
                BuildEncodeSettings());

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
            VideoInfo info = await _analysisService.ProbeAsync(path, previewCts.Token);
            if (ShouldIgnorePreviewResult(previewCts, previewVersion, path))
                return;

            VideoSummary.SetProbeResult(path, info);
            VideoSummary.SetStrategyPending();

            StrategyAnalysis strategy = await _analysisService.AnalyzeStrategyAsync(
                path,
                info,
                BuildEncodeSettings(),
                previewCts.Token);

            if (ShouldIgnorePreviewResult(previewCts, previewVersion, path))
                return;

            VideoSummary.SetStrategyResult(strategy);
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
        OutputSettings.SetSourceFolder(null);
        VideoSummary.Clear();
        ConversionLog.Clear();
        NotifyEncodeStateChanged();
    }

    private void CancelPendingPreview()
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;
    }

    private bool ShouldIgnorePreviewResult(CancellationTokenSource previewCts, int previewVersion, string path) =>
        previewCts.IsCancellationRequested ||
        previewVersion != _previewVersion ||
        !string.Equals(FileInput.InputFilePath, path, StringComparison.OrdinalIgnoreCase);

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
