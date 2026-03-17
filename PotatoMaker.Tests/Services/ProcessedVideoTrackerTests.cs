using PotatoMaker.GUI.Services;
using Xunit;

namespace PotatoMaker.Tests.Services;

public sealed class ProcessedVideoTrackerTests
{
    [Fact]
    public async Task MarkProcessedAsync_PersistsNormalizedSourceAndMatchesByLastWriteTime()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"potatomaker-processed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        string inputPath = Path.Combine(tempDirectory, "clip.mp4");
        await File.WriteAllTextAsync(inputPath, "video");

        try
        {
            var coordinator = new RecordingSettingsCoordinator(new AppSettings());
            var tracker = new ProcessedVideoTracker(coordinator);
            DateTimeOffset lastWrite = new(File.GetLastWriteTime(inputPath));

            await tracker.MarkProcessedAsync(inputPath);

            Assert.True(tracker.IsProcessed(inputPath, lastWrite));
            Assert.NotNull(coordinator.Current.ProcessedVideos);
            Assert.Single(coordinator.Current.ProcessedVideos!);

            ProcessedVideoRecord record = coordinator.Current.ProcessedVideos![0];
            Assert.Equal(Path.GetFullPath(inputPath), record.FullPath);
            Assert.Equal(File.GetLastWriteTimeUtc(inputPath).Ticks, record.SourceLastWriteUtcTicks);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task MarkProcessedAsync_DeduplicatesKeysAndKeepsNewestEntriesBounded()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"potatomaker-processed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string duplicatePath = Path.Combine(tempDirectory, "duplicate.mp4");
            await File.WriteAllTextAsync(duplicatePath, "video");
            long duplicateTicks = File.GetLastWriteTimeUtc(duplicatePath).Ticks;
            DateTimeOffset now = DateTimeOffset.UtcNow;

            ProcessedVideoRecord[] existingRecords =
            [
                new ProcessedVideoRecord(duplicatePath, duplicateTicks, now.AddMinutes(-30)),
                new ProcessedVideoRecord(duplicatePath, duplicateTicks, now.AddMinutes(-20))
            ];

            for (int index = 0; index < ProcessedVideoTracker.MaxTrackedVideos; index++)
            {
                string path = Path.Combine(tempDirectory, $"clip-{index:D3}.mp4");
                await File.WriteAllTextAsync(path, "video");
                existingRecords =
                [
                    .. existingRecords,
                    new ProcessedVideoRecord(
                        path,
                        File.GetLastWriteTimeUtc(path).Ticks,
                        now.AddMinutes(-10 - index))
                ];
            }

            var coordinator = new RecordingSettingsCoordinator(new AppSettings
            {
                ProcessedVideos = existingRecords
            });
            var tracker = new ProcessedVideoTracker(coordinator);

            await tracker.MarkProcessedAsync(duplicatePath);

            Assert.NotNull(coordinator.Current.ProcessedVideos);
            Assert.Equal(ProcessedVideoTracker.MaxTrackedVideos, coordinator.Current.ProcessedVideos!.Length);
            Assert.Equal(Path.GetFullPath(duplicatePath), coordinator.Current.ProcessedVideos[0].FullPath);
            Assert.Equal(1, coordinator.Current.ProcessedVideos.Count(record =>
                string.Equals(record.FullPath, Path.GetFullPath(duplicatePath), StringComparison.OrdinalIgnoreCase) &&
                record.SourceLastWriteUtcTicks == duplicateTicks));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private sealed class RecordingSettingsCoordinator : IAppSettingsCoordinator
    {
        public RecordingSettingsCoordinator(AppSettings current)
        {
            Current = current;
        }

        public AppSettings Current { get; private set; }

        public Task UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken ct = default)
        {
            Current = update(Current);
            return Task.CompletedTask;
        }
    }
}
