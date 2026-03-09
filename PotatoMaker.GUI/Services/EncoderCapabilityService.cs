using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Probes optional encoder capabilities available on the current machine.
/// </summary>
public interface IEncoderCapabilityService
{
    Task<bool> IsAv1NvencSupportedAsync(CancellationToken ct = default);
}

public sealed class EncoderCapabilityService : IEncoderCapabilityService
{
    private readonly object _sync = new();
    private Task<bool>? _cachedAv1NvencSupport;

    public Task<bool> IsAv1NvencSupportedAsync(CancellationToken ct = default)
    {
        lock (_sync)
        {
            _cachedAv1NvencSupport ??= ProbeAv1NvencSupportAsync();
            return _cachedAv1NvencSupport;
        }
    }

    private static async Task<bool> ProbeAv1NvencSupportAsync()
    {
        try
        {
            string ffmpegPath = PotatoMaker.Core.FFmpegBinaries.FfmpegExecutable();
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    // AV1 NVENC minimum dimensions vary by driver/GPU, so use a widely valid test size.
                    Arguments = "-hide_banner -loglevel error -f lavfi -i color=c=black:s=1920x1080:d=0.1 -frames:v 1 -c:v av1_nvenc -f null -",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
                return false;

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            _ = await stdoutTask.ConfigureAwait(false);
            _ = await stderrTask.ConfigureAwait(false);

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
