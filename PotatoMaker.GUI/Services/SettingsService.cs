using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using PotatoMaker.Core;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Persists <see cref="EncodeSettings"/> as JSON in <c>%APPDATA%/PotatoMaker/settings.json</c>.
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

    public static async Task<EncodeSettings> LoadAsync()
    {
        if (!File.Exists(SettingsPath))
            return new EncodeSettings();

        try
        {
            var json = await File.ReadAllTextAsync(SettingsPath);
            return JsonSerializer.Deserialize<EncodeSettings>(json, JsonOptions) ?? new EncodeSettings();
        }
        catch
        {
            // Corrupted file — return defaults
            return new EncodeSettings();
        }
    }

    public static async Task SaveAsync(EncodeSettings settings)
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(SettingsPath, json);
    }
}
