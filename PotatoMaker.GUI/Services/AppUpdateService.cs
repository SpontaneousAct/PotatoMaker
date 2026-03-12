using System.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace PotatoMaker.GUI.Services;

public interface IAppUpdateService
{
    bool ShouldCheckOnStartup { get; }

    TimeSpan StartupCheckDelay { get; }

    Task<AppUpdateSnapshot> GetCurrentStateAsync(CancellationToken ct = default);

    Task<AppUpdateSnapshot> CheckForUpdatesAsync(CancellationToken ct = default);

    Task ApplyUpdateAsync(Action<int>? progress = null, CancellationToken ct = default);
}

/// <summary>
/// Disables update functionality when no valid source is configured.
/// </summary>
public sealed class DisabledAppUpdateService : IAppUpdateService
{
    public bool ShouldCheckOnStartup => false;

    public TimeSpan StartupCheckDelay => TimeSpan.Zero;

    public Task<AppUpdateSnapshot> GetCurrentStateAsync(CancellationToken ct = default) =>
        Task.FromResult(AppUpdateSnapshot.Disabled);

    public Task<AppUpdateSnapshot> CheckForUpdatesAsync(CancellationToken ct = default) =>
        Task.FromResult(AppUpdateSnapshot.Disabled);

    public Task ApplyUpdateAsync(Action<int>? progress = null, CancellationToken ct = default) =>
        Task.CompletedTask;
}

public interface IVelopackUpdateManager
{
    bool IsInstalled { get; }

    bool IsPortable { get; }

    bool IsUpdatePendingRestart { get; }

    VelopackAsset? UpdatePendingRestart { get; }

    Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken ct = default);

    Task DownloadUpdatesAsync(UpdateInfo updates, Action<int> progress, CancellationToken ct = default);

    void ApplyUpdatesAndRestart(VelopackAsset toApply, string[] restartArgs);
}

public interface IVelopackUpdateManagerFactory
{
    IVelopackUpdateManager? Create(UpdateSettings settings);
}

/// <summary>
/// Wraps Velopack's update APIs behind a small testable abstraction.
/// </summary>
public sealed class VelopackUpdateManagerFactory : IVelopackUpdateManagerFactory
{
    public IVelopackUpdateManager? Create(UpdateSettings settings)
    {
        IUpdateSource? source = CreateSource(settings);
        if (source is null)
            return null;

        UpdateOptions options = new();
        if (!string.IsNullOrWhiteSpace(settings.ExplicitChannel))
            options.ExplicitChannel = settings.ExplicitChannel.Trim();

        return new VelopackUpdateManagerAdapter(new UpdateManager(source, options, locator: null));
    }

    private static IUpdateSource? CreateSource(UpdateSettings settings)
    {
        return settings.Mode switch
        {
            UpdateSourceMode.GitHub when !string.IsNullOrWhiteSpace(settings.GitHubRepositoryUrl) =>
                new GithubSource(
                    settings.GitHubRepositoryUrl.Trim(),
                    ResolveGitHubToken(settings),
                    settings.AllowPrerelease,
                    downloader: null!),
            UpdateSourceMode.File when !string.IsNullOrWhiteSpace(settings.LocalReleasePath) =>
                new SimpleFileSource(new DirectoryInfo(
                    Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.LocalReleasePath.Trim())))),
            _ => null
        };
    }

    private static string ResolveGitHubToken(UpdateSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.GitHubAccessToken))
            return settings.GitHubAccessToken.Trim();

        string? token = Environment.GetEnvironmentVariable("POTATOMAKER_UPDATE_GITHUB_TOKEN");
        return string.IsNullOrWhiteSpace(token)
            ? string.Empty
            : token.Trim();
    }

    private sealed class VelopackUpdateManagerAdapter(UpdateManager inner) : IVelopackUpdateManager
    {
        public bool IsInstalled => inner.IsInstalled;

        public bool IsPortable => inner.IsPortable;

        public bool IsUpdatePendingRestart => inner.UpdatePendingRestart is not null;

        public VelopackAsset? UpdatePendingRestart => inner.UpdatePendingRestart;

        public Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return inner.CheckForUpdatesAsync();
        }

        public Task DownloadUpdatesAsync(UpdateInfo updates, Action<int> progress, CancellationToken ct = default) =>
            inner.DownloadUpdatesAsync(updates, progress, ct);

        public void ApplyUpdatesAndRestart(VelopackAsset toApply, string[] restartArgs) =>
            inner.ApplyUpdatesAndRestart(toApply, restartArgs);
    }
}

/// <summary>
/// Coordinates background update checks and explicit user-driven update application.
/// </summary>
public sealed class AppUpdateService : IAppUpdateService
{
    private readonly UpdateSettings _settings;
    private readonly IVelopackUpdateManager? _updateManager;
    private UpdateInfo? _availableUpdate;

    public AppUpdateService(UpdateSettings settings, IVelopackUpdateManagerFactory updateManagerFactory)
    {
        ArgumentNullException.ThrowIfNull(updateManagerFactory);

        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _updateManager = updateManagerFactory.Create(settings);
    }

    public bool ShouldCheckOnStartup => _settings.CheckOnStartup;

    public TimeSpan StartupCheckDelay => TimeSpan.FromSeconds(Math.Max(0, _settings.StartupDelaySeconds));

    public Task<AppUpdateSnapshot> GetCurrentStateAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(BuildCurrentSnapshot());
    }

    public async Task<AppUpdateSnapshot> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        if (_updateManager is null)
            return AppUpdateSnapshot.Disabled;

        if (!CanSelfUpdate(_updateManager))
            return BuildUnsupportedSnapshot();

        if (_updateManager.IsUpdatePendingRestart)
            return BuildPendingRestartSnapshot(_updateManager.UpdatePendingRestart);

        try
        {
            _availableUpdate = await _updateManager.CheckForUpdatesAsync(ct).ConfigureAwait(false);
            return _availableUpdate is null
                ? BuildConfiguredSnapshot()
                : BuildAvailableSnapshot(_availableUpdate);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to check for updates. {0}", ex.Message);
            _availableUpdate = null;
            return BuildConfiguredSnapshot();
        }
    }

    public async Task ApplyUpdateAsync(Action<int>? progress = null, CancellationToken ct = default)
    {
        if (_updateManager is null || !CanSelfUpdate(_updateManager))
            return;

        if (_updateManager.IsUpdatePendingRestart)
        {
            if (_updateManager.UpdatePendingRestart is { } pendingRestart)
                _updateManager.ApplyUpdatesAndRestart(pendingRestart, []);

            return;
        }

        if (_availableUpdate is null)
            return;

        Action<int> progressCallback = progress ?? (_ => { });
        await _updateManager.DownloadUpdatesAsync(_availableUpdate, progressCallback, ct).ConfigureAwait(false);
        _updateManager.ApplyUpdatesAndRestart(_availableUpdate.TargetFullRelease, []);
    }

    private AppUpdateSnapshot BuildCurrentSnapshot()
    {
        if (_updateManager is null)
            return AppUpdateSnapshot.Disabled;

        if (!CanSelfUpdate(_updateManager))
            return BuildUnsupportedSnapshot();

        if (_updateManager.IsUpdatePendingRestart)
            return BuildPendingRestartSnapshot(_updateManager.UpdatePendingRestart);

        return _availableUpdate is null
            ? BuildConfiguredSnapshot()
            : BuildAvailableSnapshot(_availableUpdate);
    }

    private static bool CanSelfUpdate(IVelopackUpdateManager updateManager) =>
        updateManager.IsInstalled || updateManager.IsPortable || updateManager.IsUpdatePendingRestart;

    private AppUpdateSnapshot BuildConfiguredSnapshot() => new(
        IsConfigured: true,
        CanSelfUpdate: true,
        IsUpdateAvailable: false,
        IsUpdatePendingRestart: false);

    private AppUpdateSnapshot BuildUnsupportedSnapshot() => new(
        IsConfigured: true,
        CanSelfUpdate: false,
        IsUpdateAvailable: false,
        IsUpdatePendingRestart: false);

    private static AppUpdateSnapshot BuildAvailableSnapshot(UpdateInfo update) => new(
        IsConfigured: true,
        CanSelfUpdate: true,
        IsUpdateAvailable: true,
        IsUpdatePendingRestart: false,
        AvailableVersion: NormalizeVersion(update.TargetFullRelease),
        ReleaseNotesMarkdown: update.TargetFullRelease?.NotesMarkdown);

    private static AppUpdateSnapshot BuildPendingRestartSnapshot(VelopackAsset? pendingAsset) => new(
        IsConfigured: true,
        CanSelfUpdate: true,
        IsUpdateAvailable: false,
        IsUpdatePendingRestart: true,
        AvailableVersion: NormalizeVersion(pendingAsset),
        ReleaseNotesMarkdown: pendingAsset?.NotesMarkdown);

    private static string? NormalizeVersion(VelopackAsset? asset) =>
        asset?.Version?.ToFullString() ?? asset?.Version?.ToString();
}
