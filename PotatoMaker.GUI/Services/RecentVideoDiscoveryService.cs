using PotatoMaker.Core;
using System.Security;

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
    Task<IReadOnlyList<RecentVideoFile>> GetRecentVideosAsync(RecentVideoQuery query, CancellationToken ct = default);
}

/// <summary>
/// Reads the filesystem to discover recently saved supported videos.
/// </summary>
public sealed class RecentVideoDiscoveryService : IRecentVideoDiscoveryService
{
    public const int DefaultLimit = 8;
    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false
    };

    public Task<IReadOnlyList<RecentVideoFile>> GetRecentVideosAsync(RecentVideoQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Limit <= 0 || string.IsNullOrWhiteSpace(query.DirectoryPath))
            return Task.FromResult<IReadOnlyList<RecentVideoFile>>([]);

        return Task.Run(() => DiscoverRecentVideos(query, ct), ct);
    }

    private static IReadOnlyList<RecentVideoFile> DiscoverRecentVideos(RecentVideoQuery query, CancellationToken ct)
    {
        string normalizedDirectory;

        try
        {
            normalizedDirectory = Path.GetFullPath(query.DirectoryPath!);
        }
        catch (Exception ex) when (IsFilesystemAccessException(ex))
        {
            return [];
        }

        if (!Directory.Exists(normalizedDirectory))
            return [];

        string? excludedPrefix = NormalizeAffix(query.ExcludedPrefix);
        string? excludedSuffix = NormalizeAffix(query.ExcludedSuffix);
        List<RecentVideoFile> recentVideos = [];
        Stack<string> pendingDirectories = new();
        pendingDirectories.Push(normalizedDirectory);

        while (pendingDirectories.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            string currentDirectory = pendingDirectories.Pop();

            foreach (string filePath in GetFilesSafely(currentDirectory))
            {
                ct.ThrowIfCancellationRequested();
                TryAddRecentVideo(recentVideos, filePath, excludedPrefix, excludedSuffix, query.Limit);
            }

            foreach (string nestedDirectory in GetDirectoriesSafely(currentDirectory))
            {
                ct.ThrowIfCancellationRequested();
                pendingDirectories.Push(nestedDirectory);
            }
        }

        return recentVideos;
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

    private static void TryAddRecentVideo(
        List<RecentVideoFile> recentVideos,
        string filePath,
        string? excludedPrefix,
        string? excludedSuffix,
        int limit)
    {
        if (!InputMediaSupport.IsSupportedPath(filePath))
            return;

        string fileName = Path.GetFileName(filePath);
        if (IsGeneratedOutput(fileName, excludedPrefix, excludedSuffix))
            return;

        DateTimeOffset? lastModified = TryGetLastModified(filePath);
        if (lastModified is null)
            return;

        var candidate = new RecentVideoFile(filePath, fileName, lastModified.Value);
        int insertIndex = recentVideos.FindIndex(existing => CompareRecentVideos(candidate, existing) < 0);

        if (insertIndex < 0)
        {
            if (recentVideos.Count < limit)
                recentVideos.Add(candidate);

            return;
        }

        recentVideos.Insert(insertIndex, candidate);
        if (recentVideos.Count > limit)
            recentVideos.RemoveAt(recentVideos.Count - 1);
    }

    private static int CompareRecentVideos(RecentVideoFile left, RecentVideoFile right)
    {
        int modifiedComparison = right.LastModified.CompareTo(left.LastModified);
        if (modifiedComparison != 0)
            return modifiedComparison;

        int fileNameComparison = StringComparer.OrdinalIgnoreCase.Compare(left.FileName, right.FileName);
        if (fileNameComparison != 0)
            return fileNameComparison;

        return StringComparer.OrdinalIgnoreCase.Compare(left.FullPath, right.FullPath);
    }

    private static DateTimeOffset? TryGetLastModified(string filePath)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTime(filePath));
        }
        catch (Exception ex) when (IsFilesystemAccessException(ex))
        {
            return null;
        }
    }

    private static string[] GetFilesSafely(string directoryPath)
    {
        try
        {
            return Directory.GetFiles(directoryPath, "*", EnumerationOptions);
        }
        catch (Exception ex) when (IsFilesystemAccessException(ex))
        {
            return [];
        }
    }

    private static string[] GetDirectoriesSafely(string directoryPath)
    {
        try
        {
            return Directory.GetDirectories(directoryPath, "*", EnumerationOptions);
        }
        catch (Exception ex) when (IsFilesystemAccessException(ex))
        {
            return [];
        }
    }

    private static bool IsFilesystemAccessException(Exception ex) =>
        ex is ArgumentException
            or DirectoryNotFoundException
            or IOException
            or NotSupportedException
            or PathTooLongException
            or SecurityException
            or UnauthorizedAccessException;
}
