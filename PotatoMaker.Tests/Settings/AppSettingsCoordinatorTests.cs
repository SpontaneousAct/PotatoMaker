using Xunit;
using PotatoMaker.GUI.Services;

namespace PotatoMaker.Tests.Settings;

public sealed class AppSettingsCoordinatorTests
{
    [Fact]
    public async Task UpdateAsync_UpdatesCurrentAndPersistsNewSettings()
    {
        var persistence = new RecordingSettingsService();
        var coordinator = new AppSettingsCoordinator(
            persistence,
            new AppSettings
            {
                IsDarkMode = false,
                UseNvencEncoder = true,
                OutputNamePrefix = "",
                OutputNameSuffix = "_discord",
                FrameRateMode = PotatoMaker.Core.EncodeFrameRateMode.Original,
                PreviewVolumePercent = 100,
                SvtAv1Preset = 6,
                LastOutputFolder = null,
                RecentVideosDirectory = AppSettings.DefaultRecentVideosDirectory
            });

        await coordinator.UpdateAsync(settings => settings with
        {
            IsDarkMode = true,
            OutputNamePrefix = "clip_",
            OutputNameSuffix = "_mobile",
            FrameRateMode = PotatoMaker.Core.EncodeFrameRateMode.Fps30,
            PreviewVolumePercent = 42,
            SvtAv1Preset = 8,
            LastOutputFolder = "C:\\out",
            RecentVideosDirectory = "D:\\Captures"
        });

        Assert.True(coordinator.Current.IsDarkMode);
        Assert.Equal("clip_", coordinator.Current.OutputNamePrefix);
        Assert.Equal("_mobile", coordinator.Current.OutputNameSuffix);
        Assert.Equal(PotatoMaker.Core.EncodeFrameRateMode.Fps30, coordinator.Current.FrameRateMode);
        Assert.Equal(42, coordinator.Current.PreviewVolumePercent);
        Assert.Equal(8, coordinator.Current.SvtAv1Preset);
        Assert.Equal("C:\\out", coordinator.Current.LastOutputFolder);
        Assert.Equal("D:\\Captures", coordinator.Current.RecentVideosDirectory);
        Assert.Single(persistence.SavedSettings);
        Assert.Equal(coordinator.Current, persistence.SavedSettings[0]);
    }

    private sealed class RecordingSettingsService : IAppSettingsService
    {
        public List<AppSettings> SavedSettings { get; } = [];

        public AppSettings Load() => new();

        public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
        {
            SavedSettings.Add(settings);
            return Task.CompletedTask;
        }
    }
}

