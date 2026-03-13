using CommunityToolkit.Mvvm.ComponentModel;
using PotatoMaker.Core;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Shows source media details and the planned strategy.
/// </summary>
public partial class VideoSummaryViewModel : ViewModelBase
{
    [ObservableProperty] private string? _fileSize;
    [ObservableProperty] private string? _duration;
    [ObservableProperty] private string? _resolution;
    [ObservableProperty] private string? _frameRate;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string? _selectedRange;
    [ObservableProperty] private string? _selectedDuration;

    [ObservableProperty] private string? _strategyStatus;
    [ObservableProperty] private string? _strategyResolution;
    [ObservableProperty] private string? _strategyBitrate;
    [ObservableProperty] private string? _strategyParts;
    [ObservableProperty] private string? _strategyOutputFrameRate;
    [ObservableProperty] private string? _strategyCrop;
    [ObservableProperty] private string? _strategyFilter;
    [ObservableProperty] private bool _hasStrategy;

    /// <summary>
    /// Gets the last successful probe result.
    /// </summary>
    public VideoInfo? Info { get; private set; }

    /// <summary>
    /// Gets the last successful strategy result.
    /// </summary>
    public StrategyAnalysis? StrategyAnalysis { get; private set; }

    public void SetProbeResult(string path, VideoInfo info)
    {
        FileSize = File.Exists(path)
            ? FormatFileSize(new FileInfo(path).Length)
            : "N/A";

        Info = info;
        Duration = info.Duration.TotalHours >= 1
            ? info.Duration.ToString(@"h\:mm\:ss")
            : info.Duration.ToString(@"m\:ss");
        Resolution = info.Width > 0 ? $"{info.Width}x{info.Height}" : "N/A";
        FrameRate = info.FrameRate > 0 ? $"{info.FrameRate:0.##} fps" : "N/A";
        HasData = true;
    }

    public void SetSelectedRange(VideoClipRange clipRange, TimeSpan totalDuration)
    {
        VideoClipRange normalized = clipRange.Normalize(totalDuration);
        SelectedRange = $"{FormatTime(normalized.Start)} - {FormatTime(normalized.End)}";
        SelectedDuration = FormatTime(normalized.Duration);
    }

    public void SetStrategyPending()
    {
        StrategyAnalysis = null;
        HasStrategy = false;
        StrategyStatus = "Analyzing crop and strategy...";
        StrategyResolution = null;
        StrategyBitrate = null;
        StrategyParts = null;
        StrategyCrop = null;
        StrategyFilter = null;
    }

    public void SetStrategyResult(StrategyAnalysis analysis)
    {
        StrategyAnalysis = analysis;
        StrategyStatus = null;

        var plan = analysis.Plan;
        StrategyResolution = plan.ResolutionLabel;
        StrategyBitrate = plan.Parts > 1
            ? $"{plan.VideoBitrateKbps} kbps (per part)"
            : $"{plan.VideoBitrateKbps} kbps";
        StrategyParts = plan.Parts == 1 ? "Single file" : $"{plan.Parts} parts";
        StrategyOutputFrameRate = analysis.OutputFrameRate > 0 ? $"{analysis.OutputFrameRate:0.##} fps" : "Original";
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
        StrategyOutputFrameRate = null;
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
        SelectedRange = null;
        SelectedDuration = null;
        ClearStrategy();
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B"
    };

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss\.f")
            : value.ToString(@"m\:ss\.f");
}
