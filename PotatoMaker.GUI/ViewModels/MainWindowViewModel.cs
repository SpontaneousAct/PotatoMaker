using CommunityToolkit.Mvvm.Input;
using PotatoMaker.Core;
using PotatoMaker.GUI.Services;
using System;
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

    private CancellationTokenSource? _cts;

    public MainWindowViewModel()
    {
        FileInput.FileSelected += OnFileSelected;
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
        StartEncodeCommand.NotifyCanExecuteChanged();

        var encoder = OutputSettings.UseCpuEncoder
            ? EncoderChoice.SvtAv1
            : EncoderChoice.Nvenc;

        var logger   = new ViewModelLogger(ConversionLog);
        var progress = new ViewModelProgressHandler(ConversionLog);

        try
        {
            var pipeline = new ProcessingPipeline(path, info, encoder, logger, progress);
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
            StartEncodeCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanStartEncode() => FileInput.HasFile && !ConversionLog.IsProcessing;

    private async void OnFileSelected(string path)
    {
        try
        {
            await VideoSummary.ProbeAsync(path);
            StartEncodeCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            VideoSummary.Clear();
            ConversionLog.AddLog($"Error probing file: {ex.Message}");
        }
    }
}
