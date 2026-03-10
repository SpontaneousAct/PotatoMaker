using System.Diagnostics;
using Avalonia.Media.Imaging;
using PotatoMaker.Core;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Renders still-frame previews for trim points by invoking FFmpeg directly.
/// </summary>
public interface IVideoFramePreviewService
{
    Task<VideoFramePreviewResult> GenerateAsync(
        string inputPath,
        TimeSpan position,
        CancellationToken ct = default);
}

public sealed record VideoFramePreviewResult(Bitmap? Bitmap, string? ErrorMessage = null);

/// <summary>
/// Uses FFmpeg to extract a single scaled PNG frame.
/// </summary>
public sealed class VideoFramePreviewService : IVideoFramePreviewService
{
    private const int PreviewWidth = 320;

    public async Task<VideoFramePreviewResult> GenerateAsync(
        string inputPath,
        TimeSpan position,
        CancellationToken ct = default)
    {
        string ffmpegPath = FFmpegBinaries.FfmpegExecutable();
        string tempPath = Path.Combine(Path.GetTempPath(), $"potatomaker-preview-{Guid.NewGuid():N}.png");
        string timestamp = position.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        string arguments =
            $"-ss {timestamp} -i \"{inputPath}\" -frames:v 1 -vf \"scale={PreviewWidth}:-2\" -y \"{tempPath}\"";

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = arguments,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return new VideoFramePreviewResult(null, "Preview process could not be started.");

            using CancellationTokenRegistration reg = ct.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
            });

            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(ct);

            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);

            if (process.ExitCode != 0 || !File.Exists(tempPath))
            {
                string? error = stderr
                    .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                    .LastOrDefault()?.Trim();
                return new VideoFramePreviewResult(null, string.IsNullOrWhiteSpace(error) ? "Preview unavailable." : error);
            }

            await using var stream = File.OpenRead(tempPath);
            return new VideoFramePreviewResult(new Bitmap(stream));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new VideoFramePreviewResult(null, ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }
}
