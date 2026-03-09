using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PotatoMaker.Core;

namespace PotatoMaker.GUI.ViewModels;

public partial class VideoSummaryViewModel : ViewModelBase
{
    [ObservableProperty] private string? _fileSize;
    [ObservableProperty] private string? _duration;
    [ObservableProperty] private string? _resolution;
    [ObservableProperty] private string? _frameRate;
    [ObservableProperty] private bool _hasData;

    [ObservableProperty] private string? _strategyStatus;
    [ObservableProperty] private string? _strategyResolution;
    [ObservableProperty] private string? _strategyBitrate;
    [ObservableProperty] private string? _strategyParts;
    [ObservableProperty] private string? _strategyCrop;
    [ObservableProperty] private string? _strategyFilter;
    [ObservableProperty] private bool _hasStrategy;

    /// <summary>
    /// The last successful probe result. Available after <see cref="ProbeAsync"/> completes.
    /// </summary>
    public VideoInfo? Info { get; private set; }

    public StrategyAnalysis? StrategyAnalysis { get; private set; }

    public async Task ProbeAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var info = await VideoInfo.ProbeAsync(path, ct);
            SetProbeResult(path, info);
        }
        catch (Exception)
        {
            Clear();
            throw; // let the caller handle logging
        }
    }

    public void SetProbeResult(string path, VideoInfo info)
    {
        FileSize = File.Exists(path)
            ? FormatFileSize(new FileInfo(path).Length)
            : "N/A";

        Info = info;

        Duration = info.Duration.TotalHours >= 1
            ? info.Duration.ToString(@"h\:mm\:ss")
            : info.Duration.ToString(@"m\:ss");

        Resolution = info.Width > 0
            ? $"{info.Width}x{info.Height}"
            : "N/A";

        FrameRate = info.FrameRate > 0
            ? $"{info.FrameRate:0.##} fps"
            : "N/A";

        HasData = true;
    }

    public void SetStrategyPending()
    {
        StrategyAnalysis = null;
        HasStrategy = false;
        StrategyStatus = "Analyzing crop + strategy...";
        StrategyResolution = null;
        StrategyBitrate = null;
        StrategyParts = null;
        StrategyCrop = null;
        StrategyFilter = null;
    }

    public void SetStrategyResult(StrategyAnalysis analysis)
    {
        StrategyAnalysis = analysis;
        StrategyStatus = "Strategy ready";

        var plan = analysis.Plan;
        StrategyResolution = plan.ResolutionLabel;
        StrategyBitrate = plan.Parts > 1
            ? $"{plan.VideoBitrateKbps} kbps (per part)"
            : $"{plan.VideoBitrateKbps} kbps";
        StrategyParts = plan.Parts == 1 ? "Single file" : $"{plan.Parts} parts";
        StrategyCrop = string.IsNullOrWhiteSpace(analysis.CropFilter) ? "No crop detected" : analysis.CropFilter;
        StrategyFilter = analysis.VideoFilter ?? "None";
        HasStrategy = true;
    }

    public void ClearStrategy()
    {
        StrategyAnalysis = null;
        HasStrategy = false;
        StrategyStatus = null;
        StrategyResolution = null;
        StrategyBitrate = null;
        StrategyParts = null;
        StrategyCrop = null;
        StrategyFilter = null;
    }

    public void Clear()
    {
        Info = null;
        FileSize = null;
        Duration = null;
        Resolution = null;
        FrameRate = null;
        HasData = false;
        ClearStrategy();
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B"
    };
}
