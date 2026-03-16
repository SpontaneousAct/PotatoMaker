using FFMpegCore;

namespace PotatoMaker.Core;

/// <summary>
/// Lightweight probe result exposing only the metadata front-ends need.
/// </summary>
public record VideoInfo(
    TimeSpan Duration,
    int      Width,
    int      Height,
    double   FrameRate,
    int?     SourceVideoBitrateKbps = null)
{
    /// <summary>
    /// Runs FFProbe on <paramref name="path"/> and returns a <see cref="VideoInfo"/>.
    /// </summary>
    public static async Task<VideoInfo> ProbeAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string fullPath = Path.GetFullPath(path);
        InputMediaSupport.ThrowIfInvalidPath(fullPath);
        FFmpegBinaries.EnsureConfigured();

        var analysis = await FFProbe.AnalyseAsync(fullPath);
        var video = analysis.PrimaryVideoStream;

        return new VideoInfo(
            analysis.Duration,
            video?.Width  ?? 0,
            video?.Height ?? 0,
            video?.FrameRate ?? 0,
            ParseBitrateKbps(video?.BitRate));
    }

    private static int? ParseBitrateKbps(long? bitRate)
    {
        if (bitRate is not > 0)
            return null;

        return (int)Math.Max(1, Math.Round(bitRate.Value / 1000d, MidpointRounding.AwayFromZero));
    }
}
