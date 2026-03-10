using Xunit;
using PotatoMaker.Core;
using PotatoMaker.GUI.Services;
using PotatoMaker.GUI.ViewModels;

namespace PotatoMaker.Tests.ViewModels;

public sealed class EncodeWorkspaceViewModelTests
{
    [Fact]
    public async Task SelectingFile_PopulatesProbeAndStrategySummary()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(inputPath, "video");

        try
        {
            var analysisService = new RecordingAnalysisService();
            var previewService = new RecordingFramePreviewService();
            var workspace = new EncodeWorkspaceViewModel(
                analysisService,
                new NoOpEncodingService(),
                previewService,
                new StaticEncoderCapabilityService(),
                null,
                initializeEncoderSupport: false);

            bool accepted = workspace.FileInput.SetFile(inputPath);
            Assert.True(accepted);

            await analysisService.WaitForStrategyCountAsync(1);
            (string _, TimeSpan startPreviewPosition) = await previewService.WaitForRequestCountAsync(1);
            (string _, TimeSpan endPreviewPosition) = await previewService.WaitForRequestCountAsync(2);

            Assert.True(workspace.VideoSummary.HasData);
            Assert.True(workspace.VideoSummary.HasStrategy);
            Assert.Equal("1920x1080", workspace.VideoSummary.Resolution);
            Assert.Equal("0:00.0 - 1:35.0", workspace.VideoSummary.SelectedRange);
            Assert.Equal("1:35.0", workspace.VideoSummary.SelectedDuration);
            Assert.Equal(TimeSpan.Zero, startPreviewPosition);
            Assert.Equal(TimeSpan.FromSeconds(94.9), endPreviewPosition);
            Assert.Equal("crop=1920:800:0:140", workspace.VideoSummary.StrategyCrop);
            Assert.Equal("crop=1920:800:0:140,scale=-2:min(ih\\,1080)", workspace.VideoSummary.StrategyFilter);
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    [Fact]
    public async Task ChangingClipRange_RebuildsStrategyForTrimmedDuration()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(inputPath, "video");

        try
        {
            var analysisService = new RecordingAnalysisService();
            var previewService = new RecordingFramePreviewService();
            var workspace = new EncodeWorkspaceViewModel(
                analysisService,
                new NoOpEncodingService(),
                previewService,
                new StaticEncoderCapabilityService(),
                null,
                initializeEncoderSupport: false);

            Assert.True(workspace.FileInput.SetFile(inputPath));
            await analysisService.WaitForStrategyCountAsync(1);
            await previewService.WaitForRequestCountAsync(2);

            workspace.ClipRange.StartSeconds = 15;
            workspace.ClipRange.EndSeconds = 45;

            await analysisService.WaitForStrategyCountAsync(2);
            await previewService.WaitForRequestCountAsync(4);
            TimeSpan[] refreshedPreviewPositions = previewService.GetRequests()
                .Skip(2)
                .Select(request => request.Position)
                .ToArray();

            Assert.Equal(TimeSpan.FromSeconds(30), analysisService.LastRequestedClipRange?.Duration);
            Assert.Equal(TimeSpan.FromSeconds(15), analysisService.LastRequestedClipRange?.Start);
            Assert.Equal("0:15.0 - 0:45.0", workspace.VideoSummary.SelectedRange);
            Assert.Equal("0:30.0", workspace.VideoSummary.SelectedDuration);
            Assert.Contains(TimeSpan.FromSeconds(15), refreshedPreviewPositions);
            Assert.Contains(TimeSpan.FromSeconds(45), refreshedPreviewPositions);
            Assert.NotNull(workspace.VideoSummary.StrategyAnalysis);
            Assert.Equal("crop=1920:800:0:140", workspace.VideoSummary.StrategyAnalysis!.CropFilter);
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    [Fact]
    public async Task StartingEncode_ForwardsSelectedClipRange()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(inputPath, "video");

        try
        {
            var analysisService = new RecordingAnalysisService();
            var encodingService = new RecordingEncodingService();
            var workspace = new EncodeWorkspaceViewModel(
                analysisService,
                encodingService,
                new RecordingFramePreviewService(),
                new StaticEncoderCapabilityService(),
                null,
                initializeEncoderSupport: false);

            Assert.True(workspace.FileInput.SetFile(inputPath));
            await analysisService.WaitForStrategyCountAsync(1);

            workspace.ClipRange.StartSeconds = 12;
            workspace.ClipRange.EndSeconds = 27;
            await analysisService.WaitForStrategyCountAsync(2);

            workspace.EncodeButtonCommand.Execute(null);
            EncodeRequest request = await encodingService.WaitForRequestAsync();

            Assert.Equal(TimeSpan.FromSeconds(12), request.ClipRange?.Start);
            Assert.Equal(TimeSpan.FromSeconds(27), request.ClipRange?.End);
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    [Fact]
    public async Task ChangingOutputSettings_PersistsThroughCoordinator()
    {
        string outputFolder = Path.Combine(Path.GetTempPath(), $"potatomaker-out-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputFolder);

        try
        {
            var settingsCoordinator = new RecordingSettingsCoordinator(new AppSettings
            {
                IsDarkMode = false,
                UseNvencEncoder = true,
                LastOutputFolder = null
            });
            var workspace = new EncodeWorkspaceViewModel(
                new RecordingAnalysisService(),
                new NoOpEncodingService(),
                new RecordingFramePreviewService(),
                new StaticEncoderCapabilityService(),
                settingsCoordinator,
                initializeEncoderSupport: false);

            workspace.OutputSettings.SetCustomOutputFolder(outputFolder);

            AppSettings persisted = await settingsCoordinator.WaitForUpdateAsync();

            Assert.Equal(outputFolder, persisted.LastOutputFolder);
        }
        finally
        {
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    private sealed class RecordingAnalysisService : IVideoAnalysisService
    {
        private readonly List<StrategyAnalysis> _strategies = [];

        public Task<VideoInfo> ProbeAsync(string inputPath, CancellationToken ct = default) =>
            Task.FromResult(new VideoInfo(TimeSpan.FromSeconds(95), 1920, 1080, 59.94));

        public VideoClipRange? LastRequestedClipRange { get; private set; }

        public Task<StrategyAnalysis> AnalyzeStrategyAsync(
            string inputPath,
            VideoInfo info,
            EncodeSettings settings,
            VideoClipRange? clipRange = null,
            CancellationToken ct = default)
        {
            LastRequestedClipRange = clipRange;
            var strategy = new StrategyAnalysis(
                Path.GetFullPath(inputPath),
                "crop=1920:800:0:140",
                new EncodePlanner.EncodePlan(1800, 1, "scale=-2:min(ih\\,1080)", "1080p (original)"));

            lock (_strategies)
            {
                _strategies.Add(strategy);
            }

            return Task.FromResult(strategy);
        }

        public async Task<StrategyAnalysis> WaitForStrategyCountAsync(int expectedCount)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                lock (_strategies)
                {
                    if (_strategies.Count >= expectedCount)
                        return _strategies[expectedCount - 1];
                }

                await Task.Delay(10);
            }

            throw new TimeoutException($"Timed out waiting for {expectedCount} strategy calls.");
        }
    }

    private sealed class NoOpEncodingService : IVideoEncodingService
    {
        public Task RunAsync(
            EncodeRequest request,
            Microsoft.Extensions.Logging.ILogger<ProcessingPipeline> logger,
            IProgress<EncodeProgress>? progress = null,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingFramePreviewService : IVideoFramePreviewService
    {
        private readonly List<(string Path, TimeSpan Position)> _requests = [];

        public Task<VideoFramePreviewResult> GenerateAsync(
            string inputPath,
            TimeSpan position,
            CancellationToken ct = default)
        {
            lock (_requests)
            {
                _requests.Add((inputPath, position));
            }

            return Task.FromResult(new VideoFramePreviewResult(null, "Preview stub"));
        }

        public async Task<(string Path, TimeSpan Position)> WaitForRequestCountAsync(int expectedCount)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                lock (_requests)
                {
                    if (_requests.Count >= expectedCount)
                        return _requests[expectedCount - 1];
                }

                await Task.Delay(10);
            }

            throw new TimeoutException($"Timed out waiting for {expectedCount} preview requests.");
        }

        public IReadOnlyList<(string Path, TimeSpan Position)> GetRequests()
        {
            lock (_requests)
            {
                return _requests.ToArray();
            }
        }
    }

    private sealed class RecordingEncodingService : IVideoEncodingService
    {
        private readonly TaskCompletionSource<EncodeRequest> _requestTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RunAsync(
            EncodeRequest request,
            Microsoft.Extensions.Logging.ILogger<ProcessingPipeline> logger,
            IProgress<EncodeProgress>? progress = null,
            CancellationToken ct = default)
        {
            _requestTcs.TrySetResult(request);
            return Task.CompletedTask;
        }

        public Task<EncodeRequest> WaitForRequestAsync() => _requestTcs.Task;
    }

    private sealed class StaticEncoderCapabilityService : IEncoderCapabilityService
    {
        public Task<bool> IsAv1NvencSupportedAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class RecordingSettingsCoordinator : IAppSettingsCoordinator
    {
        private readonly TaskCompletionSource<AppSettings> _updateTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RecordingSettingsCoordinator(AppSettings initialSettings)
        {
            Current = initialSettings;
        }

        public AppSettings Current { get; private set; }

        public Task UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken ct = default)
        {
            Current = update(Current);
            _updateTcs.TrySetResult(Current);
            return Task.CompletedTask;
        }

        public Task<AppSettings> WaitForUpdateAsync() => _updateTcs.Task;
    }
}
