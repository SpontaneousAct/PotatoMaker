using System.IO.Compression;
using System.Security.Cryptography;

namespace PotatoMaker.GUI.Services;

public sealed record LibVlcDownloadProgress(long BytesReceived, long? TotalBytes, string Stage)
{
    public int Percent => TotalBytes is > 0
        ? Math.Clamp((int)Math.Round(BytesReceived * 100d / TotalBytes.Value), 0, 100)
        : 0;
}

/// <summary>
/// Downloads the pinned official VLC archive, verifies it, and installs the native preview runtime.
/// </summary>
public sealed class LibVlcRuntimeInstaller : IDisposable
{
    private static readonly string[] IncludedRootFiles =
    [
        "libvlc.dll",
        "libvlccore.dll",
        "COPYING.txt",
        "AUTHORS.txt",
        "THANKS.txt",
        "NEWS.txt",
        "README.txt"
    ];

    private static readonly string[] IncludedDirectories = ["plugins/", "lua/", "hrtfs/"];

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _managedRoot;

    public LibVlcRuntimeInstaller(HttpClient? httpClient = null, string? managedRoot = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        _managedRoot = Path.GetFullPath(managedRoot ?? LibVlcRuntimePackage.DefaultManagedRoot);
    }

    public string RuntimeDirectory => Path.Combine(_managedRoot, LibVlcRuntimePackage.RuntimeId);

    public async Task<LibVlcRuntimeValidationResult> InstallAsync(
        IProgress<LibVlcDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
        {
            return LibVlcRuntimeValidationResult.Missing(
                "Automatic VLC setup currently supports 64-bit Windows only.");
        }

        LibVlcRuntimeValidationResult existing = LibVlcRuntimeValidator.ValidateDirectory(RuntimeDirectory);
        if (existing.IsValid)
            return existing;

        Directory.CreateDirectory(_managedRoot);
        string archivePath = Path.Combine(_managedRoot, $"{Guid.NewGuid():N}.download");
        string stagingDirectory = Path.Combine(_managedRoot, $".{LibVlcRuntimePackage.RuntimeId}-{Guid.NewGuid():N}.tmp");
        string destinationDirectory = RuntimeDirectory;

        try
        {
            await DownloadAsync(archivePath, progress, ct).ConfigureAwait(false);
            progress?.Report(new LibVlcDownloadProgress(
                LibVlcRuntimePackage.ArchiveSizeBytes,
                LibVlcRuntimePackage.ArchiveSizeBytes,
                "Verifying VLC"));

            await using (FileStream archiveStream = File.OpenRead(archivePath))
            {
                byte[] hash = await SHA256.HashDataAsync(archiveStream, ct).ConfigureAwait(false);
                string actualHash = Convert.ToHexStringLower(hash);
                if (!actualHash.Equals(LibVlcRuntimePackage.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The VLC download failed its SHA-256 verification. Nothing was installed.");
            }

            progress?.Report(new LibVlcDownloadProgress(
                LibVlcRuntimePackage.ArchiveSizeBytes,
                LibVlcRuntimePackage.ArchiveSizeBytes,
                "Installing VLC"));
            ExtractRuntime(archivePath, stagingDirectory);
            LibVlcRuntimeValidationResult staged = LibVlcRuntimeValidator.ValidateDirectory(stagingDirectory);
            if (!staged.IsValid)
                throw new InvalidDataException(staged.Message);

            EnsureChildPath(destinationDirectory);
            if (Directory.Exists(destinationDirectory))
                Directory.Delete(destinationDirectory, recursive: true);
            Directory.Move(stagingDirectory, destinationDirectory);

            LibVlcRuntimeValidationResult installed = LibVlcRuntimeValidator.ValidateDirectory(RuntimeDirectory);
            if (!installed.IsValid)
                throw new InvalidDataException(installed.Message);

            progress?.Report(new LibVlcDownloadProgress(
                LibVlcRuntimePackage.ArchiveSizeBytes,
                LibVlcRuntimePackage.ArchiveSizeBytes,
                "VLC ready"));
            return installed;
        }
        finally
        {
            TryDeleteFile(archivePath);
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private async Task DownloadAsync(
        string archivePath,
        IProgress<LibVlcDownloadProgress>? progress,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LibVlcRuntimePackage.DownloadUrl);
        request.Headers.UserAgent.ParseAdd("PotatoMaker/1.9.5");
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength ?? LibVlcRuntimePackage.ArchiveSizeBytes;
        await using Stream source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var destination = new FileStream(
            archivePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 128,
            useAsync: true);

        byte[] buffer = new byte[1024 * 128];
        long received = 0;
        int lastReportedPercent = -1;
        while (true)
        {
            int read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
                break;

            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            received += read;
            var downloadProgress = new LibVlcDownloadProgress(received, totalBytes, "Downloading VLC");
            if (downloadProgress.Percent == lastReportedPercent)
                continue;

            lastReportedPercent = downloadProgress.Percent;
            progress?.Report(downloadProgress);
        }
    }

    private static void ExtractRuntime(string archivePath, string stagingDirectory)
    {
        Directory.CreateDirectory(stagingDirectory);
        string stagingRoot = Path.GetFullPath(stagingDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(LibVlcRuntimePackage.ArchiveRoot, StringComparison.Ordinal))
                continue;

            string relativePath = entry.FullName[LibVlcRuntimePackage.ArchiveRoot.Length..];
            if (string.IsNullOrWhiteSpace(relativePath) || !ShouldExtract(relativePath))
                continue;

            string destinationPath = Path.GetFullPath(Path.Combine(
                stagingDirectory,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!destinationPath.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The VLC archive contains an unsafe path.");

            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: false);
        }
    }

    private static bool ShouldExtract(string relativePath) =>
        IncludedRootFiles.Contains(relativePath, StringComparer.OrdinalIgnoreCase) ||
        IncludedDirectories.Any(directory => relativePath.StartsWith(directory, StringComparison.OrdinalIgnoreCase));

    private void EnsureChildPath(string path)
    {
        string root = _managedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to modify a path outside the managed VLC directory.");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            EnsureChildPath(path);
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
