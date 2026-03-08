using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Persists <see cref="AppSettings"/> as JSON in <c>%APPDATA%/PotatoMaker/settings.json</c>.
/// </summary>
public static class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PotatoMaker", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // Corrupted or incompatible file: return defaults.
            return new AppSettings();
        }
    }

    public static async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        try
        {
            var json = await File.ReadAllTextAsync(SettingsPath).ConfigureAwait(false);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // Corrupted or incompatible file: return defaults.
            return new AppSettings();
        }
    }

    public static async Task SaveAsync(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(SettingsPath, json).ConfigureAwait(false);
    }
}
