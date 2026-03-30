using Microsoft.Extensions.Logging;

namespace PotatoMaker.Core;

/// <summary>
/// Result of crop detection and encode planning for one input file.
/// </summary>
public sealed record StrategyAnalysis(
    string InputPath,
    string? CropFilter,
    string? FrameRateFilter,
    double OutputFrameRate,
    EncodePlanner.EncodePlan Plan)
{
    public string? VideoFilter => EncodePlanner.BuildVideoFilter(CropFilter, Plan.ScaleFilter, FrameRateFilter);
}

/// <summary>
/// Builds a strategy preview for a source video.
/// </summary>
public static class StrategyAnalyzer
{
    public static async Task<string?> DetectCropAsync(
        string inputPath,
        VideoInfo info,
        ILogger logger,
        VideoClipRange? clipRange = null,
        CancellationToken ct = default)
    {
        string fullPath = Path.GetFullPath(inputPath);
        InputMediaSupport.ThrowIfInvalidPath(fullPath);

        VideoClipRange effectiveRange = (clipRange ?? VideoClipRange.Full(info.Duration)).Normalize(info.Duration);
        TimeSpan effectiveDuration = effectiveRange.Duration;
        if (effectiveDuration <= TimeSpan.Zero)
            throw new InvalidOperationException(EncodePlanner.InvalidDurationMessage);

        return await CropDetector.DetectAsync(
            fullPath,
            effectiveDuration,
            info.Width,
            info.Height,
            logger,
            effectiveRange.Start,
            ct);
    }

    public static StrategyAnalysis BuildAnalysis(
        string inputPath,
        VideoInfo info,
        EncodeSettings settings,
        string? cropFilter,
        VideoClipRange? clipRange = null)
    {
        string fullPath = Path.GetFullPath(inputPath);
        InputMediaSupport.ThrowIfInvalidPath(fullPath);

        VideoClipRange effectiveRange = (clipRange ?? VideoClipRange.Full(info.Duration)).Normalize(info.Duration);
        TimeSpan effectiveDuration = effectiveRange.Duration;
        if (effectiveDuration <= TimeSpan.Zero)
            throw new InvalidOperationException(EncodePlanner.InvalidDurationMessage);

        EncodePlanner.VideoFrameSize sourceFrameSizeForPlan = EncodePlanner.ResolveSourceFrameSizeForPlan(info.Width, info.Height, cropFilter);
        double outputFrameRate = EncodePlanner.ResolveOutputFrameRate(info.FrameRate, settings);
        string? frameRateFilter = EncodePlanner.BuildFrameRateFilter(info.FrameRate, settings);
        var plan = EncodePlanner.ApplySourceVideoBitrateCap(
            EncodePlanner.PlanStrategy(
                effectiveDuration.TotalSeconds,
                sourceFrameSizeForPlan.Width,
                sourceFrameSizeForPlan.Height,
                info.FrameRate,
                settings),
            info.SourceVideoBitrateKbps);

        return new StrategyAnalysis(fullPath, cropFilter, frameRateFilter, outputFrameRate, plan);
    }

    public static async Task<StrategyAnalysis> AnalyzeAsync(
        string inputPath,
        VideoInfo info,
        EncodeSettings settings,
        ILogger logger,
        VideoClipRange? clipRange = null,
        CancellationToken ct = default)
    {
        string? cropFilter = settings.SkipCropDetect
            ? null
            : await DetectCropAsync(inputPath, info, logger, clipRange, ct);

        return BuildAnalysis(inputPath, info, settings, cropFilter, clipRange);
    }
}
