using CommunityToolkit.Mvvm.Input;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging.Abstractions;
using PotatoMaker.Core;
using PotatoMaker.GUI.Services;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PotatoMaker.GUI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public FileInputViewModel FileInput { get; } = new();
    public VideoSummaryViewModel VideoSummary { get; } = new();
    public OutputSettingsViewModel OutputSettings { get; } = new();
    public ConversionLogViewModel ConversionLog { get; } = new();
    public HelpModalViewModel HelpModal { get; } = new();

    public string VersionText => $"v{GetType().Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";

    [ObservableProperty]
    private bool _isDarkMode;

    public bool IsEncodeInProgress => ConversionLog.IsProcessing;
    public bool IsEncodeIdle => !ConversionLog.IsProcessing;
    public string EncodeButtonText => IsEncodeInProgress ? "Cancel Compression" : "Start Compression";
    public ICommand EncodeButtonCommand => IsEncodeInProgress ? CancelEncodeCommand : StartEncodeCommand;

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _probeCts;
    private int _probeVersion;
    private bool _isApplyingSettings;
    private readonly IEncoderCapabilityService _encoderCapabilityService;

    public MainWindowViewModel(AppSettings? initialSettings = null, IEncoderCapabilityService? encoderCapabilityService = null)
    {
        _encoderCapabilityService = encoderCapabilityService ?? new EncoderCapabilityService();
        FileInput.VideoSummary = VideoSummary;

        FileInput.FileSelected += OnFileSelected;
        FileInput.FileCleared += OnFileCleared;
        FileInput.PropertyChanged += OnEncodePrerequisiteChanged;
        VideoSummary.PropertyChanged += OnEncodePrerequisiteChanged;
        ConversionLog.PropertyChanged += OnEncodePrerequisiteChanged;
        OutputSettings.PropertyChanged += OnOutputSettingsChanged;

        ApplyInitialSettings(initialSettings);
        _ = InitializeEncoderSupportAsync();
    }

    [RelayCommand(CanExecute = nameof(CanStartEncode))]
    private async Task StartEncode()
    {
        var info = VideoSummary.Info;
        var path = FileInput.InputFilePath;
        var analysis = VideoSummary.StrategyAnalysis;
        if (info is null || path is null || analysis is null) return;

        _cts = new CancellationTokenSource();
        CancelEncodeCommand.NotifyCanExecuteChanged();

        ConversionLog.Clear();
        ConversionLog.IsProcessing = true;

        var settings = BuildEncodeSettings();
        var logger   = new ViewModelLogger(ConversionLog);
        var progress = new ViewModelProgressHandler(ConversionLog);
        var outputFolder = OutputSettings.OutputFolderPath
            ?? Path.GetDirectoryName(Path.GetFullPath(path))
            ?? ".";

        try
        {
            var pipeline = new ProcessingPipeline(path, info, settings, logger, progress, outputDirectory: outputFolder);
            await pipeline.RunAsync(analysis, _cts.Token);
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
            _cts.Dispose();
            _cts = null;
            CancelEncodeCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanStartEncode() => FileInput.HasFile && VideoSummary.HasData && VideoSummary.HasStrategy && !ConversionLog.IsProcessing;

    [RelayCommand(CanExecute = nameof(CanCancelEncode))]
    private void CancelEncode()
    {
        if (_cts is null || _cts.IsCancellationRequested)
            return;

        ConversionLog.AddLog("Cancellation requested...");
        _cts.Cancel();
        CancelEncodeCommand.NotifyCanExecuteChanged();
    }

    private bool CanCancelEncode() => ConversionLog.IsProcessing && _cts is not null && !_cts.IsCancellationRequested;

    private async void OnFileSelected(string path)
    {
        CancelPendingProbe();
        var probeCts = new CancellationTokenSource();
        _probeCts = probeCts;
        int probeVersion = Interlocked.Increment(ref _probeVersion);

        try
        {
            OutputSettings.SetSourceFolder(Path.GetDirectoryName(Path.GetFullPath(path)));

            VideoSummary.Clear();
            var info = await VideoInfo.ProbeAsync(path, probeCts.Token);

            if (probeCts.IsCancellationRequested ||
                probeVersion != _probeVersion ||
                !string.Equals(FileInput.InputFilePath, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            VideoSummary.SetProbeResult(path, info);

            VideoSummary.SetStrategyPending();
            var settings = BuildEncodeSettings();
            var analysis = await StrategyAnalyzer.AnalyzeAsync(path, info, settings, NullLogger.Instance, probeCts.Token);

            if (probeCts.IsCancellationRequested ||
                probeVersion != _probeVersion ||
                !string.Equals(FileInput.InputFilePath, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            VideoSummary.SetStrategyResult(analysis);
        }
        catch (OperationCanceledException) when (probeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (probeVersion != _probeVersion) return;
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
            if (ReferenceEquals(_probeCts, probeCts))
                _probeCts = null;

            probeCts.Dispose();
        }
    }

    private void OnFileCleared()
    {
        CancelPendingProbe();
        OutputSettings.SetSourceFolder(null);
        VideoSummary.Clear();
        ConversionLog.Clear();
    }

    private void CancelPendingProbe()
    {
        _probeCts?.Cancel();
        _probeCts?.Dispose();
        _probeCts = null;
    }

    private void OnEncodePrerequisiteChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileInputViewModel.HasFile) or nameof(VideoSummaryViewModel.HasData) or nameof(VideoSummaryViewModel.HasStrategy) or nameof(ConversionLogViewModel.IsProcessing))
        {
            StartEncodeCommand.NotifyCanExecuteChanged();
            CancelEncodeCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsEncodeInProgress));
            OnPropertyChanged(nameof(IsEncodeIdle));
            OnPropertyChanged(nameof(EncodeButtonText));
            OnPropertyChanged(nameof(EncodeButtonCommand));
        }
    }

    partial void OnIsDarkModeChanged(bool value)
    {
        if (Application.Current is null) return;
        Application.Current.RequestedThemeVariant = value ? ThemeVariant.Dark : ThemeVariant.Light;

        if (!_isApplyingSettings)
            SaveSettingsSafely();
    }

    private void OnOutputSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OutputSettingsViewModel.UseNvencEncoder) or nameof(OutputSettingsViewModel.CustomOutputFolder))
            SaveSettingsSafely();
    }

    private EncodeSettings BuildEncodeSettings() => new()
    {
        Encoder = OutputSettings.UseNvencEncoder && OutputSettings.CanUseNvenc
            ? EncoderChoice.Nvenc
            : EncoderChoice.SvtAv1
    };

    private void ApplyInitialSettings(AppSettings? settings)
    {
        var loaded = settings ?? new AppSettings
        {
            IsDarkMode = Application.Current?.ActualThemeVariant == ThemeVariant.Dark
        };

        _isApplyingSettings = true;
        try
        {
            OutputSettings.CustomOutputFolder = loaded.LastOutputFolder;
            OutputSettings.UseNvencEncoder = ResolveUseNvencPreference(loaded);
            IsDarkMode = loaded.IsDarkMode;
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private static bool ResolveUseNvencPreference(AppSettings settings)
    {
        if (settings.UseNvencEncoder.HasValue)
            return settings.UseNvencEncoder.Value;

        return !settings.UseCpuEncoder;
    }

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

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            bool wasUsingNvenc = OutputSettings.UseNvencEncoder;
            OutputSettings.SetNvencSupport(supportsNvencAv1);

            if (wasUsingNvenc && !OutputSettings.UseNvencEncoder)
                SaveSettingsSafely();
        });
    }

    private async void SaveSettingsSafely()
    {
        try
        {
            await PersistSettingsAsync();
        }
        catch
        {
            // Ignore persistence failures; the app should continue working with in-memory values.
        }
    }

    private Task PersistSettingsAsync()
    {
        var settings = new AppSettings
        {
            IsDarkMode = IsDarkMode,
            UseNvencEncoder = OutputSettings.UseNvencEncoder,
            UseCpuEncoder = !OutputSettings.UseNvencEncoder,
            LastOutputFolder = OutputSettings.CustomOutputFolder
        };

        return SettingsService.SaveAsync(settings);
    }
}
