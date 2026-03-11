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
                PreviewVolumePercent = 100,
                SvtAv1Preset = 6,
                LastOutputFolder = null
            });

        await coordinator.UpdateAsync(settings => settings with
        {
            IsDarkMode = true,
            OutputNamePrefix = "clip_",
            OutputNameSuffix = "_mobile",
            PreviewVolumePercent = 42,
            SvtAv1Preset = 8,
            LastOutputFolder = "C:\\out"
        });

        Assert.True(coordinator.Current.IsDarkMode);
        Assert.Equal("clip_", coordinator.Current.OutputNamePrefix);
        Assert.Equal("_mobile", coordinator.Current.OutputNameSuffix);
        Assert.Equal(42, coordinator.Current.PreviewVolumePercent);
        Assert.Equal(8, coordinator.Current.SvtAv1Preset);
        Assert.Equal("C:\\out", coordinator.Current.LastOutputFolder);
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

