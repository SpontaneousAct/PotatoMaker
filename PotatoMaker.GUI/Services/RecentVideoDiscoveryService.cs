using PotatoMaker.Core;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Represents a recently modified supported video file.
/// </summary>
public sealed record RecentVideoFile(string FullPath, string FileName, DateTimeOffset LastModified);

/// <summary>
/// Configures how recent videos should be discovered.
/// </summary>
public sealed record RecentVideoQuery(
    string? DirectoryPath,
    string? ExcludedPrefix = null,
    string? ExcludedSuffix = null,
    int Limit = RecentVideoDiscoveryService.DefaultLimit);

/// <summary>
/// Finds recent supported video files from a configured directory.
/// </summary>
public interface IRecentVideoDiscoveryService
{
    IReadOnlyList<RecentVideoFile> GetRecentVideos(RecentVideoQuery query);
}

/// <summary>
/// Reads the filesystem to discover recently saved supported videos.
/// </summary>
public sealed class RecentVideoDiscoveryService : IRecentVideoDiscoveryService
{
    public const int DefaultLimit = 8;

    public IReadOnlyList<RecentVideoFile> GetRecentVideos(RecentVideoQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Limit <= 0 || string.IsNullOrWhiteSpace(query.DirectoryPath))
            return [];

        try
        {
            string normalizedDirectory = Path.GetFullPath(query.DirectoryPath);
            if (!Directory.Exists(normalizedDirectory))
                return [];

            string? excludedPrefix = NormalizeAffix(query.ExcludedPrefix);
            string? excludedSuffix = NormalizeAffix(query.ExcludedSuffix);

            return Directory
                .EnumerateFiles(normalizedDirectory, "*", SearchOption.AllDirectories)
                .Where(InputMediaSupport.IsSupportedPath)
                .Select(path => new FileInfo(path))
                .Where(file => !IsGeneratedOutput(file.Name, excludedPrefix, excludedSuffix))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Take(query.Limit)
                .Select(file => new RecentVideoFile(
                    file.FullName,
                    file.Name,
                    new DateTimeOffset(file.LastWriteTime)))
                .ToArray();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return [];
        }
    }

    private static bool IsGeneratedOutput(string fileName, string? excludedPrefix, string? excludedSuffix)
    {
        string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrEmpty(nameWithoutExtension))
            return false;

        if (excludedPrefix is not null &&
            nameWithoutExtension.StartsWith(excludedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (excludedSuffix is not null &&
            nameWithoutExtension.EndsWith(excludedSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string? NormalizeAffix(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
