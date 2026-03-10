using Microsoft.Extensions.Logging;
using PotatoMaker.Core;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Describes one encode run requested by the GUI.
/// </summary>
public sealed record EncodeRequest(
    string InputPath,
    string OutputDirectory,
    VideoInfo Info,
    StrategyAnalysis Strategy,
    EncodeSettings Settings,
    VideoClipRange? ClipRange = null);

/// <summary>
/// Runs encodes for the GUI.
/// </summary>
public interface IVideoEncodingService
{
    Task RunAsync(
        EncodeRequest request,
        ILogger<ProcessingPipeline> logger,
        IProgress<EncodeProgress>? progress = null,
        CancellationToken ct = default);
}

/// <summary>
/// Executes the shared processing pipeline for desktop requests.
/// </summary>
public sealed class VideoEncodingService : IVideoEncodingService
{
    public Task RunAsync(
        EncodeRequest request,
        ILogger<ProcessingPipeline> logger,
        IProgress<EncodeProgress>? progress = null,
        CancellationToken ct = default)
    {
        var pipeline = new ProcessingPipeline(
            request.InputPath,
            request.Info,
            request.Settings,
            logger,
            progress,
            request.OutputDirectory,
            request.ClipRange);

        return pipeline.RunAsync(request.Strategy, ct);
    }
}
