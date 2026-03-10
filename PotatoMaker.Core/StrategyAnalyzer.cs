using Microsoft.Extensions.Logging;

namespace PotatoMaker.Core;

/// <summary>
/// Result of crop detection and encode planning for one input file.
/// </summary>
public sealed record StrategyAnalysis(
    string InputPath,
    string? CropFilter,
    EncodePlanner.EncodePlan Plan)
{
    public string? VideoFilter => EncodePlanner.BuildVideoFilter(CropFilter, Plan.ScaleFilter);
}

/// <summary>
/// Builds a strategy preview for a source video.
/// </summary>
public static class StrategyAnalyzer
{
    public static async Task<StrategyAnalysis> AnalyzeAsync(
        string inputPath,
        VideoInfo info,
        EncodeSettings settings,
        ILogger logger,
        CancellationToken ct = default)
    {
        string fullPath = Path.GetFullPath(inputPath);
        InputMediaSupport.ThrowIfInvalidPath(fullPath);

        string? cropFilter = settings.SkipCropDetect
            ? null
            : await CropDetector.DetectAsync(fullPath, info.Duration, info.Width, info.Height, logger, ct);

        int sourceHeightForPlan = EncodePlanner.ResolveSourceHeightForPlan(info.Height, cropFilter);
        var plan = EncodePlanner.PlanStrategy(info.Duration.TotalSeconds, sourceHeightForPlan, settings);

        return new StrategyAnalysis(fullPath, cropFilter, plan);
    }
}
