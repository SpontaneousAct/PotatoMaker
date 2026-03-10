using Xunit;
using PotatoMaker.GUI.Services;
using PotatoMaker.GUI.ViewModels;

namespace PotatoMaker.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task ChangingTheme_UsesThemeServiceAndPersistsPreference()
    {
        var themeService = new RecordingThemeService();
        var settingsCoordinator = new RecordingSettingsCoordinator(
            new AppSettings
            {
                IsDarkMode = true,
                UseNvencEncoder = true
            });
        var workspace = new EncodeWorkspaceViewModel(
            new NoOpAnalysisService(),
            new NoOpEncodingService(),
            new StaticEncoderCapabilityService(),
            settingsCoordinator,
            initializeEncoderSupport: false);

        var viewModel = new MainWindowViewModel(
            workspace,
            new HelpModalViewModel(),
            themeService,
            settingsCoordinator);

        Assert.True(viewModel.IsDarkMode);
        viewModel.IsDarkMode = false;

        AppSettings persisted = await settingsCoordinator.WaitForUpdateAsync();

        Assert.False(themeService.AppliedThemes[^1]);
        Assert.False(persisted.IsDarkMode);
    }

    [Fact]
    public void LoadingStartupFiles_UsesFirstSupportedVideoPath()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mp4");
        File.WriteAllText(inputPath, "video");

        try
        {
            var workspace = new EncodeWorkspaceViewModel(
                new NoOpAnalysisService(),
                new NoOpEncodingService(),
                new StaticEncoderCapabilityService(),
                null,
                initializeEncoderSupport: false);

            var viewModel = new MainWindowViewModel(
                workspace,
                new HelpModalViewModel(),
                new RecordingThemeService(),
                null);

            bool loaded = viewModel.TryLoadStartupFiles(["", "--flag", inputPath]);

            Assert.True(loaded);
            Assert.Equal(Path.GetFullPath(inputPath), workspace.FileInput.InputFilePath);
            Assert.Equal(Path.GetFileName(inputPath), workspace.FileInput.FileName);
            Assert.Null(workspace.FileInput.ValidationMessage);
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    private sealed class RecordingThemeService : IThemeService
    {
        public List<bool> AppliedThemes { get; } = [];

        public bool IsDarkModeEnabled() => false;

        public void ApplyTheme(bool isDarkMode)
        {
            AppliedThemes.Add(isDarkMode);
        }
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

    private sealed class NoOpAnalysisService : IVideoAnalysisService
    {
        public Task<PotatoMaker.Core.VideoInfo> ProbeAsync(string inputPath, CancellationToken ct = default) =>
            Task.FromResult(new PotatoMaker.Core.VideoInfo(TimeSpan.Zero, 0, 0, 0));

        public Task<PotatoMaker.Core.StrategyAnalysis> AnalyzeStrategyAsync(
            string inputPath,
            PotatoMaker.Core.VideoInfo info,
            PotatoMaker.Core.EncodeSettings settings,
            PotatoMaker.Core.VideoClipRange? clipRange = null,
            CancellationToken ct = default) =>
            Task.FromResult(new PotatoMaker.Core.StrategyAnalysis(
                inputPath,
                null,
                new PotatoMaker.Core.EncodePlanner.EncodePlan(1000, 1, null, "original")));
    }

    private sealed class NoOpEncodingService : IVideoEncodingService
    {
        public Task RunAsync(
            EncodeRequest request,
            Microsoft.Extensions.Logging.ILogger<PotatoMaker.Core.ProcessingPipeline> logger,
            IProgress<PotatoMaker.Core.EncodeProgress>? progress = null,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StaticEncoderCapabilityService : IEncoderCapabilityService
    {
        public Task<bool> IsAv1NvencSupportedAsync(CancellationToken ct = default) => Task.FromResult(true);
    }
}
