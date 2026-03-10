namespace PotatoMaker.GUI.Services;

/// <summary>
/// Keeps the current settings in memory and persists updates.
/// </summary>
public interface IAppSettingsCoordinator
{
    AppSettings Current { get; }

    Task UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken ct = default);
}

/// <summary>
/// Provides a shared, testable settings state for the desktop app.
/// </summary>
public sealed class AppSettingsCoordinator : IAppSettingsCoordinator
{
    private readonly IAppSettingsService _settingsService;
    private readonly SemaphoreSlim _sync = new(1, 1);

    public AppSettingsCoordinator(IAppSettingsService settingsService, AppSettings initialSettings)
    {
        _settingsService = settingsService;
        Current = initialSettings;
    }

    public AppSettings Current { get; private set; }

    public async Task UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        await _sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Current = update(Current);
            await _settingsService.SaveAsync(Current, ct).ConfigureAwait(false);
        }
        finally
        {
            _sync.Release();
        }
    }
}
