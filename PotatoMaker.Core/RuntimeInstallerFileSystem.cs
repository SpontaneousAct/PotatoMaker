namespace PotatoMaker.Core;

/// <summary>
/// File-system operations shared by the FFmpeg and LibVLC installers. Antivirus scanners can
/// briefly hold newly extracted executables, so writes are retried before setup is failed.
/// </summary>
internal static class RuntimeInstallerFileSystem
{
    private const int MaximumAttempts = 8;

    public static string CreateWorkingDirectory(string toolName)
    {
        string safeToolName = new(toolName
            .Where(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            .ToArray());
        return Directory.CreateTempSubdirectory($"PotatoMaker-{safeToolName}-").FullName;
    }

    public static async Task EnsureWritableAsync(string directory, CancellationToken ct)
    {
        await EnsureDirectoryAsync(directory, ct).ConfigureAwait(false);
        string probePath = Path.Combine(directory, $".potatomaker-write-test-{Guid.NewGuid():N}.tmp");

        try
        {
            await ExecuteWithRetryAsync(() =>
            {
                using FileStream probe = new(
                    probePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose);
                probe.WriteByte(0);
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteFile(probePath);
        }
    }

    public static Task EnsureDirectoryAsync(string directory, CancellationToken ct) =>
        ExecuteWithRetryAsync(() => Directory.CreateDirectory(directory), ct);

    public static Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken ct)
    {
        return ExecuteWithRetryAsync(() =>
        {
            string? destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            if (File.Exists(destinationPath))
                File.SetAttributes(destinationPath, FileAttributes.Normal);

            File.Copy(sourcePath, destinationPath, overwrite: true);
        }, ct);
    }

    public static async Task CopyDirectoryContentsAsync(
        string sourceDirectory,
        string destinationDirectory,
        CancellationToken ct)
    {
        string sourceRoot = Path.GetFullPath(sourceDirectory);
        await EnsureDirectoryAsync(destinationDirectory, ct).ConfigureAwait(false);

        foreach (string directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            string relativePath = Path.GetRelativePath(sourceRoot, directory);
            await EnsureDirectoryAsync(Path.Combine(destinationDirectory, relativePath), ct).ConfigureAwait(false);
        }

        foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            string relativePath = Path.GetRelativePath(sourceRoot, file);
            await CopyFileAsync(file, Path.Combine(destinationDirectory, relativePath), ct).ConfigureAwait(false);
        }
    }

    public static void TryDeleteDirectory(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            string tempRoot = Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(fullPath).StartsWith("PotatoMaker-", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (Directory.Exists(fullPath))
                Directory.Delete(fullPath, recursive: true);
        }
        catch
        {
            // Temporary cleanup is best effort.
        }
    }

    public static void TryDeleteManagedChildDirectory(string managedRoot, string childPath)
    {
        try
        {
            string root = Path.GetFullPath(managedRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string child = Path.GetFullPath(childPath);
            if (!child.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return;

            if (Directory.Exists(child))
                Directory.Delete(child, recursive: true);
        }
        catch
        {
            // Migration cleanup must not make an otherwise usable runtime fail.
        }
    }

    public static async Task ExecuteWithRetryAsync(Action action, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < MaximumAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), ct).ConfigureAwait(false);
            }
        }
    }

    private static bool IsRetryable(Exception exception) =>
        exception is UnauthorizedAccessException ||
        exception is IOException and not (FileNotFoundException or DirectoryNotFoundException);

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Temporary cleanup is best effort.
        }
    }
}
