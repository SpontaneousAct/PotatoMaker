namespace PotatoMaker.Core;

/// <summary>
/// Locates and activates the first compatible FFmpeg installation.
/// </summary>
public static class FfmpegRuntimeLocator
{
    public static async Task<FfmpegRuntimeValidationResult> FindAndConfigureAsync(
        string? preferredFolder = null,
        string? managedFolder = null,
        CancellationToken ct = default)
    {
        string? environmentFolder = Environment.GetEnvironmentVariable(FFmpegBinaries.FfmpegDirEnvironmentVariable);
        string legacyPackagedFolder = Path.Combine(AppContext.BaseDirectory, "ffmpeg");

        IReadOnlyList<string?> candidates = BuildCandidateFolders(
            preferredFolder,
            environmentFolder,
            managedFolder ?? FfmpegRuntimePackage.DefaultManagedBinaryFolder,
            legacyPackagedFolder);

        var attemptedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? lastMessage = null;

        foreach (string? candidate in candidates)
        {
            string key = string.IsNullOrWhiteSpace(candidate) ? "<PATH>" : Path.GetFullPath(candidate);
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

    internal static IReadOnlyList<string?> BuildCandidateFolders(
        string? preferredFolder,
        string? environmentFolder,
        string managedFolder,
        string legacyPackagedFolder)
    {
        var candidates = new List<string?>();
        if (!string.IsNullOrWhiteSpace(preferredFolder))
            candidates.Add(preferredFolder);
        if (!string.IsNullOrWhiteSpace(environmentFolder))
            candidates.Add(environmentFolder);
        candidates.Add(managedFolder ?? FfmpegRuntimePackage.DefaultManagedBinaryFolder);
        candidates.Add(null); // PATH
        candidates.Add(legacyPackagedFolder);
        return candidates;
    }
}
