using System.Globalization;

namespace PotatoMaker.Core;

/// <summary>
/// Plans bitrate, scaling, and splitting decisions.
/// </summary>
public static class EncodePlanner
{
    internal const string InvalidDurationMessage = "The selected clip has no duration. Choose a valid source video or a longer clip.";
    private const int FullHdWidth = 1920;
    private const int FullHdHeight = 1080;
    private const int HdWidth = 1280;
    private const int HdHeight = 720;

    public readonly record struct VideoFrameSize(int Width, int Height);

    /// <summary>
    /// Describes the chosen encode plan.
    /// </summary>
    public sealed record EncodePlan(
        int VideoBitrateKbps,
        int Parts,
        string? ScaleFilter,
        string ResolutionLabel,
        bool IsBitrateCappedToSource = false,
        int? SourceVideoBitrateKbps = null);

    public static VideoFrameSize ResolveSourceFrameSizeForPlan(int originalWidth, int originalHeight, string? cropFilter)
    {
        if (originalWidth <= 0 || originalHeight <= 0 || string.IsNullOrWhiteSpace(cropFilter))
            return new VideoFrameSize(originalWidth, originalHeight);

        string filter = cropFilter.Trim();
        if (!filter.StartsWith("crop=", StringComparison.OrdinalIgnoreCase))
            return new VideoFrameSize(originalWidth, originalHeight);

        string[] segments = filter["crop=".Length..].Split(':');
        if (segments.Length != 4)
            return new VideoFrameSize(originalWidth, originalHeight);

        if (!int.TryParse(segments[0], out int cropWidth) ||
            !int.TryParse(segments[1], out int cropHeight) ||
            cropWidth <= 0 ||
            cropHeight <= 0)
        {
            return new VideoFrameSize(originalWidth, originalHeight);
        }

        return new VideoFrameSize(
            Math.Min(cropWidth, originalWidth),
            Math.Min(cropHeight, originalHeight));
    }

    public static EncodePlan PlanStrategy(double durationSecs, int sourceWidth, int sourceHeight, double sourceFrameRate, EncodeSettings settings)
    {
        ValidateDuration(durationSecs);
        int bitrate = CalculateVideoBitrate(durationSecs, settings);
        double effectiveBitrate = CalculateEffectiveBitrateForPlanning(bitrate, sourceFrameRate, settings);
        VideoFrameSize sourceFrameSize = NormalizeSourceFrameSize(sourceWidth, sourceHeight);
        PlannedResolution highResolution = BuildResolutionPlan(sourceFrameSize, FullHdHeight, settings.FullHdFloorKbps, "1080p");
        PlannedResolution mediumResolution = BuildResolutionPlan(sourceFrameSize, HdHeight, settings.HdFloorKbps, "720p");

        if (effectiveBitrate >= highResolution.RequiredBitrateKbps)
            return new EncodePlan(bitrate, 1, highResolution.ScaleFilter, highResolution.Label);

        if (effectiveBitrate >= mediumResolution.RequiredBitrateKbps)
            return new EncodePlan(bitrate, 1, mediumResolution.ScaleFilter, mediumResolution.Label);

        int parts = 1;
        while (effectiveBitrate < highResolution.RequiredBitrateKbps && parts < settings.MaxParts)
        {
            parts++;
            bitrate = CalculateVideoBitrate(durationSecs / parts, settings);
            effectiveBitrate = CalculateEffectiveBitrateForPlanning(bitrate, sourceFrameRate, settings);
        }

        bitrate = Math.Max(1, bitrate);
        string splitLabel = $"{highResolution.Label.Replace(" (original)", string.Empty, StringComparison.Ordinal).Replace(" (downscaled)", string.Empty, StringComparison.Ordinal)}, {parts} parts";
        return new EncodePlan(bitrate, parts, highResolution.ScaleFilter, splitLabel);
    }

    public static EncodePlan ApplySourceVideoBitrateCap(EncodePlan plan, int? sourceVideoBitrateKbps)
    {
        int? normalizedSourceBitrateKbps = NormalizeSourceVideoBitrate(sourceVideoBitrateKbps);
        if (normalizedSourceBitrateKbps is null)
            return plan;

        if (plan.VideoBitrateKbps <= normalizedSourceBitrateKbps.Value)
            return plan with
            {
                IsBitrateCappedToSource = false,
                SourceVideoBitrateKbps = normalizedSourceBitrateKbps
            };

        return plan with
        {
            VideoBitrateKbps = Math.Max(1, normalizedSourceBitrateKbps.Value),
            IsBitrateCappedToSource = true,
            SourceVideoBitrateKbps = normalizedSourceBitrateKbps
        };
    }

    public static string? BuildCenteredCropFilterForAspectRatio(
        int sourceWidth,
        int sourceHeight,
        int aspectRatioWidth,
        int aspectRatioHeight)
    {
        if (sourceWidth <= 0 ||
            sourceHeight <= 0 ||
            aspectRatioWidth <= 0 ||
            aspectRatioHeight <= 0)
        {
            return null;
        }

        long widthScaled = (long)sourceWidth * aspectRatioHeight;
        long heightScaled = (long)sourceHeight * aspectRatioWidth;
        if (widthScaled == heightScaled)
            return null;

        int cropWidth = sourceWidth;
        int cropHeight = sourceHeight;
        if (widthScaled > heightScaled)
        {
            cropWidth = (int)Math.Floor(sourceHeight * (double)aspectRatioWidth / aspectRatioHeight);
            cropWidth = NormalizeCropDimension(cropWidth, sourceWidth);
            cropWidth = AdjustCropDimensionForCenteredOffset(cropWidth, sourceWidth);
        }
        else
        {
            cropHeight = (int)Math.Floor(sourceWidth * (double)aspectRatioHeight / aspectRatioWidth);
            cropHeight = NormalizeCropDimension(cropHeight, sourceHeight);
            cropHeight = AdjustCropDimensionForCenteredOffset(cropHeight, sourceHeight);
        }

        if (cropWidth <= 0 ||
            cropHeight <= 0 ||
            (cropWidth == sourceWidth && cropHeight == sourceHeight))
        {
            return null;
        }

        int offsetX = (sourceWidth - cropWidth) / 2;
        int offsetY = (sourceHeight - cropHeight) / 2;
        return $"crop={cropWidth}:{cropHeight}:{offsetX}:{offsetY}";
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

    private static int? NormalizeSourceVideoBitrate(int? sourceVideoBitrateKbps) =>
        sourceVideoBitrateKbps is > 0 ? sourceVideoBitrateKbps : null;

    private static VideoFrameSize NormalizeSourceFrameSize(int sourceWidth, int sourceHeight) =>
        new(
            Math.Max(1, sourceWidth),
            Math.Max(1, sourceHeight));

    private static PlannedResolution BuildResolutionPlan(
        VideoFrameSize sourceFrameSize,
        int maxHeight,
        int baseRequiredBitrateKbps,
        string downscaledLabel)
    {
        bool usesOriginalResolution = sourceFrameSize.Height <= maxHeight;
        VideoFrameSize outputFrameSize = usesOriginalResolution
            ? sourceFrameSize
            : ScaleFrameSize(sourceFrameSize, maxHeight);
        int referencePixelCount = maxHeight switch
        {
            FullHdHeight => FullHdWidth * FullHdHeight,
            HdHeight => HdWidth * HdHeight,
            _ => maxHeight * maxHeight
        };
        int outputPixelCount = Math.Max(1, outputFrameSize.Width * outputFrameSize.Height);
        int requiredBitrateKbps = Math.Max(
            1,
            (int)Math.Ceiling(baseRequiredBitrateKbps * (outputPixelCount / (double)referencePixelCount)));
        string label = usesOriginalResolution
            ? $"{outputFrameSize.Height}p (original)"
            : $"{downscaledLabel} (downscaled)";
        string? scaleFilter = usesOriginalResolution ? null : ScaleFilter(maxHeight);
        return new PlannedResolution(requiredBitrateKbps, scaleFilter, label);
    }

    private static VideoFrameSize ScaleFrameSize(VideoFrameSize sourceFrameSize, int targetHeight)
    {
        if (sourceFrameSize.Height <= targetHeight)
            return sourceFrameSize;

        int scaledWidth = (int)Math.Round(sourceFrameSize.Width * (targetHeight / (double)sourceFrameSize.Height), MidpointRounding.AwayFromZero);
        scaledWidth = Math.Max(2, scaledWidth);
        if (scaledWidth % 2 != 0)
            scaledWidth--;

        return new VideoFrameSize(scaledWidth, targetHeight);
    }

    private static int NormalizeCropDimension(int cropSize, int maxSize)
    {
        cropSize = Math.Clamp(cropSize, 1, maxSize);
        if (cropSize == maxSize)
            return cropSize;

        return cropSize % 2 == 0
            ? cropSize
            : cropSize - 1;
    }

    private static int AdjustCropDimensionForCenteredOffset(int cropSize, int sourceSize)
    {
        if (cropSize <= 2 || cropSize >= sourceSize)
            return cropSize;

        int margin = sourceSize - cropSize;
        return margin % 4 == 2
            ? cropSize - 2
            : cropSize;
    }

    // -2 preserves aspect ratio and keeps the width even for AV1 encoders.
    private static string ScaleFilter(int maxHeight) => $"scale=-2:min(ih\\,{maxHeight})";

    private sealed record PlannedResolution(
        int RequiredBitrateKbps,
        string? ScaleFilter,
        string Label);
}
