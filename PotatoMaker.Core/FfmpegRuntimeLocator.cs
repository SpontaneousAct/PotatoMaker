namespace PotatoMaker.Core;

/// <summary>
/// Locates and activates the first compatible FFmpeg installation.
/// </summary>
public static class FfmpegRuntimeLocator
{
    public static async Task<FfmpegRuntimeValidationResult> FindAndConfigureAsync(
        CancellationToken ct = default)
    {
        string? environmentFolder = Environment.GetEnvironmentVariable(FFmpegBinaries.FfmpegDirEnvironmentVariable);
        var candidates = new List<string?>();
        if (!string.IsNullOrWhiteSpace(environmentFolder))
            candidates.Add(environmentFolder);
        candidates.Add(FfmpegRuntimePackage.DefaultManagedBinaryFolder);
        candidates.Add(null); // PATH

        var attemptedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? lastMessage = null;

        foreach (string? candidate in candidates)
        {
            string key = string.IsNullOrWhiteSpace(candidate) ? "<PATH>" : candidate.Trim();
            if (!attemptedFolders.Add(key))
                continue;

            FfmpegRuntimeValidationResult result = await FfmpegRuntimeValidator.ValidateAsync(candidate, ct).ConfigureAwait(false);
            if (!result.IsValid)
            {
                lastMessage = result.Message;
                continue;
            }

            FFmpegBinaries.Configure(result.BinaryFolder);
            return result;
        }

        string detail = string.IsNullOrWhiteSpace(lastMessage) ? string.Empty : $" Last check: {lastMessage}";
        return FfmpegRuntimeValidationResult.Invalid(
            $"No compatible FFmpeg installation was found.{detail}");
    }
}
