using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using PotatoMaker.GUI.Services;
using PotatoMaker.GUI.ViewModels;
using Xunit;

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
            settingsCoordinator,
            null);

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
                null,
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
    public void OpeningExternalFiles_LoadsVideoAndReturnsToMainView()
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
                null,
                null);

            viewModel.ShowSettingsViewCommand.Execute(null);

            bool loaded = viewModel.OpenExternalFiles(["--flag", inputPath]);

            Assert.True(loaded);
            Assert.True(viewModel.IsMainViewSelected);
            Assert.Equal(Path.GetFullPath(inputPath), workspace.FileInput.InputFilePath);
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    [Fact]
    public void OpeningExternalFiles_WithoutArguments_DoesNotChangeCurrentView()
    {
        var viewModel = new MainWindowViewModel(
            new EncodeWorkspaceViewModel(
                new NoOpAnalysisService(),
                new NoOpEncodingService(),
                new StaticEncoderCapabilityService(),
                null,
                initializeEncoderSupport: false),
            new RecordingThemeService(),
            null,
            null);

        viewModel.ShowHelpViewCommand.Execute(null);

        bool loaded = viewModel.OpenExternalFiles([]);

        Assert.False(loaded);
        Assert.True(viewModel.IsHelpViewSelected);
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
                null,
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
                null,
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
            null,
            null);

        Assert.False(viewModel.TryHandleGlobalShortcut(Key.Space, KeyModifiers.Control));
        Assert.False(viewModel.TryHandleGlobalShortcut(Key.A, KeyModifiers.Control));
        Assert.False(viewModel.TryHandleGlobalShortcut(Key.D, KeyModifiers.Shift));
    }

    [Fact]
    public void VersionText_UsesSemanticVersionDisplay()
    {
        var viewModel = new MainWindowViewModel(
            new EncodeWorkspaceViewModel(
                new NoOpAnalysisService(),
                new NoOpEncodingService(),
                new StaticEncoderCapabilityService(),
                null,
                initializeEncoderSupport: false),
            new RecordingThemeService(),
            null,
            null,
            new StubAppVersionService("2.3.4-beta.5"));

        Assert.Equal("v2.3.4-beta.5", viewModel.VersionText);
    }

    [Fact]
    public void GlobalShortcutMap_IncludesPlaybackAndTrimKeys()
    {
        Assert.True(MainWindowViewModel.IsGlobalShortcut(Key.Space, KeyModifiers.None));
        Assert.True(MainWindowViewModel.IsGlobalShortcut(Key.A, KeyModifiers.None));
        Assert.True(MainWindowViewModel.IsGlobalShortcut(Key.D, KeyModifiers.None));
        Assert.False(MainWindowViewModel.IsGlobalShortcut(Key.W, KeyModifiers.None));
        Assert.False(MainWindowViewModel.IsGlobalShortcut(Key.S, KeyModifiers.None));
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
            null,
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
            null,
            null);

        viewModel.ShowHelpViewCommand.Execute(null);

        Assert.False(viewModel.TryHandleGlobalShortcut(Key.Space, KeyModifiers.None));
    }

    [Fact]
    public async Task InitializeAsync_ShowsUpdateIndicator_WhenUpdateIsAvailable()
    {
        var updateService = new StubAppUpdateService(
            currentSnapshot: new AppUpdateSnapshot(
                IsConfigured: true,
                CanSelfUpdate: true,
                IsUpdateAvailable: false,
                IsUpdatePendingRestart: false),
            checkedSnapshot: new AppUpdateSnapshot(
                IsConfigured: true,
                CanSelfUpdate: true,
                IsUpdateAvailable: true,
                IsUpdatePendingRestart: false,
                AvailableVersion: "9.9.9"));

        var viewModel = new MainWindowViewModel(
            new EncodeWorkspaceViewModel(
                new NoOpAnalysisService(),
                new NoOpEncodingService(),
                new StaticEncoderCapabilityService(),
                null,
                initializeEncoderSupport: false),
            new RecordingThemeService(),
            null,
            updateService);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsUpdateBadgeVisible);
        Assert.True(viewModel.Settings.IsUpdateSectionVisible);
        Assert.Equal("Update available: v9.9.9", viewModel.Settings.UpdateTitle);
        Assert.Equal("Install update", viewModel.Settings.UpdateActionText);
    }

    [Fact]
    public async Task ApplyUpdateCommand_UsesUpdateService_WhenUpdateIsAvailable()
    {
        var updateService = new StubAppUpdateService(
            currentSnapshot: new AppUpdateSnapshot(
                IsConfigured: true,
                CanSelfUpdate: true,
                IsUpdateAvailable: true,
                IsUpdatePendingRestart: false,
                AvailableVersion: "2.0.0"));

        var viewModel = new MainWindowViewModel(
            new EncodeWorkspaceViewModel(
                new NoOpAnalysisService(),
                new NoOpEncodingService(),
                new StaticEncoderCapabilityService(),
                null,
                initializeEncoderSupport: false),
            new RecordingThemeService(),
            null,
            updateService);

        await ((IAsyncRelayCommand)viewModel.ApplyUpdateCommand).ExecuteAsync(null);

        Assert.Equal(1, updateService.ApplyCallCount);
        Assert.True(viewModel.Settings.IsUpdateSectionVisible);
        Assert.Equal("Restart to update", viewModel.Settings.UpdateActionText);
        Assert.Contains("finished downloading", viewModel.Settings.UpdateDescription, StringComparison.OrdinalIgnoreCase);
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

        public Task<string?> DetectCropAsync(
            string inputPath,
            PotatoMaker.Core.VideoInfo info,
            CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task<PotatoMaker.Core.StrategyAnalysis> AnalyzeStrategyAsync(
            string inputPath,
            PotatoMaker.Core.VideoInfo info,
            PotatoMaker.Core.EncodeSettings settings,
            string? cropFilter = null,
            PotatoMaker.Core.VideoClipRange? clipRange = null,
            CancellationToken ct = default) =>
            Task.FromResult(new PotatoMaker.Core.StrategyAnalysis(
                inputPath,
                cropFilter,
                null,
                0,
                new PotatoMaker.Core.EncodePlanner.EncodePlan(1000, 1, null, "original")));
    }

    private sealed class RecordingAnalysisService : IVideoAnalysisService
    {
        private readonly List<PotatoMaker.Core.StrategyAnalysis> _strategies = [];

        public Task<PotatoMaker.Core.VideoInfo> ProbeAsync(string inputPath, CancellationToken ct = default) =>
            Task.FromResult(new PotatoMaker.Core.VideoInfo(TimeSpan.FromSeconds(95), 1920, 1080, 59.94));

        public Task<string?> DetectCropAsync(
            string inputPath,
            PotatoMaker.Core.VideoInfo info,
            CancellationToken ct = default) =>
            Task.FromResult<string?>("crop=1920:800:0:140");

        public Task<PotatoMaker.Core.StrategyAnalysis> AnalyzeStrategyAsync(
            string inputPath,
            PotatoMaker.Core.VideoInfo info,
            PotatoMaker.Core.EncodeSettings settings,
            string? cropFilter = null,
            PotatoMaker.Core.VideoClipRange? clipRange = null,
            CancellationToken ct = default)
        {
            var strategy = new PotatoMaker.Core.StrategyAnalysis(
                Path.GetFullPath(inputPath),
                cropFilter,
                PotatoMaker.Core.EncodePlanner.BuildFrameRateFilter(info.FrameRate, settings),
                PotatoMaker.Core.EncodePlanner.ResolveOutputFrameRate(info.FrameRate, settings),
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

    private sealed class StubAppVersionService(string semanticVersion) : IAppVersionService
    {
        public string SemanticVersion { get; } = semanticVersion;

        public string InformationalVersion { get; } = semanticVersion;

        public string DisplayVersion { get; } = $"v{semanticVersion}";
    }

    private sealed class StubAppUpdateService(
        AppUpdateSnapshot currentSnapshot,
        AppUpdateSnapshot? checkedSnapshot = null) : IAppUpdateService
    {
        public int ApplyCallCount { get; private set; }

        public bool ShouldCheckOnStartup => true;

        public TimeSpan StartupCheckDelay => TimeSpan.Zero;

        public AppUpdateSnapshot CurrentSnapshot { get; private set; } = currentSnapshot;

        public AppUpdateSnapshot CheckedSnapshot { get; } = checkedSnapshot ?? currentSnapshot;

        public Task<AppUpdateSnapshot> GetCurrentStateAsync(CancellationToken ct = default) =>
            Task.FromResult(CurrentSnapshot);

        public Task<AppUpdateSnapshot> CheckForUpdatesAsync(CancellationToken ct = default)
        {
            CurrentSnapshot = CheckedSnapshot;
            return Task.FromResult(CurrentSnapshot);
        }

        public Task ApplyUpdateAsync(Action<int>? progress = null, CancellationToken ct = default)
        {
            ApplyCallCount++;
            progress?.Invoke(100);
            CurrentSnapshot = CurrentSnapshot with
            {
                IsUpdateAvailable = false,
                IsUpdatePendingRestart = true
            };

            return Task.CompletedTask;
        }
    }
}
