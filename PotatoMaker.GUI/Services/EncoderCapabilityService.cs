using PotatoMaker.Core;

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
        return await Av1NvencSupportProbe.IsSupportedAsync().ConfigureAwait(false);
    }
}
