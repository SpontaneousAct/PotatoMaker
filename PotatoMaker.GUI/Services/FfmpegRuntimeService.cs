using PotatoMaker.Core;

namespace PotatoMaker.GUI.Services;

public interface IFfmpegRuntimeService
{
    FfmpegRuntimeValidationResult? Current { get; }

    Task<FfmpegRuntimeValidationResult> DetectAndConfigureAsync(CancellationToken ct = default);

    Task<FfmpegRuntimeValidationResult> DownloadAndConfigureAsync(
        IProgress<FfmpegDownloadProgress>? progress = null,
        CancellationToken ct = default);
}

/// <summary>
/// Coordinates validation, installation, and activation of PotatoMaker's pinned FFmpeg runtime.
/// </summary>
public sealed class FfmpegRuntimeService : IFfmpegRuntimeService
{
    private readonly FfmpegRuntimeInstaller _installer;
    private readonly SemaphoreSlim _sync = new(1, 1);

    public FfmpegRuntimeService(FfmpegRuntimeInstaller installer)
    {
        _installer = installer;
    }

    public FfmpegRuntimeValidationResult? Current { get; private set; }

    public async Task<FfmpegRuntimeValidationResult> DetectAndConfigureAsync(CancellationToken ct = default)
    {
        if (Current?.IsValid == true)
            return Current;

        await _sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Current?.IsValid == true)
                return Current;

            string? developerOverride = Environment.GetEnvironmentVariable(FFmpegBinaries.FfmpegDirEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(developerOverride))
            {
                FfmpegRuntimeValidationResult overridden = await FfmpegRuntimeValidator.ValidateAsync(
                    developerOverride,
                    ct).ConfigureAwait(false);
                if (overridden.IsValid)
                {
                    FFmpegBinaries.Configure(overridden.BinaryFolder);
                    Current = overridden;
                    return Current;
                }
            }

            Current = await FfmpegRuntimeValidator.ValidateAsync(_installer.BinaryFolder, ct).ConfigureAwait(false);
            if (Current.IsValid)
                FFmpegBinaries.Configure(Current.BinaryFolder);
            return Current;
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<FfmpegRuntimeValidationResult> DownloadAndConfigureAsync(
        IProgress<FfmpegDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        await _sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Current = await _installer.InstallAsync(progress, ct).ConfigureAwait(false);
            return Current;
        }
        finally
        {
            _sync.Release();
        }
    }

}
