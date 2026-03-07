namespace PotatoMaker.Core;

public static class EncodePlanner
{
    public record EncodePlan(int VideoBitrateKbps, int Parts, string? ScaleFilter, string ResolutionLabel);

    public static EncodePlan PlanStrategy(double durationSecs, int origHeight, EncodeSettings settings)
    {
        int bitrate = CalculateVideoBitrate(durationSecs, settings);

        if (bitrate >= settings.FullHdFloorKbps)
        {
            string label = origHeight <= 1080
                ? $"{origHeight}p (original)"
                : "1080p (downscaled)";
            return new EncodePlan(bitrate, 1, ScaleFilter(1080), label);
        }

        if (bitrate >= settings.HdFloorKbps)
        {
            string label = origHeight <= 720
                ? $"{origHeight}p (original)"
                : "720p (downscaled)";
            return new EncodePlan(bitrate, 1, ScaleFilter(720), label);
        }

        int parts = 1;

        while (bitrate < settings.HdFloorKbps && parts < settings.MaxParts)
        {
            parts++;
            bitrate = CalculateVideoBitrate(durationSecs / parts, settings);
        }

        bitrate = Math.Max(bitrate, settings.MinVideoBitrateKbps);

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

    private static int CalculateVideoBitrate(double durationSecs, EncodeSettings settings) =>
        (int)(settings.EffectiveTargetMb * 8192.0 / durationSecs) - settings.AudioBitrateKbps;

    // -2 preserves aspect ratio for any AR (16:9, 21:9, 32:9, …)
    // and ensures width is divisible by 2, required by AV1 encoders.
    // min(ih,N) prevents upscaling sources that are already below the target height.
    // The \\, is an FFmpeg filter-graph escape for the comma separator.
    private static string ScaleFilter(int maxHeight) =>
        $"scale=-2:min(ih\\,{maxHeight})";
}
