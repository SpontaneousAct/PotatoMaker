using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace PotatoMaker.Core;

public sealed record FfmpegDownloadProgress(long BytesReceived, long? TotalBytes, string Stage)
{
    public int Percent => TotalBytes is > 0
        ? Math.Clamp((int)Math.Round(BytesReceived * 100d / TotalBytes.Value), 0, 100)
        : 0;
}

/// <summary>
/// Downloads PotatoMaker's pinned FFmpeg archive directly from BtbN, verifies its SHA-256 hash,
/// and installs only ffmpeg.exe and ffprobe.exe into the stable per-user FFmpeg directory.
/// </summary>
public sealed class FfmpegRuntimeInstaller : IDisposable
{
    private const int MaximumDownloadAttempts = 3;
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _managedRoot;

    public FfmpegRuntimeInstaller(HttpClient? httpClient = null, string? managedRoot = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = DownloadTimeout };
        _ownsHttpClient = httpClient is null;
        _managedRoot = Path.GetFullPath(managedRoot ?? FfmpegRuntimePackage.DefaultManagedRoot);
    }

    public string BinaryFolder => _managedRoot;

    internal string LegacyBinaryFolder => Path.Combine(_managedRoot, FfmpegRuntimePackage.RuntimeId, "bin");

    /// <summary>
    /// Detects the flat runtime and migrates the versioned layout used by PotatoMaker 1.9.6 when possible.
    /// A valid legacy runtime remains usable if migration is blocked.
    /// </summary>
    public async Task<FfmpegRuntimeValidationResult> DetectExistingAsync(CancellationToken ct = default)
    {
        FfmpegRuntimeValidationResult current = await FfmpegRuntimeValidator
            .ValidateAsync(BinaryFolder, ct)
            .ConfigureAwait(false);
        if (current.IsValid)
            return current;

        FfmpegRuntimeValidationResult legacy = await FfmpegRuntimeValidator
            .ValidateAsync(LegacyBinaryFolder, ct)
            .ConfigureAwait(false);
        if (!legacy.IsValid)
            return current;

        try
        {
            await RuntimeInstallerFileSystem.EnsureWritableAsync(_managedRoot, ct).ConfigureAwait(false);
            await InstallToolsAsync(LegacyBinaryFolder, ct).ConfigureAwait(false);
            FfmpegRuntimeValidationResult migrated = await FfmpegRuntimeValidator
                .ValidateAsync(BinaryFolder, ct)
                .ConfigureAwait(false);
            if (migrated.IsValid)
            {
                RuntimeInstallerFileSystem.TryDeleteManagedChildDirectory(
                    _managedRoot,
                    Path.Combine(_managedRoot, FfmpegRuntimePackage.RuntimeId));
                return migrated with { Message = $"Migrated FFmpeg to {BinaryFolder}." };
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Compatibility matters more than cleanup. Keep using the verified legacy copy.
        }

        return legacy;
    }

    public async Task<FfmpegRuntimeValidationResult> InstallAsync(
        IProgress<FfmpegDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return FfmpegRuntimeValidationResult.Invalid("The automatic FFmpeg download currently supports Windows only.");

        FfmpegRuntimeValidationResult existing = await DetectExistingAsync(ct).ConfigureAwait(false);
        if (existing.IsValid)
        {
            FFmpegBinaries.Configure(existing.BinaryFolder);
            return existing;
        }

        try
        {
            await RuntimeInstallerFileSystem.EnsureWritableAsync(_managedRoot, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MediaToolInstallationException(
                "FFmpeg",
                "preparing the install folder",
                _managedRoot,
                ex);
        }

        string workingDirectory = RuntimeInstallerFileSystem.CreateWorkingDirectory("ffmpeg");
        string archivePath = Path.Combine(workingDirectory, "ffmpeg.zip");
        string stagingDirectory = Path.Combine(workingDirectory, "extracted");

        try
        {
            await DownloadWithRetryAsync(archivePath, progress, ct).ConfigureAwait(false);
            progress?.Report(new FfmpegDownloadProgress(
                FfmpegRuntimePackage.ArchiveSizeBytes,
                FfmpegRuntimePackage.ArchiveSizeBytes,
                "Verifying FFmpeg download"));

            await VerifyArchiveAsync(archivePath, ct).ConfigureAwait(false);

            progress?.Report(new FfmpegDownloadProgress(
                FfmpegRuntimePackage.ArchiveSizeBytes,
                FfmpegRuntimePackage.ArchiveSizeBytes,
                "Extracting FFmpeg"));
            try
            {
                await ExtractToolsAsync(archivePath, stagingDirectory, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException ||
                ex is IOException && ex is not InvalidDataException)
            {
                throw new MediaToolInstallationException(
                    "FFmpeg",
                    "extracting the verified archive",
                    _managedRoot,
                    ex);
            }

            FfmpegRuntimeValidationResult staged = await FfmpegRuntimeValidator
                .ValidateAsync(stagingDirectory, ct)
                .ConfigureAwait(false);
            if (!staged.IsValid)
                throw new InvalidDataException(staged.Message);

            progress?.Report(new FfmpegDownloadProgress(
                FfmpegRuntimePackage.ArchiveSizeBytes,
                FfmpegRuntimePackage.ArchiveSizeBytes,
                "Installing FFmpeg"));
            try
            {
                await InstallToolsAsync(stagingDirectory, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new MediaToolInstallationException(
                    "FFmpeg",
                    "copying the verified files",
                    _managedRoot,
                    ex);
            }

            FfmpegRuntimeValidationResult installed = await FfmpegRuntimeValidator
                .ValidateAsync(BinaryFolder, ct)
                .ConfigureAwait(false);
            if (!installed.IsValid)
                throw new InvalidDataException(installed.Message);

            RuntimeInstallerFileSystem.TryDeleteManagedChildDirectory(
                _managedRoot,
                Path.Combine(_managedRoot, FfmpegRuntimePackage.RuntimeId));
            FFmpegBinaries.Configure(installed.BinaryFolder);
            progress?.Report(new FfmpegDownloadProgress(
                FfmpegRuntimePackage.ArchiveSizeBytes,
                FfmpegRuntimePackage.ArchiveSizeBytes,
                "FFmpeg ready"));
            return installed;
        }
        finally
        {
            RuntimeInstallerFileSystem.TryDeleteDirectory(workingDirectory);
        }
    }

    internal static async Task ExtractToolsAsync(
        string archivePath,
        string destinationDirectory,
        CancellationToken ct = default)
    {
        await RuntimeInstallerFileSystem.EnsureDirectoryAsync(destinationDirectory, ct).ConfigureAwait(false);
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        await ExtractToolAsync(
            archive,
            "ffmpeg.exe",
            Path.Combine(destinationDirectory, "ffmpeg.exe"),
            ct).ConfigureAwait(false);
        await ExtractToolAsync(
            archive,
            "ffprobe.exe",
            Path.Combine(destinationDirectory, "ffprobe.exe"),
            ct).ConfigureAwait(false);
    }

    private async Task InstallToolsAsync(string sourceDirectory, CancellationToken ct)
    {
        await RuntimeInstallerFileSystem.CopyFileAsync(
            Path.Combine(sourceDirectory, "ffmpeg.exe"),
            Path.Combine(_managedRoot, "ffmpeg.exe"),
            ct).ConfigureAwait(false);
        await RuntimeInstallerFileSystem.CopyFileAsync(
            Path.Combine(sourceDirectory, "ffprobe.exe"),
            Path.Combine(_managedRoot, "ffprobe.exe"),
            ct).ConfigureAwait(false);
    }

    private async Task DownloadWithRetryAsync(
        string archivePath,
        IProgress<FfmpegDownloadProgress>? progress,
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
                progress?.Report(new FfmpegDownloadProgress(
                    File.Exists(archivePath) ? new FileInfo(archivePath).Length : 0,
                    FfmpegRuntimePackage.ArchiveSizeBytes,
                    $"FFmpeg download interrupted; retrying ({attempt + 1} of {MaximumDownloadAttempts})"));
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task DownloadAsync(
        string archivePath,
        IProgress<FfmpegDownloadProgress>? progress,
        CancellationToken ct)
    {
        long existingBytes = File.Exists(archivePath) ? new FileInfo(archivePath).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, FfmpegRuntimePackage.DownloadUrl);
        request.Headers.UserAgent.ParseAdd("PotatoMaker/1.9.7");
        if (existingBytes > 0)
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable &&
            existingBytes == FfmpegRuntimePackage.ArchiveSizeBytes)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
        bool resumed = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!resumed)
            existingBytes = 0;

        long? totalBytes = response.Content.Headers.ContentLength is { } contentLength
            ? existingBytes + contentLength
            : FfmpegRuntimePackage.ArchiveSizeBytes;
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
        long nextUnknownLengthReport = received;
        while (true)
        {
            int read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
                break;

            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            received += read;
            var downloadProgress = new FfmpegDownloadProgress(received, totalBytes, "Downloading FFmpeg from BtbN");
            bool shouldReport = totalBytes is > 0
                ? downloadProgress.Percent != lastReportedPercent
                : received >= nextUnknownLengthReport;
            if (!shouldReport)
                continue;

            lastReportedPercent = downloadProgress.Percent;
            nextUnknownLengthReport = received + (1024 * 1024);
            progress?.Report(downloadProgress);
        }
    }

    private static async Task VerifyArchiveAsync(string archivePath, CancellationToken ct)
    {
        await using FileStream archiveStream = File.OpenRead(archivePath);
        byte[] hash = await SHA256.HashDataAsync(archiveStream, ct).ConfigureAwait(false);
        string actualHash = Convert.ToHexStringLower(hash);
        if (!actualHash.Equals(FfmpegRuntimePackage.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The FFmpeg download failed its SHA-256 verification. The downloaded file was discarded and nothing was installed.");
        }
    }

    private static Task ExtractToolAsync(
        ZipArchive archive,
        string fileName,
        string destinationPath,
        CancellationToken ct)
    {
        ZipArchiveEntry? entry = archive.Entries.SingleOrDefault(entry =>
            entry.FullName.EndsWith($"/bin/{fileName}", StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            throw new InvalidDataException($"The verified FFmpeg archive does not contain {fileName}.");

        return RuntimeInstallerFileSystem.ExecuteWithRetryAsync(
            () => entry.ExtractToFile(destinationPath, overwrite: true),
            ct);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
