using PotatoMaker.Core;
using PotatoMaker.GUI.Services;
using Xunit;

namespace PotatoMaker.Tests.Services;

public sealed class MediaToolsRuntimeServiceTests
{
    [Fact]
    public async Task InstallMissingAsync_InstallsBothRequiredTools()
    {
        var ffmpeg = new StubFfmpegRuntimeService(isInstalled: false);
        var libVlc = new StubLibVlcRuntimeService(isInstalled: false);
        var service = new MediaToolsRuntimeService(ffmpeg, libVlc);

        MediaToolsRuntimeStatus result = await service.InstallMissingAsync();

        Assert.True(result.IsReady);
        Assert.Equal(1, ffmpeg.DownloadCount);
        Assert.Equal(1, libVlc.DownloadCount);
    }

    [Fact]
    public async Task InstallMissingAsync_DoesNotRedownloadVerifiedTools()
    {
        var ffmpeg = new StubFfmpegRuntimeService(isInstalled: true);
        var libVlc = new StubLibVlcRuntimeService(isInstalled: true);
        var service = new MediaToolsRuntimeService(ffmpeg, libVlc);

        MediaToolsRuntimeStatus result = await service.InstallMissingAsync();

        Assert.True(result.IsReady);
        Assert.Equal(0, ffmpeg.DownloadCount);
        Assert.Equal(0, libVlc.DownloadCount);
    }

    [Fact]
    public async Task InstallMissingAsync_StopsWhenFfmpegInstallationFails()
    {
        var ffmpeg = new StubFfmpegRuntimeService(isInstalled: false, downloadSucceeds: false);
        var libVlc = new StubLibVlcRuntimeService(isInstalled: false);
        var service = new MediaToolsRuntimeService(ffmpeg, libVlc);

        MediaToolsRuntimeStatus result = await service.InstallMissingAsync();

        Assert.False(result.IsReady);
        Assert.Equal(1, ffmpeg.DownloadCount);
        Assert.Equal(0, libVlc.DownloadCount);
    }

    private sealed class StubFfmpegRuntimeService(
        bool isInstalled,
        bool downloadSucceeds = true) : IFfmpegRuntimeService
    {
        private bool _isInstalled = isInstalled;

        public int DownloadCount { get; private set; }

        public FfmpegRuntimeValidationResult? Current { get; private set; }

        public Task<FfmpegRuntimeValidationResult> DetectAndConfigureAsync(CancellationToken ct = default)
        {
            Current = Result(_isInstalled);
            return Task.FromResult(Current);
        }

        public Task<FfmpegRuntimeValidationResult> DownloadAndConfigureAsync(
            IProgress<FfmpegDownloadProgress>? progress = null,
            CancellationToken ct = default)
        {
            DownloadCount++;
            _isInstalled = downloadSucceeds;
            Current = Result(_isInstalled);
            return Task.FromResult(Current);
        }

        private static FfmpegRuntimeValidationResult Result(bool valid) => valid
            ? new(true, @"C:\media-tools\ffmpeg", "FFmpeg test runtime", "Ready")
            : FfmpegRuntimeValidationResult.Invalid("Missing");
    }

    private sealed class StubLibVlcRuntimeService(bool isInstalled) : ILibVlcRuntimeService
    {
        private bool _isInstalled = isInstalled;

        public int DownloadCount { get; private set; }

        public LibVlcRuntimeValidationResult? Current { get; private set; }

        public LibVlcRuntimeValidationResult Detect()
        {
            Current = Result(_isInstalled);
            return Current;
        }

        public LibVlcRuntimeValidationResult DetectAndInitialize() => Detect();

        public Task<LibVlcRuntimeValidationResult> DownloadAsync(
            IProgress<LibVlcDownloadProgress>? progress = null,
            CancellationToken ct = default)
        {
            DownloadCount++;
            _isInstalled = true;
            Current = Result(valid: true);
            return Task.FromResult(Current);
        }

        private static LibVlcRuntimeValidationResult Result(bool valid) => valid
            ? new(true, @"C:\media-tools\vlc", "3.0.23", "Ready")
            : LibVlcRuntimeValidationResult.Missing("Missing");
    }
}
