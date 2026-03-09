using CommunityToolkit.Mvvm.Input;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging.Abstractions;
using PotatoMaker.Core;
using PotatoMaker.GUI.Services;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PotatoMaker.GUI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public FileInputViewModel FileInput { get; } = new();
    public VideoSummaryViewModel VideoSummary { get; } = new();
    public OutputSettingsViewModel OutputSettings { get; } = new();
    public ConversionLogViewModel ConversionLog { get; } = new();

    public string VersionText => $"v{GetType().Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";

    [ObservableProperty]
    private bool _isDarkMode;

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _probeCts;
    private int _probeVersion;
    private bool _isApplyingSettings;

    public MainWindowViewModel(AppSettings? initialSettings = null)
    {
        FileInput.FileSelected += OnFileSelected;
        FileInput.FileCleared += OnFileCleared;
        FileInput.PropertyChanged += OnEncodePrerequisiteChanged;
        VideoSummary.PropertyChanged += OnEncodePrerequisiteChanged;
        ConversionLog.PropertyChanged += OnEncodePrerequisiteChanged;
        OutputSettings.PropertyChanged += OnOutputSettingsChanged;

        ApplyInitialSettings(initialSettings);
    }

    [RelayCommand(CanExecute = nameof(CanStartEncode))]
    private async Task StartEncode()
    {
        var info = VideoSummary.Info;
        var path = FileInput.InputFilePath;
        var analysis = VideoSummary.StrategyAnalysis;
        if (info is null || path is null || analysis is null) return;

        _cts = new CancellationTokenSource();

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
        }
    }

    private bool CanStartEncode() => FileInput.HasFile && VideoSummary.HasData && VideoSummary.HasStrategy && !ConversionLog.IsProcessing;

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
            StartEncodeCommand.NotifyCanExecuteChanged();
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
        if (e.PropertyName is nameof(OutputSettingsViewModel.UseCpuEncoder) or nameof(OutputSettingsViewModel.CustomOutputFolder))
            SaveSettingsSafely();
    }

    private EncodeSettings BuildEncodeSettings() => new()
    {
        Encoder = OutputSettings.UseCpuEncoder
            ? EncoderChoice.SvtAv1
            : EncoderChoice.Nvenc
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
            OutputSettings.UseCpuEncoder = loaded.UseCpuEncoder;
            IsDarkMode = loaded.IsDarkMode;
        }
        finally
        {
            _isApplyingSettings = false;
        }
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
            UseCpuEncoder = OutputSettings.UseCpuEncoder,
            LastOutputFolder = OutputSettings.CustomOutputFolder
        };

        return SettingsService.SaveAsync(settings);
    }
}
