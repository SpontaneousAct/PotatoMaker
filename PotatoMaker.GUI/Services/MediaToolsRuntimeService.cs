using PotatoMaker.Core;

namespace PotatoMaker.GUI.Services;

public sealed record MediaToolsRuntimeStatus(
    FfmpegRuntimeValidationResult Ffmpeg,
    LibVlcRuntimeValidationResult LibVlc)
{
    public bool IsReady => Ffmpeg.IsValid && LibVlc.IsValid;

    public long RequiredDownloadBytes =>
        (Ffmpeg.IsValid ? 0 : FfmpegRuntimePackage.ArchiveSizeBytes) +
        (LibVlc.IsValid ? 0 : LibVlcRuntimePackage.ArchiveSizeBytes);
}

public sealed record MediaToolsDownloadProgress(
    string Tool,
    string Stage,
    int Percent,
    int ToolNumber,
    int ToolCount);

public interface IMediaToolsRuntimeService
{
    Task<MediaToolsRuntimeStatus> DetectAsync(CancellationToken ct = default);

    Task<MediaToolsRuntimeStatus> InstallMissingAsync(
        IProgress<MediaToolsDownloadProgress>? progress = null,
        CancellationToken ct = default);
}

/// <summary>
/// Provides one deterministic setup path for the two native media runtimes required by the GUI.
/// </summary>
public sealed class MediaToolsRuntimeService : IMediaToolsRuntimeService
{
    private readonly IFfmpegRuntimeService _ffmpegRuntimeService;
    private readonly ILibVlcRuntimeService _libVlcRuntimeService;
    private readonly SemaphoreSlim _setupSync = new(1, 1);

    public MediaToolsRuntimeService(
        IFfmpegRuntimeService ffmpegRuntimeService,
        ILibVlcRuntimeService libVlcRuntimeService)
    {
        _ffmpegRuntimeService = ffmpegRuntimeService;
        _libVlcRuntimeService = libVlcRuntimeService;
    }

    public async Task<MediaToolsRuntimeStatus> DetectAsync(CancellationToken ct = default)
    {
        FfmpegRuntimeValidationResult ffmpeg = await _ffmpegRuntimeService
            .DetectAndConfigureAsync(ct)
            .ConfigureAwait(false);
        LibVlcRuntimeValidationResult libVlc = _libVlcRuntimeService.Detect();
        return new MediaToolsRuntimeStatus(ffmpeg, libVlc);
    }

    public async Task<MediaToolsRuntimeStatus> InstallMissingAsync(
        IProgress<MediaToolsDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        await _setupSync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            MediaToolsRuntimeStatus status = await DetectAsync(ct).ConfigureAwait(false);
            int toolCount = (status.Ffmpeg.IsValid ? 0 : 1) + (status.LibVlc.IsValid ? 0 : 1);
            int toolNumber = 0;

            if (!status.Ffmpeg.IsValid)
            {
                int currentToolNumber = ++toolNumber;
                var ffmpegProgress = new Progress<FfmpegDownloadProgress>(value =>
                    progress?.Report(new MediaToolsDownloadProgress(
                        "FFmpeg",
                        value.Stage,
                        value.Percent,
                        currentToolNumber,
                        toolCount)));
                FfmpegRuntimeValidationResult ffmpeg = await _ffmpegRuntimeService
                    .DownloadAndConfigureAsync(ffmpegProgress, ct)
                    .ConfigureAwait(false);
                status = status with { Ffmpeg = ffmpeg };
                if (!ffmpeg.IsValid)
                    return status;
            }

            if (!status.LibVlc.IsValid)
            {
                int currentToolNumber = ++toolNumber;
                var libVlcProgress = new Progress<LibVlcDownloadProgress>(value =>
                    progress?.Report(new MediaToolsDownloadProgress(
                        "VLC",
                        value.Stage,
                        value.Percent,
                        currentToolNumber,
                        toolCount)));
                LibVlcRuntimeValidationResult libVlc = await _libVlcRuntimeService
                    .DownloadAsync(libVlcProgress, ct)
                    .ConfigureAwait(false);
                status = status with { LibVlc = libVlc };
            }

            return status;
        }
        finally
        {
            _setupSync.Release();
        }
    }
}
