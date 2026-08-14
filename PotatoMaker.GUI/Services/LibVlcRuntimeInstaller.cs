using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using PotatoMaker.Core;

namespace PotatoMaker.GUI.Services;

public sealed record LibVlcDownloadProgress(long BytesReceived, long? TotalBytes, string Stage)
{
    public int Percent => TotalBytes is > 0
        ? Math.Clamp((int)Math.Round(BytesReceived * 100d / TotalBytes.Value), 0, 100)
        : 0;
}

/// <summary>
/// Downloads PotatoMaker's pinned official VLC archive directly from VideoLAN, verifies its
/// SHA-256 hash, and installs the native preview runtime into the stable per-user LibVLC directory.
/// </summary>
public sealed class LibVlcRuntimeInstaller : IDisposable
{
    private const int MaximumDownloadAttempts = 3;
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);

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
        _httpClient = httpClient ?? new HttpClient { Timeout = DownloadTimeout };
        _ownsHttpClient = httpClient is null;
        _managedRoot = Path.GetFullPath(managedRoot ?? LibVlcRuntimePackage.DefaultManagedRoot);
    }

    public string RuntimeDirectory => _managedRoot;

    internal string LegacyRuntimeDirectory => Path.Combine(_managedRoot, LibVlcRuntimePackage.RuntimeId);

    /// <summary>
    /// Detects the flat runtime and migrates the versioned layout used by PotatoMaker 1.9.6 when possible.
    /// A valid legacy runtime remains usable if migration is blocked.
    /// </summary>
    public async Task<LibVlcRuntimeValidationResult> DetectExistingAsync(CancellationToken ct = default)
    {
        LibVlcRuntimeValidationResult current = LibVlcRuntimeValidator.ValidateDirectory(RuntimeDirectory);
        if (current.IsValid)
            return current;

        LibVlcRuntimeValidationResult legacy = LibVlcRuntimeValidator.ValidateDirectory(LegacyRuntimeDirectory);
        if (!legacy.IsValid)
            return current;

        try
        {
            await RuntimeInstallerFileSystem.EnsureWritableAsync(_managedRoot, ct).ConfigureAwait(false);
            await RuntimeInstallerFileSystem.CopyDirectoryContentsAsync(
                LegacyRuntimeDirectory,
                _managedRoot,
                ct).ConfigureAwait(false);
            LibVlcRuntimeValidationResult migrated = LibVlcRuntimeValidator.ValidateDirectory(RuntimeDirectory);
            if (migrated.IsValid)
            {
                RuntimeInstallerFileSystem.TryDeleteManagedChildDirectory(_managedRoot, LegacyRuntimeDirectory);
                return migrated with { Message = $"Migrated VLC to {RuntimeDirectory}." };
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Compatibility matters more than cleanup. Keep using the verified legacy copy.
        }

        return legacy;
    }

    public async Task<LibVlcRuntimeValidationResult> InstallAsync(
        IProgress<LibVlcDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
        {
            return LibVlcRuntimeValidationResult.Missing(
                "Automatic VLC setup currently supports 64-bit Windows only.");
        }

        LibVlcRuntimeValidationResult existing = await DetectExistingAsync(ct).ConfigureAwait(false);
        if (existing.IsValid)
            return existing;

        try
        {
            await RuntimeInstallerFileSystem.EnsureWritableAsync(_managedRoot, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MediaToolInstallationException(
                "VLC",
                "preparing the install folder",
                _managedRoot,
                ex);
        }

        string workingDirectory = RuntimeInstallerFileSystem.CreateWorkingDirectory("vlc");
        string archivePath = Path.Combine(workingDirectory, "vlc.zip");
        string stagingDirectory = Path.Combine(workingDirectory, "extracted");

        try
        {
            await DownloadWithRetryAsync(archivePath, progress, ct).ConfigureAwait(false);
            progress?.Report(new LibVlcDownloadProgress(
                LibVlcRuntimePackage.ArchiveSizeBytes,
                LibVlcRuntimePackage.ArchiveSizeBytes,
                "Verifying VLC download"));

            await VerifyArchiveAsync(archivePath, ct).ConfigureAwait(false);

            progress?.Report(new LibVlcDownloadProgress(
                LibVlcRuntimePackage.ArchiveSizeBytes,
                LibVlcRuntimePackage.ArchiveSizeBytes,
                "Extracting VLC"));
            try
            {
                await ExtractRuntimeAsync(archivePath, stagingDirectory, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException ||
                ex is IOException && ex is not InvalidDataException)
            {
                throw new MediaToolInstallationException(
                    "VLC",
                    "extracting the verified archive",
                    _managedRoot,
                    ex);
            }
            LibVlcRuntimeValidationResult staged = LibVlcRuntimeValidator.ValidateDirectory(stagingDirectory);
            if (!staged.IsValid)
                throw new InvalidDataException(staged.Message);

            progress?.Report(new LibVlcDownloadProgress(
                LibVlcRuntimePackage.ArchiveSizeBytes,
                LibVlcRuntimePackage.ArchiveSizeBytes,
                "Installing VLC"));
            try
            {
                await RuntimeInstallerFileSystem.CopyDirectoryContentsAsync(
                    stagingDirectory,
                    _managedRoot,
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new MediaToolInstallationException(
                    "VLC",
                    "copying the verified files",
                    _managedRoot,
                    ex);
            }

            LibVlcRuntimeValidationResult installed = LibVlcRuntimeValidator.ValidateDirectory(RuntimeDirectory);
            if (!installed.IsValid)
                throw new InvalidDataException(installed.Message);

            RuntimeInstallerFileSystem.TryDeleteManagedChildDirectory(_managedRoot, LegacyRuntimeDirectory);
            progress?.Report(new LibVlcDownloadProgress(
                LibVlcRuntimePackage.ArchiveSizeBytes,
                LibVlcRuntimePackage.ArchiveSizeBytes,
                "VLC ready"));
            return installed;
        }
        finally
        {
            RuntimeInstallerFileSystem.TryDeleteDirectory(workingDirectory);
        }
    }

    internal static async Task ExtractRuntimeAsync(
        string archivePath,
        string stagingDirectory,
        CancellationToken ct = default)
    {
        await RuntimeInstallerFileSystem.EnsureDirectoryAsync(stagingDirectory, ct).ConfigureAwait(false);
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
                await RuntimeInstallerFileSystem.EnsureDirectoryAsync(destinationPath, ct).ConfigureAwait(false);
                continue;
            }

            await RuntimeInstallerFileSystem
                .EnsureDirectoryAsync(Path.GetDirectoryName(destinationPath)!, ct)
                .ConfigureAwait(false);
            await RuntimeInstallerFileSystem.ExecuteWithRetryAsync(
                () => entry.ExtractToFile(destinationPath, overwrite: true),
                ct).ConfigureAwait(false);
        }
    }

    private async Task DownloadWithRetryAsync(
        string archivePath,
        IProgress<LibVlcDownloadProgress>? progress,
        CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await DownloadAsync(archivePath, progress, ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (
                (ex is HttpRequestException or IOException or TaskCanceledException) &&
                !ct.IsCancellationRequested &&
                attempt < MaximumDownloadAttempts)
            {
                progress?.Report(new LibVlcDownloadProgress(
                    File.Exists(archivePath) ? new FileInfo(archivePath).Length : 0,
                    LibVlcRuntimePackage.ArchiveSizeBytes,
                    $"VLC download interrupted; retrying ({attempt + 1} of {MaximumDownloadAttempts})"));
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task DownloadAsync(
        string archivePath,
        IProgress<LibVlcDownloadProgress>? progress,
        CancellationToken ct)
    {
        long existingBytes = File.Exists(archivePath) ? new FileInfo(archivePath).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, LibVlcRuntimePackage.DownloadUrl);
        request.Headers.UserAgent.ParseAdd("PotatoMaker/1.9.7");
        if (existingBytes > 0)
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable &&
            existingBytes == LibVlcRuntimePackage.ArchiveSizeBytes)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
        bool resumed = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!resumed)
            existingBytes = 0;

        long? totalBytes = response.Content.Headers.ContentLength is { } contentLength
            ? existingBytes + contentLength
            : LibVlcRuntimePackage.ArchiveSizeBytes;
        await using Stream source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var destination = new FileStream(
            archivePath,
            resumed ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 128,
            useAsync: true);

        byte[] buffer = new byte[1024 * 128];
        long received = existingBytes;
        int lastReportedPercent = -1;
        while (true)
        {
            int read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
                break;

            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            received += read;
            var downloadProgress = new LibVlcDownloadProgress(received, totalBytes, "Downloading VLC from VideoLAN");
            if (downloadProgress.Percent == lastReportedPercent)
                continue;

            lastReportedPercent = downloadProgress.Percent;
            progress?.Report(downloadProgress);
        }
    }

    private static async Task VerifyArchiveAsync(string archivePath, CancellationToken ct)
    {
        await using FileStream archiveStream = File.OpenRead(archivePath);
        byte[] hash = await SHA256.HashDataAsync(archiveStream, ct).ConfigureAwait(false);
        string actualHash = Convert.ToHexStringLower(hash);
        if (!actualHash.Equals(LibVlcRuntimePackage.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The VLC download failed its SHA-256 verification. The downloaded file was discarded and nothing was installed.");
        }
    }

    private static bool ShouldExtract(string relativePath) =>
        IncludedRootFiles.Contains(relativePath, StringComparer.OrdinalIgnoreCase) ||
        IncludedDirectories.Any(directory => relativePath.StartsWith(directory, StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
