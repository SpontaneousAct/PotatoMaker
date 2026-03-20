namespace PotatoMaker.Core;

/// <summary>
/// Builds output file names for encoded media.
/// </summary>
public static class OutputFileNameBuilder
{
    public static string BuildOutputPath(
        string outputDirectory,
        string sourceFileNameWithoutExtension,
        EncodeSettings settings,
        int? partNumber = null,
        VideoClipRange? clipRange = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileNameWithoutExtension);
        ArgumentNullException.ThrowIfNull(settings);

        string normalizedOutputDirectory = Path.GetFullPath(outputDirectory);
        string fileStem = BuildOutputStem(sourceFileNameWithoutExtension, settings, clipRange);
        string fileName = partNumber is int part
            ? $"{fileStem}_part{part}.mp4"
            : $"{fileStem}.mp4";

        return Path.Combine(normalizedOutputDirectory, fileName);
    }

    public static string BuildOutputStem(
        string sourceFileNameWithoutExtension,
        EncodeSettings settings,
        VideoClipRange? clipRange = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileNameWithoutExtension);
        ArgumentNullException.ThrowIfNull(settings);

        string prefix = EncodeSettings.NormalizeOutputNameAffix(settings.OutputNamePrefix);
        string suffix = EncodeSettings.NormalizeOutputNameAffix(settings.OutputNameSuffix);
        string clipSuffix = clipRange is { } range
            ? $"_{FormatClipBoundary(range.Start)}-{FormatClipBoundary(range.End)}"
            : string.Empty;

        return $"{prefix}{sourceFileNameWithoutExtension}{clipSuffix}{suffix}";
    }

    private static string FormatClipBoundary(TimeSpan value)
    {
        long totalMilliseconds = Math.Max(0, (long)Math.Round(value.TotalMilliseconds, MidpointRounding.AwayFromZero));
        return totalMilliseconds.ToString("D9");
    }
}
