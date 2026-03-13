using Microsoft.Extensions.Logging.Abstractions;
using PotatoMaker.Core;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Probes source media and builds strategy previews.
/// </summary>
public interface IVideoAnalysisService
{
    Task<VideoInfo> ProbeAsync(string inputPath, CancellationToken ct = default);

    Task<string?> DetectCropAsync(
        string inputPath,
        VideoInfo info,
        CancellationToken ct = default);

    Task<StrategyAnalysis> AnalyzeStrategyAsync(
        string inputPath,
        VideoInfo info,
        EncodeSettings settings,
        string? cropFilter = null,
        VideoClipRange? clipRange = null,
        CancellationToken ct = default);
}

/// <summary>
/// Uses the shared core pipeline helpers to analyze input videos.
/// </summary>
public sealed class VideoAnalysisService : IVideoAnalysisService
{
    public Task<VideoInfo> ProbeAsync(string inputPath, CancellationToken ct = default) =>
        VideoInfo.ProbeAsync(inputPath, ct);

    public Task<string?> DetectCropAsync(
        string inputPath,
        VideoInfo info,
        CancellationToken ct = default) =>
        StrategyAnalyzer.DetectCropAsync(inputPath, info, NullLogger.Instance, ct: ct);

    public Task<StrategyAnalysis> AnalyzeStrategyAsync(
        string inputPath,
        VideoInfo info,
        EncodeSettings settings,
        string? cropFilter = null,
        VideoClipRange? clipRange = null,
        CancellationToken ct = default) =>
        Task.FromResult(StrategyAnalyzer.BuildAnalysis(inputPath, info, settings, cropFilter, clipRange));
}
