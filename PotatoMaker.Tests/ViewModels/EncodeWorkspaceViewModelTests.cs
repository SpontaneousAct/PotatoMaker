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

            await analysisService.WaitForStrategyCountAsync(1);

            Assert.True(workspace.VideoSummary.HasData);
            Assert.True(workspace.VideoSummary.HasStrategy);
            Assert.Equal("1920x1080", workspace.VideoSummary.Resolution);
            Assert.Equal("0:00.0 - 1:35.0", workspace.VideoSummary.SelectedRange);
            Assert.Equal("1:35.0", workspace.VideoSummary.SelectedDuration);
            Assert.Equal("59.94 fps", workspace.VideoSummary.StrategyOutputFrameRate);
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
            var workspace = new EncodeWorkspaceViewModel(
                analysisService,
                new NoOpEncodingService(),
                new StaticEncoderCapabilityService(),
                null,
                initializeEncoderSupport: false);

            Assert.True(workspace.FileInput.SetFile(inputPath));
            await analysisService.WaitForStrategyCountAsync(1);

            workspace.ClipRange.StartSeconds = 15;
            workspace.ClipRange.EndSeconds = 45;

            await analysisService.WaitForStrategyCountAsync(2);

            Assert.Equal(TimeSpan.FromSeconds(30), analysisService.LastRequestedClipRange?.Duration);
            Assert.Equal(TimeSpan.FromSeconds(15), analysisService.LastRequestedClipRange?.Start);
            Assert.Equal("0:15.0 - 0:45.0", workspace.VideoSummary.SelectedRange);
            Assert.Equal("0:30.0", workspace.VideoSummary.SelectedDuration);
            Assert.NotNull(workspace.VideoSummary.StrategyAnalysis);
            Assert.Equal("crop=1920:800:0:140", workspace.VideoSummary.StrategyAnalysis!.CropFilter);
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    [Fact]
    public async Task ChangingClipRange_UpdatesSummaryImmediatelyAndCoalescesStrategyRefresh()
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

            Assert.True(workspace.FileInput.SetFile(inputPath));
            await analysisService.WaitForStrategyCountAsync(1);

            workspace.ClipRange.StartSeconds = 5;
            workspace.ClipRange.EndSeconds = 80;
            workspace.ClipRange.StartSeconds = 15;
            workspace.ClipRange.EndSeconds = 45;

            Assert.Equal("0:15.0 - 0:45.0", workspace.VideoSummary.SelectedRange);
            Assert.Equal("0:30.0", workspace.VideoSummary.SelectedDuration);
            await analysisService.WaitForStrategyCountAsync(5);

            Assert.Equal(5, analysisService.StrategyCount);
            Assert.Equal(TimeSpan.FromSeconds(15), analysisService.LastRequestedClipRange?.Start);
            Assert.Equal(TimeSpan.FromSeconds(45), analysisService.LastRequestedClipRange?.End);
            Assert.Equal(1, analysisService.DetectCropCallCount);
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    [Fact]
    public async Task ChangingClipRange_DuringCropDetection_KeepsPendingStateUntilCropCompletes()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(inputPath, "video");

        try
        {
            var analysisService = new DelayedCropAnalysisService();
            var workspace = new EncodeWorkspaceViewModel(
                analysisService,
                new NoOpEncodingService(),
                new StaticEncoderCapabilityService(),
                null,
                initializeEncoderSupport: false);

            Assert.True(workspace.FileInput.SetFile(inputPath));
            await analysisService.WaitForCropDetectionRequestAsync();

            workspace.ClipRange.StartSeconds = 15;
            workspace.ClipRange.EndSeconds = 45;

            Assert.Equal("0:15.0 - 0:45.0", workspace.VideoSummary.SelectedRange);
            Assert.Equal("0:30.0", workspace.VideoSummary.SelectedDuration);
            Assert.False(workspace.VideoSummary.HasStrategy);
            Assert.Equal("Analyzing crop and strategy...", workspace.VideoSummary.StrategyStatus);
            Assert.Null(workspace.VideoSummary.StrategyCrop);

            analysisService.CompleteCropDetection("crop=1920:800:0:140");
            await analysisService.WaitForStrategyCountAsync(1);

            Assert.True(workspace.VideoSummary.HasStrategy);
            Assert.Equal("crop=1920:800:0:140", workspace.VideoSummary.StrategyCrop);
            Assert.Equal(TimeSpan.FromSeconds(15), analysisService.LastRequestedClipRange?.Start);
            Assert.Equal(TimeSpan.FromSeconds(45), analysisService.LastRequestedClipRange?.End);
            Assert.Equal(1, analysisService.DetectCropCallCount);
            Assert.Equal(1, analysisService.StrategyCount);
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
                new StaticEncoderCapabilityService(),
                null,
                initializeEncoderSupport: false);

            Assert.True(workspace.FileInput.SetFile(inputPath));
            await analysisService.WaitForStrategyCountAsync(1);

            workspace.ClipRange.StartSeconds = 12;
            workspace.ClipRange.EndSeconds = 27;
            await analysisService.WaitForStrategyCountAsync(2);

            workspace.OutputSettings.UseNvencEncoder = false;
            workspace.OutputSettings.SetCpuEncodePreset(10);
            workspace.OutputSettings.SetFrameRateMode(EncodeFrameRateMode.Fps30);
            workspace.OutputSettings.OutputNamePrefix = "share_";
            workspace.OutputSettings.OutputNameSuffix = "_mobile";

            workspace.EncodeButtonCommand.Execute(null);
            EncodeRequest request = await encodingService.WaitForRequestAsync();

            Assert.Equal(TimeSpan.FromSeconds(12), request.ClipRange?.Start);
            Assert.Equal(TimeSpan.FromSeconds(27), request.ClipRange?.End);
            Assert.Equal(EncoderChoice.SvtAv1, request.Settings.Encoder);
            Assert.Equal("share_", request.Settings.OutputNamePrefix);
            Assert.Equal("_mobile", request.Settings.OutputNameSuffix);
            Assert.Equal(EncodeFrameRateMode.Fps30, request.Settings.FrameRateMode);
            Assert.Equal(10, request.Settings.SvtAv1Preset);
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    [Fact]
    public async Task TrimCommands_UseCurrentPlaybackPosition()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(inputPath, "video");

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

            Assert.True(workspace.FileInput.SetFile(inputPath));
            await analysisService.WaitForStrategyCountAsync(1);

            player.TimelineSeconds = 18;
            player.SetTrimStartCommand.Execute(null);
            await analysisService.WaitForStrategyCountAsync(2);

            player.TimelineSeconds = 41;
            player.SetTrimEndCommand.Execute(null);
            await analysisService.WaitForStrategyCountAsync(3);

            Assert.Equal(TimeSpan.FromSeconds(18), workspace.ClipRange.Start);
            Assert.Equal(TimeSpan.FromSeconds(41), workspace.ClipRange.End);
            Assert.Equal("0:18.0 - 0:41.0", workspace.VideoSummary.SelectedRange);
            Assert.Equal("0:23.0", workspace.VideoSummary.SelectedDuration);
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    [Fact]
    public async Task PreviewTrimBoundary_UpdatesPlaybackPositionWhileAdjustingSelection()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(inputPath, "video");

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

            Assert.True(workspace.FileInput.SetFile(inputPath));
            await analysisService.WaitForStrategyCountAsync(1);

            workspace.BeginTrimBoundaryPreview();
            workspace.PreviewTrimBoundary(ClipBoundary.Start, TimeSpan.FromSeconds(22));
            await analysisService.WaitForStrategyCountAsync(2);

            Assert.Equal(TimeSpan.FromSeconds(22), workspace.ClipRange.Start);
            Assert.Equal(22, workspace.VideoPlayer.TimelineSeconds);

            workspace.PreviewTrimBoundary(ClipBoundary.End, TimeSpan.FromSeconds(48));
            await analysisService.WaitForStrategyCountAsync(3);

            Assert.Equal(TimeSpan.FromSeconds(48), workspace.ClipRange.End);
            Assert.Equal(48, workspace.VideoPlayer.TimelineSeconds);

            workspace.EndTrimBoundaryPreview();
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    [Fact]
    public async Task PreviewTrimBoundary_ClampsToMinimumDurationAtStart()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(inputPath, "video");

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

            Assert.True(workspace.FileInput.SetFile(inputPath));
            await analysisService.WaitForStrategyCountAsync(1);

            workspace.ClipRange.StartSeconds = 0;
            workspace.ClipRange.EndSeconds = 5;
            await analysisService.WaitForStrategyCountAsync(2);

            workspace.BeginTrimBoundaryPreview();
            workspace.PreviewTrimBoundary(ClipBoundary.End, TimeSpan.Zero);
            await analysisService.WaitForStrategyCountAsync(3);

            Assert.Equal(TimeSpan.Zero, workspace.ClipRange.Start);
            Assert.Equal(TimeSpan.FromSeconds(0.1), workspace.ClipRange.End);
            Assert.Equal(0.1, workspace.VideoPlayer.TimelineSeconds, precision: 3);

            workspace.EndTrimBoundaryPreview();
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
                OutputNamePrefix = "",
                OutputNameSuffix = "_discord",
                FrameRateMode = EncodeFrameRateMode.Original,
                PreviewVolumePercent = 100,
                SvtAv1Preset = 6,
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

    [Fact]
    public async Task ChangingOutputNameSettings_PersistsThroughCoordinator()
    {
        var settingsCoordinator = new RecordingSettingsCoordinator(new AppSettings
        {
            IsDarkMode = false,
            UseNvencEncoder = true,
            OutputNamePrefix = "",
            OutputNameSuffix = "_discord",
            FrameRateMode = EncodeFrameRateMode.Original,
            PreviewVolumePercent = 100,
            SvtAv1Preset = 6
        });
        var workspace = new EncodeWorkspaceViewModel(
            new RecordingAnalysisService(),
            new NoOpEncodingService(),
            new StaticEncoderCapabilityService(),
            settingsCoordinator,
            initializeEncoderSupport: false);

        workspace.OutputSettings.OutputNamePrefix = "share_";

        AppSettings persisted = await settingsCoordinator.WaitForUpdateAsync();

        Assert.Equal("share_", persisted.OutputNamePrefix);
        Assert.Equal("_discord", persisted.OutputNameSuffix);
    }

    [Fact]
    public void InitialSettings_ApplyVolumeAndCpuPreset()
    {
        const double initialVolume = 37;
        const int initialPreset = 10;
        const EncodeFrameRateMode initialFrameRateMode = EncodeFrameRateMode.Fps30;
        var player = new VideoPlayerViewModel(initializePlayer: false);
        var workspace = new EncodeWorkspaceViewModel(
            new RecordingAnalysisService(),
            new NoOpEncodingService(),
            player,
            new StaticEncoderCapabilityService(),
            new RecordingSettingsCoordinator(new AppSettings
            {
                IsDarkMode = false,
                UseNvencEncoder = false,
                OutputNamePrefix = "clip_",
                OutputNameSuffix = "_mobile",
                FrameRateMode = initialFrameRateMode,
                PreviewVolumePercent = initialVolume,
                SvtAv1Preset = initialPreset,
                LastOutputFolder = "C:\\encoded"
            }),
            initializeEncoderSupport: false);

        Assert.Equal(initialVolume, workspace.VideoPlayer.VolumePercent);
        Assert.Equal(initialPreset, workspace.OutputSettings.CpuEncodePreset);
        Assert.False(workspace.OutputSettings.UseNvencEncoder);
        Assert.Equal("clip_", workspace.OutputSettings.OutputNamePrefix);
        Assert.Equal("_mobile", workspace.OutputSettings.OutputNameSuffix);
        Assert.Equal(initialFrameRateMode, workspace.OutputSettings.FrameRateMode);
        Assert.Equal("C:\\encoded", workspace.OutputSettings.CustomOutputFolder);
    }

    [Fact]
    public async Task ChangingVolume_PersistsThroughCoordinator()
    {
        var settingsCoordinator = new RecordingSettingsCoordinator(new AppSettings
        {
            IsDarkMode = false,
            UseNvencEncoder = true,
            OutputNamePrefix = "",
            OutputNameSuffix = "_discord",
            FrameRateMode = EncodeFrameRateMode.Original,
            PreviewVolumePercent = 100,
            SvtAv1Preset = 6
        });
        var workspace = new EncodeWorkspaceViewModel(
            new RecordingAnalysisService(),
            new NoOpEncodingService(),
            new VideoPlayerViewModel(initializePlayer: false),
            new StaticEncoderCapabilityService(),
            settingsCoordinator,
            initializeEncoderSupport: false);

        workspace.VideoPlayer.VolumePercent = 44;

        AppSettings persisted = await settingsCoordinator.WaitForUpdateAsync();

        Assert.Equal(44, persisted.PreviewVolumePercent);
    }

    [Fact]
    public async Task ChangingCpuPreset_PersistsThroughCoordinator()
    {
        var settingsCoordinator = new RecordingSettingsCoordinator(new AppSettings
        {
            IsDarkMode = false,
            UseNvencEncoder = true,
            OutputNamePrefix = "",
            OutputNameSuffix = "_discord",
            FrameRateMode = EncodeFrameRateMode.Original,
            PreviewVolumePercent = 100,
            SvtAv1Preset = 6
        });
        var workspace = new EncodeWorkspaceViewModel(
            new RecordingAnalysisService(),
            new NoOpEncodingService(),
            new StaticEncoderCapabilityService(),
            settingsCoordinator,
            initializeEncoderSupport: false);

        workspace.OutputSettings.SetCpuEncodePreset(11);

        AppSettings persisted = await settingsCoordinator.WaitForUpdateAsync();

        Assert.Equal(10, persisted.SvtAv1Preset);
    }

    [Fact]
    public async Task ChangingFrameRateMode_RebuildsStrategyAndPersistsThroughCoordinator()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(inputPath, "video");

        try
        {
            var analysisService = new RecordingAnalysisService();
            var settingsCoordinator = new RecordingSettingsCoordinator(new AppSettings
            {
                IsDarkMode = false,
                UseNvencEncoder = true,
                OutputNamePrefix = "",
                OutputNameSuffix = "_discord",
                FrameRateMode = EncodeFrameRateMode.Original,
                PreviewVolumePercent = 100,
                SvtAv1Preset = 6
            });
            var workspace = new EncodeWorkspaceViewModel(
                analysisService,
                new NoOpEncodingService(),
                new StaticEncoderCapabilityService(),
                settingsCoordinator,
                initializeEncoderSupport: false);

            Assert.True(workspace.FileInput.SetFile(inputPath));
            await analysisService.WaitForStrategyCountAsync(1);

            workspace.OutputSettings.SetFrameRateMode(EncodeFrameRateMode.Fps30);

            await analysisService.WaitForStrategyCountAsync(2);
            AppSettings persisted = await settingsCoordinator.WaitForUpdateAsync();

            Assert.Equal(EncodeFrameRateMode.Fps30, analysisService.LastRequestedSettings?.FrameRateMode);
            Assert.Equal(EncodeFrameRateMode.Fps30, persisted.FrameRateMode);
            Assert.Equal("30 fps", workspace.VideoSummary.StrategyOutputFrameRate);
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    [Fact]
    public async Task DisposingDuringEncode_CancelsWithoutThrowing()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(inputPath, "video");

        try
        {
            var analysisService = new RecordingAnalysisService();
            var encodingService = new BlockingEncodingService();
            var workspace = new EncodeWorkspaceViewModel(
                analysisService,
                encodingService,
                new StaticEncoderCapabilityService(),
                null,
                initializeEncoderSupport: false);

            Assert.True(workspace.FileInput.SetFile(inputPath));
            await analysisService.WaitForStrategyCountAsync(1);

            Task encodeTask = workspace.StartEncodeCommand.ExecuteAsync(null);
            await encodingService.WaitForStartAsync();

            workspace.Dispose();

            await encodeTask;
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    [Fact]
    public async Task EncodingInProgress_LocksSourceSelectionUntilCompletion()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mp4");
        string replacementPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mov");
        await File.WriteAllTextAsync(inputPath, "video");
        await File.WriteAllTextAsync(replacementPath, "replacement");

        try
        {
            var analysisService = new RecordingAnalysisService();
            var encodingService = new BlockingEncodingService();
            var workspace = new EncodeWorkspaceViewModel(
                analysisService,
                encodingService,
                new StaticEncoderCapabilityService(),
                null,
                initializeEncoderSupport: false);

            Assert.True(workspace.FileInput.SetFile(inputPath));
            await analysisService.WaitForStrategyCountAsync(1);

            string fullInputPath = Path.GetFullPath(inputPath);
            Task encodeTask = workspace.StartEncodeCommand.ExecuteAsync(null);
            await encodingService.WaitForStartAsync();

            Assert.True(workspace.IsEncodeInProgress);
            Assert.False(workspace.FileInput.ClearFileCommand.CanExecute(null));
            Assert.False(workspace.FileInput.SelectFileCommand.CanExecute(null));

            workspace.FileInput.ClearFileCommand.Execute(null);
            Assert.Equal(fullInputPath, workspace.FileInput.InputFilePath);

            bool changed = workspace.FileInput.SetFile(replacementPath);

            Assert.False(changed);
            Assert.Equal(fullInputPath, workspace.FileInput.InputFilePath);
            Assert.Equal(FileInputViewModel.LockedSelectionMessage, workspace.FileInput.ValidationMessage);

            workspace.CancelEncodeCommand.Execute(null);
            await encodeTask;

            Assert.False(workspace.IsEncodeInProgress);
            Assert.True(workspace.FileInput.ClearFileCommand.CanExecute(null));
            Assert.True(workspace.FileInput.SelectFileCommand.CanExecute(null));
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(replacementPath);
        }
    }

    private sealed class RecordingAnalysisService : IVideoAnalysisService
    {
        private readonly List<StrategyAnalysis> _strategies = [];

        public Task<VideoInfo> ProbeAsync(string inputPath, CancellationToken ct = default) =>
            Task.FromResult(new VideoInfo(TimeSpan.FromSeconds(95), 1920, 1080, 59.94));

        public int StrategyCount
        {
            get
            {
                lock (_strategies)
                {
                    return _strategies.Count;
                }
            }
        }

        public int DetectCropCallCount { get; private set; }

        public VideoClipRange? LastRequestedClipRange { get; private set; }

        public EncodeSettings? LastRequestedSettings { get; private set; }

        public Task<string?> DetectCropAsync(string inputPath, VideoInfo info, CancellationToken ct = default)
        {
            DetectCropCallCount++;
            return Task.FromResult<string?>("crop=1920:800:0:140");
        }

        public Task<StrategyAnalysis> AnalyzeStrategyAsync(
            string inputPath,
            VideoInfo info,
            EncodeSettings settings,
            string? cropFilter = null,
            VideoClipRange? clipRange = null,
            CancellationToken ct = default)
        {
            LastRequestedClipRange = clipRange;
            LastRequestedSettings = settings;
            var strategy = new StrategyAnalysis(
                Path.GetFullPath(inputPath),
                cropFilter,
                EncodePlanner.BuildFrameRateFilter(info.FrameRate, settings),
                EncodePlanner.ResolveOutputFrameRate(info.FrameRate, settings),
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

    private sealed class DelayedCropAnalysisService : IVideoAnalysisService
    {
        private readonly TaskCompletionSource _cropRequestedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string?> _cropResultTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<StrategyAnalysis> _strategies = [];

        public int DetectCropCallCount { get; private set; }

        public int StrategyCount
        {
            get
            {
                lock (_strategies)
                {
                    return _strategies.Count;
                }
            }
        }

        public VideoClipRange? LastRequestedClipRange { get; private set; }

        public Task<VideoInfo> ProbeAsync(string inputPath, CancellationToken ct = default) =>
            Task.FromResult(new VideoInfo(TimeSpan.FromSeconds(95), 1920, 1080, 59.94));

        public Task<string?> DetectCropAsync(string inputPath, VideoInfo info, CancellationToken ct = default)
        {
            DetectCropCallCount++;
            _cropRequestedTcs.TrySetResult();
            ct.Register(() => _cropResultTcs.TrySetCanceled(ct));
            return _cropResultTcs.Task;
        }

        public Task<StrategyAnalysis> AnalyzeStrategyAsync(
            string inputPath,
            VideoInfo info,
            EncodeSettings settings,
            string? cropFilter = null,
            VideoClipRange? clipRange = null,
            CancellationToken ct = default)
        {
            LastRequestedClipRange = clipRange;
            var strategy = new StrategyAnalysis(
                Path.GetFullPath(inputPath),
                cropFilter,
                EncodePlanner.BuildFrameRateFilter(info.FrameRate, settings),
                EncodePlanner.ResolveOutputFrameRate(info.FrameRate, settings),
                new EncodePlanner.EncodePlan(1800, 1, "scale=-2:min(ih\\,1080)", "1080p (original)"));

            lock (_strategies)
            {
                _strategies.Add(strategy);
            }

            return Task.FromResult(strategy);
        }

        public void CompleteCropDetection(string? cropFilter) => _cropResultTcs.TrySetResult(cropFilter);

        public Task WaitForCropDetectionRequestAsync() => _cropRequestedTcs.Task;

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

    private sealed class BlockingEncodingService : IVideoEncodingService
    {
        private readonly TaskCompletionSource _startTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RunAsync(
            EncodeRequest request,
            Microsoft.Extensions.Logging.ILogger<ProcessingPipeline> logger,
            IProgress<EncodeProgress>? progress = null,
            CancellationToken ct = default)
        {
            _startTcs.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }

        public Task WaitForStartAsync() => _startTcs.Task;
    }

    private sealed class StaticEncoderCapabilityService : IEncoderCapabilityService
    {
        public Task<bool> IsAv1NvencSupportedAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class RecordingSettingsCoordinator : IAppSettingsCoordinator
    {
        private TaskCompletionSource<AppSettings> _updateTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

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

        public async Task<AppSettings> WaitForUpdateAsync()
        {
            AppSettings settings = await _updateTcs.Task;
            _updateTcs = new TaskCompletionSource<AppSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
            return settings;
        }
    }
}
