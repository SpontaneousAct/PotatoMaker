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
                LastOutputFolder = null
            });

        await coordinator.UpdateAsync(settings => settings with
        {
            IsDarkMode = true,
            LastOutputFolder = "C:\\out"
        });

        Assert.True(coordinator.Current.IsDarkMode);
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

