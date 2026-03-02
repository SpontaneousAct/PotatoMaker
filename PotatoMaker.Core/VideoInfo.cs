using FFMpegCore;

namespace PotatoMaker.Core;

/// <summary>
/// Lightweight probe result exposing only the metadata front-ends need.
/// </summary>
public record VideoInfo(
    TimeSpan Duration,
    int      Width,
    int      Height,
    double   FrameRate)
{
    /// <summary>
    /// Runs FFProbe on <paramref name="path"/> and returns a <see cref="VideoInfo"/>.
    /// </summary>
    public static async Task<VideoInfo> ProbeAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var analysis = await FFProbe.AnalyseAsync(path);
        var video = analysis.PrimaryVideoStream;

        return new VideoInfo(
            analysis.Duration,
            video?.Width  ?? 0,
            video?.Height ?? 0,
            video?.FrameRate ?? 0);
    }
}
