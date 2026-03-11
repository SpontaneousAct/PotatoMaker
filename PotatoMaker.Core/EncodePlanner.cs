namespace PotatoMaker.Core;

/// <summary>
/// Plans bitrate, scaling, and splitting decisions.
/// </summary>
public static class EncodePlanner
{
    internal const string InvalidDurationMessage = "The selected clip has no duration. Choose a valid source video or a longer clip.";

    /// <summary>
    /// Describes the chosen encode plan.
    /// </summary>
    public sealed record EncodePlan(int VideoBitrateKbps, int Parts, string? ScaleFilter, string ResolutionLabel);

    public static int ResolveSourceHeightForPlan(int originalHeight, string? cropFilter)
    {
        if (originalHeight <= 0 || string.IsNullOrWhiteSpace(cropFilter))
            return originalHeight;

        string filter = cropFilter.Trim();
        if (!filter.StartsWith("crop=", StringComparison.OrdinalIgnoreCase))
            return originalHeight;

        string[] segments = filter["crop=".Length..].Split(':');
        if (segments.Length != 4)
            return originalHeight;

        return int.TryParse(segments[1], out int cropHeight) && cropHeight > 0
            ? Math.Min(cropHeight, originalHeight)
            : originalHeight;
    }

    public static EncodePlan PlanStrategy(double durationSecs, int origHeight, EncodeSettings settings)
    {
        ValidateDuration(durationSecs);
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
        while (bitrate < settings.FullHdFloorKbps && parts < settings.MaxParts)
        {
            parts++;
            bitrate = CalculateVideoBitrate(durationSecs / parts, settings);
        }

        bitrate = Math.Max(1, bitrate);

        string splitLabel = origHeight <= 1080
            ? $"{Math.Min(origHeight, 1080)}p, {parts} parts"
            : $"1080p (downscaled), {parts} parts";

        return new EncodePlan(bitrate, parts, ScaleFilter(1080), splitLabel);
    }

    public static string? BuildVideoFilter(string? crop, string? scale) =>
        (crop, scale) switch
        {
            (null, null) => null,
            (null, var scaleFilter) => scaleFilter,
            (var cropFilter, null) => cropFilter,
            (var cropFilter, var scaleFilter) => $"{cropFilter},{scaleFilter}"
        };

    private static int CalculateVideoBitrate(double durationSecs, EncodeSettings settings) =>
        (int)(settings.EffectiveTargetMb * 8192.0 / durationSecs) - settings.AudioBitrateKbps;

    private static void ValidateDuration(double durationSecs)
    {
        if (double.IsNaN(durationSecs) || double.IsInfinity(durationSecs) || durationSecs <= 0)
            throw new InvalidOperationException(InvalidDurationMessage);
    }

    // -2 preserves aspect ratio and keeps the width even for AV1 encoders.
    private static string ScaleFilter(int maxHeight) => $"scale=-2:min(ih\\,{maxHeight})";
}
