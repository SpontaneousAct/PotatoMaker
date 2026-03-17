using Avalonia.Media.Imaging;
using PotatoMaker.Core;
using System.Diagnostics;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Generates compact thumbnails for recent videos.
/// </summary>
public interface IRecentVideoThumbnailService
{
    Task<Bitmap?> GetThumbnailAsync(string videoPath, CancellationToken ct = default);
}

/// <summary>
/// Disables thumbnail generation while keeping the shell workflow intact.
/// </summary>
public sealed class DisabledRecentVideoThumbnailService : IRecentVideoThumbnailService
{
    public static DisabledRecentVideoThumbnailService Instance { get; } = new();

    private DisabledRecentVideoThumbnailService()
    {
    }

    public Task<Bitmap?> GetThumbnailAsync(string videoPath, CancellationToken ct = default) =>
        Task.FromResult<Bitmap?>(null);
}

/// <summary>
/// Uses FFmpeg to extract a representative frame from a video file.
/// </summary>
public sealed class RecentVideoThumbnailService : IRecentVideoThumbnailService
{
    private const int ThumbnailWidth = 160;
    private static readonly TimeSpan[] SeekOffsets =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.Zero
    ];

    public async Task<Bitmap?> GetThumbnailAsync(string videoPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            return null;

        string ffmpegPath = FFmpegBinaries.FfmpegExecutable();
        string outputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-thumb-{Guid.NewGuid():N}.png");

        try
        {
            foreach (TimeSpan seekOffset in SeekOffsets)
            {
                ct.ThrowIfCancellationRequested();

                bool extracted = await TryExtractThumbnailAsync(ffmpegPath, videoPath, outputPath, seekOffset, ct)
                    .ConfigureAwait(false);
                if (!extracted)
                    continue;

                await using FileStream stream = File.OpenRead(outputPath);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
                buffer.Position = 0;
                return new Bitmap(buffer);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
        finally
        {
            TryDelete(outputPath);
        }

        return null;
    }

    private static async Task<bool> TryExtractThumbnailAsync(
        string ffmpegPath,
        string videoPath,
        string outputPath,
        TimeSpan seekOffset,
        CancellationToken ct)
    {
        TryDelete(outputPath);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-hide_banner");
        process.StartInfo.ArgumentList.Add("-loglevel");
        process.StartInfo.ArgumentList.Add("error");
        process.StartInfo.ArgumentList.Add("-nostdin");
        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-ss");
        process.StartInfo.ArgumentList.Add(seekOffset.ToString(@"hh\:mm\:ss\.fff"));
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(videoPath);
        process.StartInfo.ArgumentList.Add("-frames:v");
        process.StartInfo.ArgumentList.Add("1");
        process.StartInfo.ArgumentList.Add("-vf");
        process.StartInfo.ArgumentList.Add($"scale={ThumbnailWidth}:-2:flags=lanczos");
        process.StartInfo.ArgumentList.Add(outputPath);

        try
        {
            if (!process.Start())
                return false;

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

            return process.ExitCode == 0 &&
                File.Exists(outputPath) &&
                new FileInfo(outputPath).Length > 0;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch
        {
            TryKill(process);
            return false;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static void TryDelete(string path)
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
}
