using Microsoft.Extensions.Logging;

namespace PotatoMaker.Core;

public sealed record StrategyAnalysis(
    string InputPath,
    string? CropFilter,
    EncodePlanner.EncodePlan Plan,
    int SourceHeightForPlan)
{
    public string? VideoFilter => EncodePlanner.BuildVideoFilter(CropFilter, Plan.ScaleFilter);
}

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

        string? cropFilter = settings.SkipCropDetect
            ? null
            : await CropDetector.DetectAsync(fullPath, info.Duration, info.Width, info.Height, logger, ct);

        int sourceHeightForPlan = EncodePlanner.ResolveSourceHeightForPlan(info.Height, cropFilter);
        var plan = EncodePlanner.PlanStrategy(info.Duration.TotalSeconds, sourceHeightForPlan, settings);

        return new StrategyAnalysis(fullPath, cropFilter, plan, sourceHeightForPlan);
    }
}
