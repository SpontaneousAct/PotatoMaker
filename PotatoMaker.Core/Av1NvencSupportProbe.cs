using System.Diagnostics;

namespace PotatoMaker.Core;

/// <summary>
/// Probes whether FFmpeg can use the AV1 NVENC encoder on the current machine.
/// </summary>
public static class Av1NvencSupportProbe
{
    public static async Task<bool> IsSupportedAsync(CancellationToken ct = default)
    {
        try
        {
            FFmpegBinaries.EnsureConfigured();

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = FFmpegBinaries.FfmpegExecutable(),
                    Arguments = "-hide_banner -loglevel error -f lavfi -i color=c=black:s=1920x1080:d=0.1 -frames:v 1 -c:v av1_nvenc -f null -",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
                return false;

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            _ = await stdoutTask.ConfigureAwait(false);
            _ = await stderrTask.ConfigureAwait(false);

            return process.ExitCode == 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}
