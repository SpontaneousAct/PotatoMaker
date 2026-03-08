using CommunityToolkit.Mvvm.Input;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
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
    private bool _isApplyingSettings;

    public string? LastOutputFolder { get; private set; }

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
        if (info is null || path is null) return;

        _cts = new CancellationTokenSource();

        ConversionLog.Clear();
        ConversionLog.IsProcessing = true;

        var settings = new EncodeSettings
        {
            Encoder = OutputSettings.UseCpuEncoder
                ? EncoderChoice.SvtAv1
                : EncoderChoice.Nvenc
        };

        var logger   = new ViewModelLogger(ConversionLog);
        var progress = new ViewModelProgressHandler(ConversionLog);

        try
        {
            var pipeline = new ProcessingPipeline(path, info, settings, logger, progress);
            await pipeline.RunAsync(_cts.Token);
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

    private bool CanStartEncode() => FileInput.HasFile && VideoSummary.HasData && !ConversionLog.IsProcessing;

    private async void OnFileSelected(string path)
    {
        try
        {
            LastOutputFolder = Path.GetDirectoryName(Path.GetFullPath(path));
            SaveSettingsSafely();

            VideoSummary.Clear();
            await VideoSummary.ProbeAsync(path);
        }
        catch (Exception ex)
        {
            VideoSummary.Clear();
            ConversionLog.AddLog($"Error probing file: {ex.Message}");
        }
    }

    private void OnFileCleared()
    {
        VideoSummary.Clear();
    }

    private void OnEncodePrerequisiteChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileInputViewModel.HasFile) or nameof(VideoSummaryViewModel.HasData) or nameof(ConversionLogViewModel.IsProcessing))
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
        if (e.PropertyName == nameof(OutputSettingsViewModel.UseCpuEncoder))
            SaveSettingsSafely();
    }

    private void ApplyInitialSettings(AppSettings? settings)
    {
        var loaded = settings ?? new AppSettings
        {
            IsDarkMode = Application.Current?.ActualThemeVariant == ThemeVariant.Dark
        };

        _isApplyingSettings = true;
        try
        {
            LastOutputFolder = loaded.LastOutputFolder;
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
            LastOutputFolder = LastOutputFolder
        };

        return SettingsService.SaveAsync(settings);
    }
}
