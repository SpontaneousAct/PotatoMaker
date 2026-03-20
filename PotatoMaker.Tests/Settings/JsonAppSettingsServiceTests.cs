using System.Diagnostics;
using PotatoMaker.GUI.Services;
using Xunit;

namespace PotatoMaker.Tests.Settings;

public sealed class JsonAppSettingsServiceTests
{
    [Fact]
    public void Load_InvalidJson_ReturnsDefaultsAndReportsWarning()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"potatomaker-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        string settingsPath = Path.Combine(tempDirectory, "appsettings.json");
        File.WriteAllText(settingsPath, "{ invalid json");

        var listener = new RecordingTraceListener();
        TraceListenerCollection listeners = Trace.Listeners;
        listeners.Add(listener);

        try
        {
            var service = new JsonAppSettingsService(settingsPath);

            AppSettings settings = service.Load();

            Assert.Equal(new AppSettings(), settings);
            Assert.Contains(listener.Messages, message => message.Contains("Failed to parse settings data", StringComparison.Ordinal));
        }
        finally
        {
            listeners.Remove(listener);
            listener.Dispose();
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_ReplacesExistingFileWithoutLeavingTemporaryFiles()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"potatomaker-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        string settingsPath = Path.Combine(tempDirectory, "appsettings.json");
        await File.WriteAllTextAsync(settingsPath, "{}");

        try
        {
            var service = new JsonAppSettingsService(settingsPath);

            await service.SaveAsync(new AppSettings
            {
                Theme = AppTheme.Sepia,
                UseNvencEncoder = false,
                OutputNamePrefix = "potato_",
                OutputNameSuffix = "_share",
                FrameRateMode = PotatoMaker.Core.EncodeFrameRateMode.Fps30,
                PreviewVolumePercent = 42,
                SvtAv1Preset = 8,
                LastOutputFolder = "C:\\encoded",
                RecentVideosDirectory = "D:\\Captures",
                ProcessedVideos =
                [
                    new ProcessedVideoRecord(
                        "C:\\Videos\\clip001.mp4",
                        638600000000000000,
                        new DateTimeOffset(2026, 3, 17, 9, 30, 0, TimeSpan.Zero))
                ],
                CompressionQueueItems =
                [
                    new QueuedCompressionItemRecord(
                        "queue-1",
                        "C:\\Videos\\clip001.mp4",
                        "C:\\encoded",
                        new PotatoMaker.Core.VideoInfo(TimeSpan.FromSeconds(90), 1920, 1080, 60),
                        new PotatoMaker.Core.StrategyAnalysis(
                            "C:\\Videos\\clip001.mp4",
                            "crop=1920:800:0:140",
                            null,
                            60,
                            new PotatoMaker.Core.EncodePlanner.EncodePlan(1800, 1, "scale=-2:min(ih\\,1080)", "1080p (original)")),
                        new PotatoMaker.Core.EncodeSettings(),
                        0,
                        TimeSpan.FromSeconds(30).Ticks,
                        2_048_000,
                        CompressionQueueItemStatus.Queued,
                        0,
            "Ready",
                        null,
                        null,
                        new DateTimeOffset(2026, 3, 18, 9, 30, 0, TimeSpan.Zero))
                ]
            });

            Assert.True(File.Exists(settingsPath));
            Assert.Empty(Directory.GetFiles(tempDirectory, "*.tmp"));
            Assert.Contains("\"AppSettings\"", await File.ReadAllTextAsync(settingsPath), StringComparison.Ordinal);

            AppSettings settings = service.Load();
            Assert.Equal(AppTheme.Sepia, settings.Theme);
            Assert.False(settings.UseNvencEncoder);
            Assert.Equal("potato_", settings.OutputNamePrefix);
            Assert.Equal("_share", settings.OutputNameSuffix);
            Assert.Equal(PotatoMaker.Core.EncodeFrameRateMode.Fps30, settings.FrameRateMode);
            Assert.Equal(42, settings.PreviewVolumePercent);
            Assert.Equal(8, settings.SvtAv1Preset);
            Assert.Equal("C:\\encoded", settings.LastOutputFolder);
            Assert.Equal("D:\\Captures", settings.RecentVideosDirectory);
            Assert.NotNull(settings.ProcessedVideos);
            Assert.Single(settings.ProcessedVideos!);
            Assert.Equal("C:\\Videos\\clip001.mp4", settings.ProcessedVideos[0].FullPath);
            Assert.Equal(638600000000000000, settings.ProcessedVideos[0].SourceLastWriteUtcTicks);
            Assert.NotNull(settings.CompressionQueueItems);
            Assert.Single(settings.CompressionQueueItems!);
            Assert.Equal("queue-1", settings.CompressionQueueItems[0].Id);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Load_WhenOnlyLegacySettingsFileExists_UsesLegacySettings()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"potatomaker-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        string settingsPath = Path.Combine(tempDirectory, "appsettings.json");
        string legacySettingsPath = Path.Combine(tempDirectory, "settings.json");
        File.WriteAllText(legacySettingsPath, """
            {
              "UseNvencEncoder": false,
              "OutputNamePrefix": "legacy_",
              "OutputNameSuffix": "_legacy",
              "RecentVideosDirectory": "E:\\LegacyCaptures",
              "ProcessedVideos": [
                {
                  "FullPath": "E:\\LegacyCaptures\\clip.mp4",
                  "SourceLastWriteUtcTicks": 638600000000000001,
                  "ProcessedAtUtc": "2026-03-17T09:30:00+00:00"
                }
              ],
              "CompressionQueueItems": [
                {
                  "Id": "queue-legacy",
                  "InputPath": "E:\\LegacyCaptures\\clip.mp4",
                  "OutputDirectory": "E:\\LegacyCaptures\\encoded",
                  "Info": {
                    "Duration": "00:01:30",
                    "Width": 1920,
                    "Height": 1080,
                    "FrameRate": 60,
                    "SourceVideoBitrateKbps": 4000
                  },
                  "Strategy": {
                    "InputPath": "E:\\LegacyCaptures\\clip.mp4",
                    "CropFilter": null,
                    "FrameRateFilter": null,
                    "OutputFrameRate": 60,
                    "Plan": {
                      "VideoBitrateKbps": 1800,
                      "Parts": 1,
                      "ScaleFilter": "scale=-2:min(ih\\\\,1080)",
                      "ResolutionLabel": "1080p (original)",
                      "IsBitrateCappedToSource": false,
                      "SourceVideoBitrateKbps": 4000
                    }
                  },
                  "Settings": {
                    "Encoder": "Nvenc",
                    "OutputNamePrefix": "",
                    "OutputNameSuffix": "_discord",
                    "FrameRateMode": "Original",
                    "TargetSizeMb": 9.5,
                    "EffectiveTargetMb": 9.0,
                    "AudioBitrateKbps": 128,
                    "SvtAv1Preset": 6,
                    "MinVideoBitrateKbps": 100,
                    "HdFloorKbps": 500,
                    "FullHdFloorKbps": 1000,
                    "MaxParts": 10,
                    "SkipCropDetect": false
                  },
                  "ClipStartTicks": 0,
                  "ClipEndTicks": 300000000,
                  "SelectedSizeBytes": 2048000,
                  "Status": "Queued",
                  "ProgressPercent": 0,
            "ProgressStateText": "Ready",
                  "OutputSizeBytes": null,
                  "FailureMessage": null,
                  "AddedAtUtc": "2026-03-18T09:30:00+00:00"
                }
              ]
            }
            """);

        try
        {
            var service = new JsonAppSettingsService(settingsPath);

            AppSettings settings = service.Load();

            Assert.False(settings.UseNvencEncoder);
            Assert.Equal("legacy_", settings.OutputNamePrefix);
            Assert.Equal("_legacy", settings.OutputNameSuffix);
            Assert.Equal("E:\\LegacyCaptures", settings.RecentVideosDirectory);
            Assert.NotNull(settings.ProcessedVideos);
            Assert.Single(settings.ProcessedVideos!);
            Assert.Equal("E:\\LegacyCaptures\\clip.mp4", settings.ProcessedVideos[0].FullPath);
            Assert.NotNull(settings.CompressionQueueItems);
            Assert.Single(settings.CompressionQueueItems!);
            Assert.Equal("queue-legacy", settings.CompressionQueueItems[0].Id);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Load_WhenLegacyIsDarkModeIsPresent_MigratesToDarkTheme()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"potatomaker-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        string settingsPath = Path.Combine(tempDirectory, "appsettings.json");
        File.WriteAllText(settingsPath, """
            {
              "AppSettings": {
                "IsDarkMode": true,
                "UseNvencEncoder": false
              }
            }
            """);

        try
        {
            var service = new JsonAppSettingsService(settingsPath);

            AppSettings settings = service.Load();

            Assert.Equal(AppTheme.Dark, settings.Theme);
            Assert.False(settings.UseNvencEncoder);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private sealed class RecordingTraceListener : TraceListener
    {
        public List<string> Messages { get; } = [];

        public override void Write(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                Messages.Add(message);
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                Messages.Add(message);
        }
    }
}
