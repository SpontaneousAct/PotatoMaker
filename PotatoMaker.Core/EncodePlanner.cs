namespace PotatoMaker.Core;

public static class EncodePlanner
{
    // Budget constants
    public const double TargetSizeMb        = 9.5;
    public const double EffectiveTargetMb   = 9.0;
    public const int    AudioBitrateKbps    = 128;
    public const int    MinVideoBitrateKbps = 100;

    // Quality-floor thresholds
    private const int FullHdFloorKbps = 1000;
    private const int HdFloorKbps     = 500;
    private const int MaxSplitParts   = 10;

    public record EncodePlan(int VideoBitrateKbps, int Parts, string? ScaleFilter, string ResolutionLabel);

    public static EncodePlan PlanStrategy(double durationSecs, int origHeight)
    {
        int bitrate = CalculateVideoBitrate(durationSecs);

        if (bitrate >= FullHdFloorKbps)
        {
            string label = origHeight <= 1080
                ? $"{origHeight}p (original)"
                : "1080p (downscaled)";
            return new EncodePlan(bitrate, 1, ScaleFilter(1080), label);
        }

        if (bitrate >= HdFloorKbps)
        {
            string label = origHeight <= 720
                ? $"{origHeight}p (original)"
                : "720p (downscaled)";
            return new EncodePlan(bitrate, 1, ScaleFilter(720), label);
        }

        int parts = 1;

        while (bitrate < HdFloorKbps && parts < MaxSplitParts)
        {
            parts++;
            bitrate = CalculateVideoBitrate(durationSecs / parts);
        }

        bitrate = Math.Max(bitrate, MinVideoBitrateKbps);

        string splitLabel = origHeight <= 1080
            ? $"{Math.Min(origHeight, 1080)}p, {parts} parts"
            : $"1080p (downscaled), {parts} parts";

        return new EncodePlan(bitrate, parts, ScaleFilter(1080), splitLabel);
    }

    public static string? BuildVideoFilter(string? crop, string? scale) =>
        (crop, scale) switch
        {
            (null, null)   => null,
            (null, var s)  => s,
            (var c, null)  => c,
            (var c, var s) => $"{c},{s}"
        };

    private static int CalculateVideoBitrate(double durationSecs) =>
        (int)(EffectiveTargetMb * 8192.0 / durationSecs) - AudioBitrateKbps;

    // -2 preserves aspect ratio for any AR (16:9, 21:9, 32:9, …)
    // and ensures width is divisible by 2, required by AV1 encoders.
    // min(ih,N) prevents upscaling sources that are already below the target height.
    // The \\, is an FFmpeg filter-graph escape for the comma separator.
    private static string ScaleFilter(int maxHeight) =>
        $"scale=-2:min(ih\\,{maxHeight})";
}
