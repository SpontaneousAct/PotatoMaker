using Xunit;
using PotatoMaker.GUI.Services;
using PotatoMaker.GUI.ViewModels;
using Avalonia.Input;

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
                UseNvencEncoder = true,
                PreviewVolumePercent = 100,
                SvtAv1Preset = 6
            });
        var workspace = new EncodeWorkspaceViewModel(
            new NoOpAnalysisService(),
            new NoOpEncodingService(),
            new StaticEncoderCapabilityService(),
            settingsCoordinator,
            initializeEncoderSupport: false);

        var viewModel = new MainWindowViewModel(
            workspace,
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

    [Fact]
    public async Task SpaceShortcut_IsIgnoredWhenPlaybackCommandIsUnavailable()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mp4");
        File.WriteAllText(inputPath, "video");

        try
        {
            var analysisService = new RecordingAnalysisService();
            var player = new VideoPlayerViewModel(initializePlayer: false);
            var workspace = new EncodeWorkspaceViewModel(
                analysisService,
                new NoOpEncodingService(),
                player,
                new StaticEncoderCapabilityService(),
                null,
                initializeEncoderSupport: false);
            var viewModel = new MainWindowViewModel(
                workspace,
                new RecordingThemeService(),
                null);

            Assert.True(workspace.FileInput.SetFile(inputPath));
            await analysisService.WaitForStrategyCountAsync(1);

            bool handled = viewModel.TryHandleGlobalShortcut(Key.Space, KeyModifiers.None);

            Assert.False(handled);
            Assert.Equal("Play", workspace.VideoPlayer.TogglePlaybackText);
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    [Fact]
    public async Task TrimShortcuts_SetStartAndEndAtCurrentPosition()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mp4");
        File.WriteAllText(inputPath, "video");

        try
        {
            var analysisService = new RecordingAnalysisService();
            var player = new VideoPlayerViewModel(initializePlayer: false);
            var workspace = new EncodeWorkspaceViewModel(
                analysisService,
                new NoOpEncodingService(),
                player,
                new StaticEncoderCapabilityService(),
                null,
                initializeEncoderSupport: false);
            var viewModel = new MainWindowViewModel(
                workspace,
                new RecordingThemeService(),
                null);

            Assert.True(workspace.FileInput.SetFile(inputPath));
            await analysisService.WaitForStrategyCountAsync(1);

            player.TimelineSeconds = 14;
            Assert.True(viewModel.TryHandleGlobalShortcut(Key.A, KeyModifiers.None));
            await analysisService.WaitForStrategyCountAsync(2);

            player.TimelineSeconds = 39;
            Assert.True(viewModel.TryHandleGlobalShortcut(Key.D, KeyModifiers.None));
            await analysisService.WaitForStrategyCountAsync(3);

            Assert.Equal(TimeSpan.FromSeconds(14), workspace.ClipRange.Start);
            Assert.Equal(TimeSpan.FromSeconds(39), workspace.ClipRange.End);
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    [Fact]
    public void Shortcuts_WithModifiers_AreIgnored()
    {
        var viewModel = new MainWindowViewModel(
            new EncodeWorkspaceViewModel(
                new NoOpAnalysisService(),
                new NoOpEncodingService(),
                new StaticEncoderCapabilityService(),
                null,
                initializeEncoderSupport: false),
            new RecordingThemeService(),
            null);

        Assert.False(viewModel.TryHandleGlobalShortcut(Key.Space, KeyModifiers.Control));
    }

    [Fact]
    public void SwitchingAwayFromMain_DoesNotManipulateVideoSurface()
    {
        var workspace = new EncodeWorkspaceViewModel(
            new NoOpAnalysisService(),
            new NoOpEncodingService(),
            new StaticEncoderCapabilityService(),
            null,
            initializeEncoderSupport: false);
        var viewModel = new MainWindowViewModel(
            workspace,
            new RecordingThemeService(),
            null);

        Assert.False(workspace.VideoPlayer.SuppressVideoSurface);

        viewModel.ShowSettingsViewCommand.Execute(null);
        Assert.False(workspace.VideoPlayer.SuppressVideoSurface);

        viewModel.ShowMainViewCommand.Execute(null);
        Assert.False(workspace.VideoPlayer.SuppressVideoSurface);
        Assert.True(viewModel.IsMainViewSelected);

        viewModel.Dispose();
    }

    [Fact]
    public void GlobalShortcuts_AreIgnoredOutsideMainView()
    {
        var workspace = new EncodeWorkspaceViewModel(
            new NoOpAnalysisService(),
            new NoOpEncodingService(),
            new StaticEncoderCapabilityService(),
            null,
            initializeEncoderSupport: false);
        var viewModel = new MainWindowViewModel(
            workspace,
            new RecordingThemeService(),
            null);

        viewModel.ShowHelpViewCommand.Execute(null);

        Assert.False(viewModel.TryHandleGlobalShortcut(Key.Space, KeyModifiers.None));
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

    private sealed class RecordingAnalysisService : IVideoAnalysisService
    {
        private readonly List<PotatoMaker.Core.StrategyAnalysis> _strategies = [];

        public Task<PotatoMaker.Core.VideoInfo> ProbeAsync(string inputPath, CancellationToken ct = default) =>
            Task.FromResult(new PotatoMaker.Core.VideoInfo(TimeSpan.FromSeconds(95), 1920, 1080, 59.94));

        public Task<PotatoMaker.Core.StrategyAnalysis> AnalyzeStrategyAsync(
            string inputPath,
            PotatoMaker.Core.VideoInfo info,
            PotatoMaker.Core.EncodeSettings settings,
            PotatoMaker.Core.VideoClipRange? clipRange = null,
            CancellationToken ct = default)
        {
            var strategy = new PotatoMaker.Core.StrategyAnalysis(
                Path.GetFullPath(inputPath),
                "crop=1920:800:0:140",
                new PotatoMaker.Core.EncodePlanner.EncodePlan(1800, 1, "scale=-2:min(ih\\,1080)", "1080p (original)"));

            lock (_strategies)
            {
                _strategies.Add(strategy);
            }

            return Task.FromResult(strategy);
        }

        public async Task<PotatoMaker.Core.StrategyAnalysis> WaitForStrategyCountAsync(int expectedCount)
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
            Microsoft.Extensions.Logging.ILogger<PotatoMaker.Core.ProcessingPipeline> logger,
            IProgress<PotatoMaker.Core.EncodeProgress>? progress = null,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StaticEncoderCapabilityService : IEncoderCapabilityService
    {
        public Task<bool> IsAv1NvencSupportedAsync(CancellationToken ct = default) => Task.FromResult(true);
    }
}
