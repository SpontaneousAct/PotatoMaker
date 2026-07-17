using System.IO.Compression;
using System.Security.Cryptography;

namespace PotatoMaker.Core;

public sealed record FfmpegDownloadProgress(long BytesReceived, long? TotalBytes, string Stage)
{
    public int Percent => TotalBytes is > 0
        ? Math.Clamp((int)Math.Round(BytesReceived * 100d / TotalBytes.Value), 0, 100)
        : 0;
}

/// <summary>
/// Downloads the pinned FFmpeg archive directly from its upstream provider,
/// verifies it, and installs only ffmpeg/ffprobe in the user's local app data.
/// </summary>
public sealed class FfmpegRuntimeInstaller : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _managedRoot;

    public FfmpegRuntimeInstaller(HttpClient? httpClient = null, string? managedRoot = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        _managedRoot = Path.GetFullPath(managedRoot ?? FfmpegRuntimePackage.DefaultManagedRoot);
    }

    public string BinaryFolder => Path.Combine(_managedRoot, FfmpegRuntimePackage.RuntimeId, "bin");

    public async Task<FfmpegRuntimeValidationResult> InstallAsync(
        IProgress<FfmpegDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return FfmpegRuntimeValidationResult.Invalid("The automatic FFmpeg download currently supports Windows only.");

        FfmpegRuntimeValidationResult existing = await FfmpegRuntimeValidator.ValidateAsync(BinaryFolder, ct).ConfigureAwait(false);
        if (existing.IsValid)
        {
            FFmpegBinaries.Configure(existing.BinaryFolder);
            return existing;
        }

        Directory.CreateDirectory(_managedRoot);
        string archivePath = Path.Combine(_managedRoot, $"{Guid.NewGuid():N}.download");
        string stagingDirectory = Path.Combine(_managedRoot, $".{FfmpegRuntimePackage.RuntimeId}-{Guid.NewGuid():N}.tmp");
        string destinationDirectory = Path.Combine(_managedRoot, FfmpegRuntimePackage.RuntimeId);

        try
        {
            await DownloadAsync(archivePath, progress, ct).ConfigureAwait(false);
            progress?.Report(new FfmpegDownloadProgress(FfmpegRuntimePackage.ArchiveSizeBytes, FfmpegRuntimePackage.ArchiveSizeBytes, "Verifying download"));

            await using (FileStream archiveStream = File.OpenRead(archivePath))
            {
                byte[] hash = await SHA256.HashDataAsync(archiveStream, ct).ConfigureAwait(false);
                string actualHash = Convert.ToHexStringLower(hash);
                if (!actualHash.Equals(FfmpegRuntimePackage.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The FFmpeg download failed its SHA-256 verification. Nothing was installed.");
            }

            progress?.Report(new FfmpegDownloadProgress(FfmpegRuntimePackage.ArchiveSizeBytes, FfmpegRuntimePackage.ArchiveSizeBytes, "Installing FFmpeg"));
            string stagingBin = Path.Combine(stagingDirectory, "bin");
            Directory.CreateDirectory(stagingBin);
            ExtractTool(archivePath, "ffmpeg.exe", Path.Combine(stagingBin, "ffmpeg.exe"));
            ExtractTool(archivePath, "ffprobe.exe", Path.Combine(stagingBin, "ffprobe.exe"));

            FfmpegRuntimeValidationResult staged = await FfmpegRuntimeValidator.ValidateAsync(stagingBin, ct).ConfigureAwait(false);
            if (!staged.IsValid)
                throw new InvalidDataException(staged.Message);

            EnsureChildPath(destinationDirectory);
            if (Directory.Exists(destinationDirectory))
                Directory.Delete(destinationDirectory, recursive: true);
            Directory.Move(stagingDirectory, destinationDirectory);

            FfmpegRuntimeValidationResult installed = await FfmpegRuntimeValidator.ValidateAsync(BinaryFolder, ct).ConfigureAwait(false);
            if (!installed.IsValid)
                throw new InvalidDataException(installed.Message);

            FFmpegBinaries.Configure(installed.BinaryFolder);
            progress?.Report(new FfmpegDownloadProgress(FfmpegRuntimePackage.ArchiveSizeBytes, FfmpegRuntimePackage.ArchiveSizeBytes, "Ready"));
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
        IProgress<FfmpegDownloadProgress>? progress,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, FfmpegRuntimePackage.DownloadUrl);
        request.Headers.UserAgent.ParseAdd("PotatoMaker/1.9.5");
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength ?? FfmpegRuntimePackage.ArchiveSizeBytes;
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
        long nextUnknownLengthReport = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
                break;

            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            received += read;
            var downloadProgress = new FfmpegDownloadProgress(received, totalBytes, "Downloading FFmpeg");
            bool shouldReport = totalBytes is > 0
                ? downloadProgress.Percent != lastReportedPercent
                : received >= nextUnknownLengthReport;
            if (shouldReport)
            {
                lastReportedPercent = downloadProgress.Percent;
                nextUnknownLengthReport = received + (1024 * 1024);
                progress?.Report(downloadProgress);
            }
        }
    }

    private static void ExtractTool(string archivePath, string fileName, string destinationPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        ZipArchiveEntry? entry = archive.Entries.SingleOrDefault(entry =>
            entry.FullName.EndsWith($"/bin/{fileName}", StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            throw new InvalidDataException($"The verified archive does not contain {fileName}.");

        entry.ExtractToFile(destinationPath, overwrite: false);
    }

    private void EnsureChildPath(string path)
    {
        string root = _managedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to modify a path outside the managed FFmpeg directory.");
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
