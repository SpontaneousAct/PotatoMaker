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
        int? partNumber = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileNameWithoutExtension);
        ArgumentNullException.ThrowIfNull(settings);

        string normalizedOutputDirectory = Path.GetFullPath(outputDirectory);
        string fileStem = BuildOutputStem(sourceFileNameWithoutExtension, settings);
        string fileName = partNumber is int part
            ? $"{fileStem}_part{part}.mp4"
            : $"{fileStem}.mp4";

        return Path.Combine(normalizedOutputDirectory, fileName);
    }

    public static string BuildOutputStem(string sourceFileNameWithoutExtension, EncodeSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileNameWithoutExtension);
        ArgumentNullException.ThrowIfNull(settings);

        string prefix = EncodeSettings.NormalizeOutputNameAffix(settings.OutputNamePrefix);
        string suffix = EncodeSettings.NormalizeOutputNameAffix(settings.OutputNameSuffix);

        return $"{prefix}{sourceFileNameWithoutExtension}{suffix}";
    }
}
