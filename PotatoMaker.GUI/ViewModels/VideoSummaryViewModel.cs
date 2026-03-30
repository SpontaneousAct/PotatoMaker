using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PotatoMaker.Core;

namespace PotatoMaker.GUI.ViewModels;

public sealed class CropModeOption
{
    public CropModeOption(
        string id,
        string label,
        int? aspectRatioWidth = null,
        int? aspectRatioHeight = null)
    {
        Id = id;
        Label = label;
        AspectRatioWidth = aspectRatioWidth;
        AspectRatioHeight = aspectRatioHeight;
    }

    public string Id { get; }

    public string Label { get; }

    public int? AspectRatioWidth { get; }

    public int? AspectRatioHeight { get; }

    public bool IsAuto => AspectRatioWidth is null || AspectRatioHeight is null;

    public override string ToString() => Label;
}

/// <summary>
/// Shows source media details and the planned strategy.
/// </summary>
public partial class VideoSummaryViewModel : ViewModelBase
{
    private const double NoOpCropAreaThreshold = 0.97d;
    private const double ExactFrameRateTolerance = 0.01d;
    private static readonly CropModeOption[] AvailableCropOptions =
    [
        new("auto", "Auto"),
        new("32:9", "32:9", 32, 9),
        new("21:9", "21:9", 21, 9),
        new("16:9", "16:9", 16, 9),
        new("9:16", "9:16", 9, 16)
    ];

    public VideoSummaryViewModel()
        : this(new OutputSettingsViewModel())
    {
    }

    public VideoSummaryViewModel(OutputSettingsViewModel outputSettings)
    {
        OutputSettings = outputSettings ?? throw new ArgumentNullException(nameof(outputSettings));
        OutputSettings.PropertyChanged += OnOutputSettingsChanged;
    }

    [ObservableProperty] private string? _fileSize;
    [ObservableProperty] private string? _duration;
    [ObservableProperty] private string? _resolution;
    [ObservableProperty] private string? _frameRate;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string? _selectedRange;
    [ObservableProperty] private string? _selectedStart;
    [ObservableProperty] private string? _selectedEnd;
    [ObservableProperty] private string? _selectedDuration;

    [ObservableProperty] private string? _strategyStatus;
    [ObservableProperty] private string? _strategyResolution;
    [ObservableProperty] private string? _strategyBitrate;
    [ObservableProperty] private string? _strategyParts;
    [ObservableProperty] private string? _strategyOutputFrameRate;
    [ObservableProperty] private string? _strategyCrop;
    [ObservableProperty] private string? _strategyFilter;
    [ObservableProperty] private bool _hasStrategy;
    [ObservableProperty] private CropModeOption? _selectedCropOption;
    [ObservableProperty] private bool _canSelectCropMode;
    [ObservableProperty] private bool _canSelectFrameRate;

    public ObservableCollection<CropModeOption> CropOptions { get; } = [];
    public ObservableCollection<FrameRateOption> FrameRateOptions { get; } = [];

    public OutputSettingsViewModel OutputSettings { get; }

    public FrameRateOption? SelectedFrameRateOption
    {
        get => ResolveFrameRateSelection(OutputSettings.SelectedFrameRateOption);
        set
        {
            if (value is null)
                return;

            FrameRateOption? nextOption = OutputSettings.FrameRateOptions
                .FirstOrDefault(option => option.Value == value.Value);
            if (nextOption is null || ReferenceEquals(nextOption, OutputSettings.SelectedFrameRateOption))
                return;

            OutputSettings.SelectedFrameRateOption = nextOption;
        }
    }

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
        UpdateCropOptions(info);
        UpdateFrameRateOptions(info);
        HasData = true;
    }

    public void SetSelectedRange(VideoClipRange clipRange, TimeSpan totalDuration)
    {
        VideoClipRange normalized = clipRange.Normalize(totalDuration);
        SelectedStart = FormatTime(normalized.Start);
        SelectedEnd = FormatTime(normalized.End);
        SelectedRange = $"{SelectedStart} - {SelectedEnd}";
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
        StrategyOutputFrameRate = null;
        StrategyCrop = null;
        StrategyFilter = null;
    }

    public void SetStrategyResult(StrategyAnalysis analysis)
    {
        StrategyAnalysis = analysis;
        StrategyStatus = null;

        var plan = analysis.Plan;
        StrategyResolution = FormatResolutionSummary(plan.ResolutionLabel);
        StrategyBitrate = $"{plan.VideoBitrateKbps} kbps";
        StrategyParts = plan.Parts == 1 ? "Single file" : $"{plan.Parts} parts";
        StrategyOutputFrameRate = analysis.OutputFrameRate > 0 ? $"{analysis.OutputFrameRate:0.##} fps" : "Original";
        StrategyCrop = FormatCropSummary(analysis.CropFilter, SelectedCropOption ?? AvailableCropOptions[0]);
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
        SelectedStart = null;
        SelectedEnd = null;
        SelectedDuration = null;
        SelectedCropOption = null;
        ClearCropOptions();
        ClearFrameRateOptions();
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

    private void UpdateCropOptions(VideoInfo info)
    {
        CropModeOption? previousSelection = SelectedCropOption;
        ReplaceCropOptions(AvailableCropOptions.Where(option => option.IsAuto || !ShouldHideCropOption(info, option)));
        SelectedCropOption = ResolveSelection(previousSelection);
    }

    private void ClearCropOptions()
    {
        CropOptions.Clear();
        CanSelectCropMode = false;
    }

    private void ReplaceCropOptions(IEnumerable<CropModeOption> options)
    {
        CropOptions.Clear();
        foreach (CropModeOption option in options)
            CropOptions.Add(option);

        CanSelectCropMode = CropOptions.Count > 0;
    }

    private void UpdateFrameRateOptions(VideoInfo info)
    {
        ReplaceFrameRateOptions(BuildVisibleFrameRateOptions(info.FrameRate));
        OnPropertyChanged(nameof(SelectedFrameRateOption));
    }

    private void ClearFrameRateOptions()
    {
        FrameRateOptions.Clear();
        CanSelectFrameRate = false;
        OnPropertyChanged(nameof(SelectedFrameRateOption));
    }

    private void ReplaceFrameRateOptions(IEnumerable<FrameRateOption> options)
    {
        FrameRateOptions.Clear();
        foreach (FrameRateOption option in options)
            FrameRateOptions.Add(option);

        CanSelectFrameRate = FrameRateOptions.Count > 0;
    }

    private CropModeOption? ResolveSelection(CropModeOption? previousSelection) =>
        CropOptions.Count == 0
            ? null
            : previousSelection is not null
                ? CropOptions.FirstOrDefault(option => option.Id == previousSelection.Id) ?? CropOptions[0]
                : CropOptions[0];

    private FrameRateOption? ResolveFrameRateSelection(FrameRateOption? selectedOption)
    {
        if (FrameRateOptions.Count == 0)
            return null;

        if (selectedOption is not null)
        {
            FrameRateOption? visibleMatch = FrameRateOptions.FirstOrDefault(option => option.Value == selectedOption.Value);
            if (visibleMatch is not null)
                return visibleMatch;

            if (Info is not null && IsNoOpFrameRateChoice(Info.FrameRate, selectedOption))
                return FrameRateOptions.FirstOrDefault(option => option.Value == EncodeFrameRateMode.Original);
        }

        return FrameRateOptions.FirstOrDefault(option => option.Value == EncodeSettings.DefaultFrameRateMode)
            ?? FrameRateOptions.FirstOrDefault();
    }

    private static bool ShouldHideCropOption(VideoInfo info, CropModeOption option)
    {
        if (option.IsAuto || info.Width <= 0 || info.Height <= 0)
            return false;

        string? cropFilter = EncodePlanner.BuildCenteredCropFilterForAspectRatio(
            info.Width,
            info.Height,
            option.AspectRatioWidth!.Value,
            option.AspectRatioHeight!.Value);
        if (string.IsNullOrWhiteSpace(cropFilter))
            return false;

        EncodePlanner.VideoFrameSize croppedFrameSize = EncodePlanner.ResolveSourceFrameSizeForPlan(info.Width, info.Height, cropFilter);
        double sourcePixelCount = info.Width * (double)info.Height;
        double croppedPixelCount = croppedFrameSize.Width * (double)croppedFrameSize.Height;
        return croppedPixelCount / sourcePixelCount >= NoOpCropAreaThreshold;
    }

    private static bool IsNoOpFrameRateChoice(double frameRate, FrameRateOption option)
    {
        if (option.Value == EncodeFrameRateMode.Original || frameRate <= 0)
            return false;

        return frameRate - (double)option.Value < ExactFrameRateTolerance;
    }

    private static IReadOnlyList<FrameRateOption> BuildVisibleFrameRateOptions(double sourceFrameRate)
    {
        if (sourceFrameRate <= 0)
            return [];

        List<FrameRateOption> options = [];
        if (sourceFrameRate - 30d >= ExactFrameRateTolerance)
            options.Add(new FrameRateOption(EncodeFrameRateMode.Fps30, "30 FPS"));

        if (sourceFrameRate - 60d >= ExactFrameRateTolerance)
            options.Add(new FrameRateOption(EncodeFrameRateMode.Fps60, "60 FPS"));

        options.Add(new FrameRateOption(EncodeFrameRateMode.Original, $"{sourceFrameRate:0.##} FPS"));
        return options;
    }

    private static string FormatCropSummary(string? cropFilter, CropModeOption cropMode)
    {
        if (cropMode.IsAuto)
            return "Auto";

        return cropMode.Label;
    }

    private static string FormatResolutionSummary(string resolutionLabel)
    {
        if (string.IsNullOrWhiteSpace(resolutionLabel))
            return "N/A";

        string compactLabel = resolutionLabel.Trim();
        int commaIndex = compactLabel.IndexOf(',');
        if (commaIndex >= 0)
            compactLabel = compactLabel[..commaIndex];

        return compactLabel
            .Replace(" (original)", string.Empty, StringComparison.Ordinal)
            .Replace(" (downscaled)", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private void OnOutputSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OutputSettingsViewModel.SelectedFrameRateOption))
            OnPropertyChanged(nameof(SelectedFrameRateOption));
    }
}
