using CommunityToolkit.Mvvm.Input;
using PotatoMaker.Core;
using PotatoMaker.GUI.Services;
using PotatoMaker.GUI.ViewModels;
using Xunit;

namespace PotatoMaker.Tests.ViewModels;

public sealed class CompressionQueueViewModelTests
{
    [Fact]
    public async Task AddAsync_RejectsExactDuplicates_AndHonorsQueueLimit()
    {
        var queue = new CompressionQueueViewModel(
            null,
            new NoOpEncodingService(),
            new EncodeExecutionCoordinator());

        string inputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
        await File.WriteAllTextAsync(inputPath, new string('a', 1024));

        try
        {
            QueuedCompressionItemDraft draft = CreateDraft(inputPath, "D:\\encoded", new VideoClipRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(40)), 256);

            QueueEnqueueResult firstResult = await queue.AddAsync(draft);
            QueueEnqueueResult duplicateResult = await queue.AddAsync(draft);

            Assert.True(firstResult.Succeeded);
            Assert.False(duplicateResult.Succeeded);
            Assert.Single(queue.Items);

            for (int index = 1; index < queue.MaxQueueSize; index++)
            {
                string nextPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mov");
                await File.WriteAllTextAsync(nextPath, new string('b', 512));
                QueueEnqueueResult addResult = await queue.AddAsync(CreateDraft(nextPath, "D:\\encoded", VideoClipRange.Full(TimeSpan.FromSeconds(100)), 512));
                Assert.True(addResult.Succeeded);
            }

            QueueEnqueueResult overflowResult = await queue.AddAsync(CreateDraft(
                Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mkv"),
                "D:\\encoded",
                VideoClipRange.Full(TimeSpan.FromSeconds(100)),
                512));

            Assert.False(overflowResult.Succeeded);
            Assert.Equal(queue.MaxQueueSize, queue.ActiveItemCount);
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    [Fact]
    public async Task AddAsync_AllowsSameClipWhenCropDiffers()
    {
        var queue = new CompressionQueueViewModel(
            null,
            new NoOpEncodingService(),
            new EncodeExecutionCoordinator());

        string inputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
        await File.WriteAllTextAsync(inputPath, new string('a', 1024));

        try
        {
            QueuedCompressionItemDraft autoCropDraft = CreateDraft(
                inputPath,
                "D:\\encoded",
                new VideoClipRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(40)),
                256,
                "crop=1920:800:0:140");
            QueuedCompressionItemDraft manualCropDraft = CreateDraft(
                inputPath,
                "D:\\encoded",
                new VideoClipRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(40)),
                256,
                "crop=1920:820:0:130");

            QueueEnqueueResult firstResult = await queue.AddAsync(autoCropDraft);
            QueueEnqueueResult secondResult = await queue.AddAsync(manualCropDraft);

            Assert.True(firstResult.Succeeded);
            Assert.True(secondResult.Succeeded);
            Assert.Equal(2, queue.Items.Count);
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    [Fact]
    public async Task CompressAllAsync_ProcessesQueuedItemsSequentially_AndPersistsState()
    {
        string firstInputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mp4");
        string secondInputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mov");
        await File.WriteAllTextAsync(firstInputPath, "first");
        await File.WriteAllTextAsync(secondInputPath, "second");

        try
        {
            var settingsCoordinator = new RecordingSettingsCoordinator(new AppSettings());
            var notifier = new RecordingEncodeCompletionNotifier();
            var encodingService = new RecordingEncodingService(
                new ProcessingPipelineResult(["D:\\encoded\\first.mp4"], 1_048_576),
                new ProcessingPipelineResult(["D:\\encoded\\second.mp4"], 524_288));
            var processedTracker = new RecordingProcessedVideoTracker();
            var queue = new CompressionQueueViewModel(
                settingsCoordinator,
                encodingService,
                new EncodeExecutionCoordinator(),
                notifier,
                processedVideoTracker: processedTracker);

            Assert.True((await queue.AddAsync(CreateDraft(firstInputPath, "D:\\encoded", new VideoClipRange(TimeSpan.Zero, TimeSpan.FromSeconds(20)), 100))).Succeeded);
            Assert.True((await queue.AddAsync(CreateDraft(
                secondInputPath,
                "D:\\encoded",
                new VideoClipRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(35)),
                80,
                cropFilter: "crop=1280:720:0:0",
                outputFrameRate: 30,
                parts: 3))).Succeeded);

            await queue.CompressAllCommand.ExecuteAsync(null);

            Assert.Equal(
                [Path.GetFullPath(firstInputPath), Path.GetFullPath(secondInputPath)],
                encodingService.Requests.Select(request => request.InputPath).ToArray());
            Assert.All(queue.Items, item => Assert.Equal(CompressionQueueItemStatus.Completed, item.Status));
            Assert.All(queue.Items, item => Assert.Equal("Done", item.ProgressText));
            Assert.All(queue.Items, item => Assert.Equal("100%", item.ProgressPercentText));
            Assert.All(queue.Items, item => Assert.NotEqual("--", item.ElapsedText));
            Assert.Equal("60", queue.Items[0].OutputFpsText);
            Assert.Equal("12:5", queue.Items[0].CropText);
            Assert.Equal("1", queue.Items[0].OutputPartsText);
            Assert.Equal("30", queue.Items[1].OutputFpsText);
            Assert.Equal("16:9", queue.Items[1].CropText);
            Assert.Equal("3", queue.Items[1].OutputPartsText);
            Assert.Equal(0, queue.ActiveItemCount);
            Assert.Equal(1, notifier.NotificationCount);
            Assert.Equal(
                [Path.GetFullPath(firstInputPath), Path.GetFullPath(secondInputPath)],
                processedTracker.MarkedPaths);

            AppSettings persisted = settingsCoordinator.Current;
            Assert.NotNull(persisted.CompressionQueueItems);
            Assert.Empty(persisted.CompressionQueueItems!);
        }
        finally
        {
            File.Delete(firstInputPath);
            File.Delete(secondInputPath);
        }
    }

    [Fact]
    public void UpdateProgress_DoesNotOverwriteCompletedState()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mp4");
        CompressionQueueItemViewModel item = CompressionQueueItemViewModel.Create(
            CreateDraft(inputPath, "D:\\encoded", VideoClipRange.Full(TimeSpan.FromSeconds(100)), 123));

        item.MarkEncoding();
        item.MarkCompleted(new ProcessingPipelineResult(["D:\\encoded\\done.mp4"], 100));
        item.UpdateProgress(new EncodeProgress("encoding", 100));

        Assert.Equal(CompressionQueueItemStatus.Completed, item.Status);
        Assert.Equal("Done", item.ProgressText);
    }

    [Fact]
    public void CropText_SnapsNearCommonAspectRatios()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mp4");
        CompressionQueueItemViewModel item = CompressionQueueItemViewModel.Create(
            CreateDraft(
                inputPath,
                "D:\\encoded",
                VideoClipRange.Full(TimeSpan.FromSeconds(100)),
                123,
                cropFilter: "crop=808:1440:0:0"));

        Assert.Equal("9:16", item.CropText);
    }

    [Fact]
    public async Task ClearQueue_RemovesVisibleItems_AndClearsPersistedQueue()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(inputPath, "clear");

        try
        {
            var settingsCoordinator = new RecordingSettingsCoordinator(new AppSettings());
            var queue = new CompressionQueueViewModel(
                settingsCoordinator,
                new NoOpEncodingService(),
                new EncodeExecutionCoordinator());

            Assert.True((await queue.AddAsync(CreateDraft(inputPath, "D:\\encoded", VideoClipRange.Full(TimeSpan.FromSeconds(100)), 123))).Succeeded);

            await queue.ClearQueueCommand.ExecuteAsync(null);

            Assert.Empty(queue.Items);
            Assert.NotNull(settingsCoordinator.Current.CompressionQueueItems);
            Assert.Empty(settingsCoordinator.Current.CompressionQueueItems!);
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    [Fact]
    public async Task RestartCancelledItem_RunsItAgain_WhenQueueIsIdle()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(inputPath, "retry");

        try
        {
            var settingsCoordinator = new RecordingSettingsCoordinator(new AppSettings());
            var notifier = new RecordingEncodeCompletionNotifier();
            var encodingService = new RecordingEncodingService(
                new ProcessingPipelineResult(["D:\\encoded\\retry.mp4"], 321_000));
            var queue = new CompressionQueueViewModel(
                settingsCoordinator,
                encodingService,
                new EncodeExecutionCoordinator(),
                notifier);

            Assert.True((await queue.AddAsync(CreateDraft(inputPath, "D:\\encoded", VideoClipRange.Full(TimeSpan.FromSeconds(100)), 123))).Succeeded);

            CompressionQueueItemViewModel item = Assert.Single(queue.Items);
            item.MarkCancelled();

            await item.PrimaryActionCommand.ExecuteAsync(null);

            Assert.Equal(CompressionQueueItemStatus.Completed, item.Status);
            Assert.Equal("Done", item.ProgressText);
            Assert.Single(encodingService.Requests);
            Assert.Equal(1, notifier.NotificationCount);
            Assert.NotNull(settingsCoordinator.Current.CompressionQueueItems);
            Assert.Empty(settingsCoordinator.Current.CompressionQueueItems!);
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    private static QueuedCompressionItemDraft CreateDraft(
        string inputPath,
        string outputDirectory,
        VideoClipRange clipRange,
        long selectedSizeBytes,
        string? cropFilter = "crop=1920:800:0:140",
        double outputFrameRate = 60,
        int parts = 1)
    {
        VideoInfo info = new(TimeSpan.FromSeconds(100), 1920, 1080, 60, 4000);
        EncodeSettings settings = new()
        {
            Encoder = EncoderChoice.Nvenc,
            OutputNamePrefix = "queue_",
            OutputNameSuffix = "_discord",
            FrameRateMode = EncodeFrameRateMode.Original,
            SvtAv1Preset = EncodeSettings.DefaultSvtAv1Preset
        };
        StrategyAnalysis strategy = new(
            Path.GetFullPath(inputPath),
            cropFilter,
            null,
            outputFrameRate,
            new EncodePlanner.EncodePlan(1800, parts, "scale=-2:min(ih\\,1080)", parts == 1 ? "1080p (original)" : $"1080p, {parts} parts"));

        return new QueuedCompressionItemDraft(
            Path.GetFullPath(inputPath),
            outputDirectory,
            info,
            strategy,
            settings,
            clipRange,
            selectedSizeBytes);
    }

    private sealed class NoOpEncodingService : IVideoEncodingService
    {
        public Task<ProcessingPipelineResult> RunAsync(
            EncodeRequest request,
            Microsoft.Extensions.Logging.ILogger<ProcessingPipeline> logger,
            IProgress<EncodeProgress>? progress = null,
            CancellationToken ct = default) =>
            Task.FromResult(new ProcessingPipelineResult([], 0));
    }

    private sealed class RecordingEncodingService(params ProcessingPipelineResult[] results) : IVideoEncodingService
    {
        private readonly Queue<ProcessingPipelineResult> _results = new(results);

        public List<EncodeRequest> Requests { get; } = [];

        public Task<ProcessingPipelineResult> RunAsync(
            EncodeRequest request,
            Microsoft.Extensions.Logging.ILogger<ProcessingPipeline> logger,
            IProgress<EncodeProgress>? progress = null,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            progress?.Report(new EncodeProgress("encoding", 100));
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class RecordingProcessedVideoTracker : IProcessedVideoTracker
    {
        public event EventHandler? ProcessedVideosChanged;

        public List<string> MarkedPaths { get; } = [];

        public bool IsProcessed(string videoPath, DateTimeOffset lastModified) => false;

        public Task MarkProcessedAsync(string videoPath, CancellationToken ct = default)
        {
            MarkedPaths.Add(Path.GetFullPath(videoPath));
            ProcessedVideosChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEncodeCompletionNotifier : IEncodeCompletionNotifier
    {
        public int NotificationCount { get; private set; }

        public void NotifyEncodeSucceeded() => NotificationCount++;
    }

    private sealed class RecordingSettingsCoordinator(AppSettings initialSettings) : IAppSettingsCoordinator
    {
        public AppSettings Current { get; private set; } = initialSettings;

        public Task UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken ct = default)
        {
            Current = update(Current);
            return Task.CompletedTask;
        }
    }
}
