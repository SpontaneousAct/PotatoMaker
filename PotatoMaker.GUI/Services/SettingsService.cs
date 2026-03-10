using System.Text.Json;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Loads and saves app settings.
/// </summary>
public interface IAppSettingsService
{
    AppSettings Load();

    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}

/// <summary>
/// Persists <see cref="AppSettings"/> as JSON in <c>%APPDATA%/PotatoMaker/settings.json</c>.
/// </summary>
public sealed class JsonAppSettingsService : IAppSettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PotatoMaker",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        try
        {
            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        string directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(SettingsPath, json, ct).ConfigureAwait(false);
    }
}
