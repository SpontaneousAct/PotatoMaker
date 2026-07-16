using LibVLCSharp.Shared;
using LibVlcCore = LibVLCSharp.Shared.Core;

namespace PotatoMaker.GUI.Services;

public interface ILibVlcRuntimeService
{
    LibVlcRuntimeValidationResult? Current { get; }

    LibVlcRuntimeValidationResult Detect();

    LibVlcRuntimeValidationResult DetectAndInitialize();

    Task<LibVlcRuntimeValidationResult> DownloadAsync(
        IProgress<LibVlcDownloadProgress>? progress = null,
        CancellationToken ct = default);
}

/// <summary>
/// Coordinates validation, installation, and process-wide initialization of PotatoMaker's pinned LibVLC runtime.
/// </summary>
public sealed class LibVlcRuntimeService : ILibVlcRuntimeService
{
    private static readonly object InitializationSync = new();
    private static string? _initializedDirectory;

    private readonly LibVlcRuntimeInstaller _installer;
    private readonly SemaphoreSlim _installSync = new(1, 1);

    public LibVlcRuntimeService(LibVlcRuntimeInstaller? installer = null)
    {
        _installer = installer ?? new LibVlcRuntimeInstaller();
    }

    public LibVlcRuntimeValidationResult? Current { get; private set; }

    public LibVlcRuntimeValidationResult Detect()
    {
        string? developerOverride = Environment.GetEnvironmentVariable("POTATOMAKER_LIBVLC_DIR");
        if (!string.IsNullOrWhiteSpace(developerOverride))
        {
            LibVlcRuntimeValidationResult overridden = LibVlcRuntimeValidator.ValidateDirectory(developerOverride);
            if (overridden.IsValid)
            {
                Current = overridden;
                return overridden;
            }
        }

        Current = LibVlcRuntimeValidator.ValidateDirectory(_installer.RuntimeDirectory);
        return Current;
    }

    public LibVlcRuntimeValidationResult DetectAndInitialize()
    {
        lock (InitializationSync)
        {
            if (_initializedDirectory is not null)
                return LibVlcRuntimeValidator.ValidateDirectory(_initializedDirectory);

            LibVlcRuntimeValidationResult result = Detect();
            if (!result.IsValid || result.RuntimeDirectory is null)
                return result;

            try
            {
                string pluginsDirectory = Path.Combine(result.RuntimeDirectory, "plugins");
                Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", pluginsDirectory);
                LibVlcCore.Initialize(result.RuntimeDirectory);
                _initializedDirectory = result.RuntimeDirectory;
                return result;
            }
            catch (Exception ex)
            {
                Current = LibVlcRuntimeValidationResult.Missing(
                    $"The VLC preview runtime could not be loaded: {ex.Message}");
                return Current;
            }
        }
    }

    public async Task<LibVlcRuntimeValidationResult> DownloadAsync(
        IProgress<LibVlcDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        await _installSync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Current = await _installer.InstallAsync(progress, ct).ConfigureAwait(false);
            return Current;
        }
        finally
        {
            _installSync.Release();
        }
    }
}
