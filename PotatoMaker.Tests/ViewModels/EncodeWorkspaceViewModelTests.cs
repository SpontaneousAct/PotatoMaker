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
            var workspace = new EncodeWorkspaceViewModel(
                analysisService,
                new NoOpEncodingService(),
                new StaticEncoderCapabilityService(),
                null,
                initializeEncoderSupport: false);

            bool accepted = workspace.FileInput.SetFile(inputPath);
            Assert.True(accepted);

            await analysisService.WaitForStrategyAsync();

            Assert.True(workspace.VideoSummary.HasData);
            Assert.True(workspace.VideoSummary.HasStrategy);
            Assert.Equal("1920x1080", workspace.VideoSummary.Resolution);
            Assert.Equal("crop=1920:800:0:140", workspace.VideoSummary.StrategyCrop);
            Assert.Equal("crop=1920:800:0:140,scale=-2:min(ih\\,1080)", workspace.VideoSummary.StrategyFilter);
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
        private readonly TaskCompletionSource<StrategyAnalysis> _strategyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<VideoInfo> ProbeAsync(string inputPath, CancellationToken ct = default) =>
            Task.FromResult(new VideoInfo(TimeSpan.FromSeconds(95), 1920, 1080, 59.94));

        public Task<StrategyAnalysis> AnalyzeStrategyAsync(
            string inputPath,
            VideoInfo info,
            EncodeSettings settings,
            CancellationToken ct = default)
        {
            var strategy = new StrategyAnalysis(
                Path.GetFullPath(inputPath),
                "crop=1920:800:0:140",
                new EncodePlanner.EncodePlan(1800, 1, "scale=-2:min(ih\\,1080)", "1080p (original)"));

            _strategyTcs.TrySetResult(strategy);
            return Task.FromResult(strategy);
        }

        public Task<StrategyAnalysis> WaitForStrategyAsync() => _strategyTcs.Task;
    }

    private sealed class NoOpEncodingService : IVideoEncodingService
    {
        public Task RunAsync(
            EncodeRequest request,
            Microsoft.Extensions.Logging.ILogger<ProcessingPipeline> logger,
            IProgress<EncodeProgress>? progress = null,
            CancellationToken ct = default) => Task.CompletedTask;
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
