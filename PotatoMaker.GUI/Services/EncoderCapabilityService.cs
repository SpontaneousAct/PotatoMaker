using System.Diagnostics;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Probes optional encoder capabilities available on the current machine.
/// </summary>
public interface IEncoderCapabilityService
{
    Task<bool> IsAv1NvencSupportedAsync(CancellationToken ct = default);
}

/// <summary>
/// Detects whether AV1 NVENC is available for FFmpeg.
/// </summary>
public sealed class EncoderCapabilityService : IEncoderCapabilityService
{
    private readonly object _sync = new();
    private Task<bool>? _cachedAv1NvencSupport;

    public Task<bool> IsAv1NvencSupportedAsync(CancellationToken ct = default)
    {
        Task<bool> probeTask;
        lock (_sync)
        {
            _cachedAv1NvencSupport ??= ProbeAv1NvencSupportAsync();
            probeTask = _cachedAv1NvencSupport;
        }

        return ct.CanBeCanceled
            ? probeTask.WaitAsync(ct)
            : probeTask;
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
