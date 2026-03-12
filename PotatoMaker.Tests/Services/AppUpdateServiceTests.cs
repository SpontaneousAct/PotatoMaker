using NuGet.Versioning;
using PotatoMaker.GUI.Services;
using Velopack;
using Xunit;

namespace PotatoMaker.Tests.Services;

public sealed class AppUpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsAvailableSnapshot_WhenRemoteReleaseExists()
    {
        var manager = new StubVelopackUpdateManager
        {
            IsInstalled = true,
            AvailableUpdate = CreateUpdateInfo("1.2.3")
        };
        var service = CreateService(manager);

        AppUpdateSnapshot snapshot = await service.CheckForUpdatesAsync();

        Assert.True(snapshot.IsConfigured);
        Assert.True(snapshot.CanSelfUpdate);
        Assert.True(snapshot.IsUpdateAvailable);
        Assert.Equal("1.2.3", snapshot.AvailableVersion);
    }

    [Fact]
    public async Task GetCurrentStateAsync_ReturnsPendingRestart_WhenUpdateWasAlreadyDownloaded()
    {
        var manager = new StubVelopackUpdateManager
        {
            IsInstalled = true,
            IsUpdatePendingRestart = true,
            UpdatePendingRestart = new VelopackAsset
            {
                Version = SemanticVersion.Parse("2.0.0")
            }
        };
        var service = CreateService(manager);

        AppUpdateSnapshot snapshot = await service.GetCurrentStateAsync();

        Assert.True(snapshot.IsUpdatePendingRestart);
        Assert.Equal("2.0.0", snapshot.AvailableVersion);
    }

    [Fact]
    public async Task ApplyUpdateAsync_DownloadsAndAppliesCachedRelease()
    {
        var manager = new StubVelopackUpdateManager
        {
            IsInstalled = true,
            AvailableUpdate = CreateUpdateInfo("3.4.5")
        };
        var service = CreateService(manager);

        await service.CheckForUpdatesAsync();
        await service.ApplyUpdateAsync();

        Assert.Equal(1, manager.DownloadCallCount);
        Assert.Equal("3.4.5", manager.AppliedVersion);
    }

    private static AppUpdateService CreateService(StubVelopackUpdateManager manager) =>
        new(
            new UpdateSettings
            {
                Mode = UpdateSourceMode.GitHub,
                GitHubRepositoryUrl = "https://github.com/example/repo"
            },
            new StubVelopackUpdateManagerFactory(manager));

    private static UpdateInfo CreateUpdateInfo(string version) =>
        new(
            new VelopackAsset
            {
                Version = SemanticVersion.Parse(version)
            },
            isDowngrade: false,
            deltaBaseRelease: null!,
            deltasToTarget: []);

    private sealed class StubVelopackUpdateManagerFactory(IVelopackUpdateManager manager) : IVelopackUpdateManagerFactory
    {
        public IVelopackUpdateManager? Create(UpdateSettings settings) => manager;
    }

    private sealed class StubVelopackUpdateManager : IVelopackUpdateManager
    {
        public bool IsInstalled { get; init; }

        public bool IsPortable { get; init; }

        public bool IsUpdatePendingRestart { get; init; }

        public VelopackAsset? UpdatePendingRestart { get; init; }

        public UpdateInfo? AvailableUpdate { get; init; }

        public int DownloadCallCount { get; private set; }

        public string? AppliedVersion { get; private set; }

        public Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken ct = default) =>
            Task.FromResult(AvailableUpdate);

        public Task DownloadUpdatesAsync(UpdateInfo updates, Action<int> progress, CancellationToken ct = default)
        {
            DownloadCallCount++;
            progress(100);
            return Task.CompletedTask;
        }

        public void ApplyUpdatesAndRestart(VelopackAsset toApply, string[] restartArgs)
        {
            AppliedVersion = toApply.Version?.ToFullString() ?? toApply.Version?.ToString();
        }
    }
}
