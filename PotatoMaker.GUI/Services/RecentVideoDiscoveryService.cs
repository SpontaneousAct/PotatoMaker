using PotatoMaker.Core;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Represents a recently modified supported video file.
/// </summary>
public sealed record RecentVideoFile(string FullPath, string FileName, DateTimeOffset LastModified);

/// <summary>
/// Finds recent supported video files from a configured directory.
/// </summary>
public interface IRecentVideoDiscoveryService
{
    IReadOnlyList<RecentVideoFile> GetRecentVideos(string? directoryPath, int limit = RecentVideoDiscoveryService.DefaultLimit);
}

/// <summary>
/// Reads the filesystem to discover recently saved supported videos.
/// </summary>
public sealed class RecentVideoDiscoveryService : IRecentVideoDiscoveryService
{
    public const int DefaultLimit = 5;

    public IReadOnlyList<RecentVideoFile> GetRecentVideos(string? directoryPath, int limit = DefaultLimit)
    {
        if (limit <= 0 || string.IsNullOrWhiteSpace(directoryPath))
            return [];

        try
        {
            string normalizedDirectory = Path.GetFullPath(directoryPath);
            if (!Directory.Exists(normalizedDirectory))
                return [];

            return Directory
                .EnumerateFiles(normalizedDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(InputMediaSupport.IsSupportedPath)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
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
}
