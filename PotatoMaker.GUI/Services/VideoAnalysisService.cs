using Microsoft.Extensions.Logging.Abstractions;
using PotatoMaker.Core;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Probes source media and builds strategy previews.
/// </summary>
public interface IVideoAnalysisService
{
    Task<VideoInfo> ProbeAsync(string inputPath, CancellationToken ct = default);

    Task<StrategyAnalysis> AnalyzeStrategyAsync(
        string inputPath,
        VideoInfo info,
        EncodeSettings settings,
        CancellationToken ct = default);
}

/// <summary>
/// Uses the shared core pipeline helpers to analyze input videos.
/// </summary>
public sealed class VideoAnalysisService : IVideoAnalysisService
{
    public Task<VideoInfo> ProbeAsync(string inputPath, CancellationToken ct = default) =>
        VideoInfo.ProbeAsync(inputPath, ct);

    public Task<StrategyAnalysis> AnalyzeStrategyAsync(
        string inputPath,
        VideoInfo info,
        EncodeSettings settings,
        CancellationToken ct = default) =>
        StrategyAnalyzer.AnalyzeAsync(inputPath, info, settings, NullLogger.Instance, ct);
}
