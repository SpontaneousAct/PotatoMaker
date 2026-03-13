using System.Globalization;

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

    public static EncodePlan PlanStrategy(double durationSecs, int origHeight, double sourceFrameRate, EncodeSettings settings)
    {
        ValidateDuration(durationSecs);
        int bitrate = CalculateVideoBitrate(durationSecs, settings);
        double effectiveBitrate = CalculateEffectiveBitrateForPlanning(bitrate, sourceFrameRate, settings);

        if (effectiveBitrate >= settings.FullHdFloorKbps)
        {
            string label = origHeight <= 1080
                ? $"{origHeight}p (original)"
                : "1080p (downscaled)";
            return new EncodePlan(bitrate, 1, ScaleFilter(1080), label);
        }

        if (effectiveBitrate >= settings.HdFloorKbps)
        {
            string label = origHeight <= 720
                ? $"{origHeight}p (original)"
                : "720p (downscaled)";
            return new EncodePlan(bitrate, 1, ScaleFilter(720), label);
        }

        int parts = 1;
        while (effectiveBitrate < settings.FullHdFloorKbps && parts < settings.MaxParts)
        {
            parts++;
            bitrate = CalculateVideoBitrate(durationSecs / parts, settings);
            effectiveBitrate = CalculateEffectiveBitrateForPlanning(bitrate, sourceFrameRate, settings);
        }

        bitrate = Math.Max(1, bitrate);

        string splitLabel = origHeight <= 1080
            ? $"{Math.Min(origHeight, 1080)}p, {parts} parts"
            : $"1080p (downscaled), {parts} parts";

        return new EncodePlan(bitrate, parts, ScaleFilter(1080), splitLabel);
    }

    public static double ResolveOutputFrameRate(double sourceFrameRate, EncodeSettings settings)
    {
        if (sourceFrameRate <= 0)
            return 0;

        double requestedFrameRate = settings.FrameRateMode switch
        {
            EncodeFrameRateMode.Fps30 => 30,
            EncodeFrameRateMode.Fps60 => 60,
            _ => sourceFrameRate
        };

        return Math.Min(sourceFrameRate, requestedFrameRate);
    }

    public static string? BuildFrameRateFilter(double sourceFrameRate, EncodeSettings settings)
    {
        double outputFrameRate = ResolveOutputFrameRate(sourceFrameRate, settings);
        if (outputFrameRate <= 0 || sourceFrameRate - outputFrameRate < 0.01)
            return null;

        return $"fps={outputFrameRate.ToString("0.###", CultureInfo.InvariantCulture)}";
    }

    public static string? BuildVideoFilter(params string?[] filters)
    {
        string[] normalizedFilters = filters
            .Where(filter => !string.IsNullOrWhiteSpace(filter))
            .Select(filter => filter!.Trim())
            .ToArray();

        return normalizedFilters.Length == 0
            ? null
            : string.Join(",", normalizedFilters);
    }

    private static int CalculateVideoBitrate(double durationSecs, EncodeSettings settings) =>
        (int)(settings.EffectiveTargetMb * 8192.0 / durationSecs) - settings.AudioBitrateKbps;

    private static double CalculateEffectiveBitrateForPlanning(int actualBitrateKbps, double sourceFrameRate, EncodeSettings settings)
    {
        double outputFrameRate = ResolveOutputFrameRate(sourceFrameRate, settings);
        if (sourceFrameRate <= 0 || outputFrameRate <= 0)
            return actualBitrateKbps;

        double frameBudgetMultiplier = sourceFrameRate / outputFrameRate;
        return actualBitrateKbps * Math.Max(1, frameBudgetMultiplier);
    }

    private static void ValidateDuration(double durationSecs)
    {
        if (double.IsNaN(durationSecs) || double.IsInfinity(durationSecs) || durationSecs <= 0)
            throw new InvalidOperationException(InvalidDurationMessage);
    }

    // -2 preserves aspect ratio and keeps the width even for AV1 encoders.
    private static string ScaleFilter(int maxHeight) => $"scale=-2:min(ih\\,{maxHeight})";
}
