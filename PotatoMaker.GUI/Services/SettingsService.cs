using System.Text.Json;
using System.Diagnostics;

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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public JsonAppSettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PotatoMaker",
            "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
            return new AppSettings();

        try
        {
            string json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            Trace.TraceWarning("Failed to parse settings file '{0}'. Default settings will be used. {1}", _settingsPath, ex.Message);
            return new AppSettings();
        }
        catch (IOException ex)
        {
            Trace.TraceWarning("Failed to read settings file '{0}'. Default settings will be used. {1}", _settingsPath, ex.Message);
            return new AppSettings();
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.TraceWarning("Settings file '{0}' is not accessible. Default settings will be used. {1}", _settingsPath, ex.Message);
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        string directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(directory, $"{Path.GetFileName(_settingsPath)}.{Guid.NewGuid():N}.tmp");
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        try
        {
            await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            if (File.Exists(_settingsPath))
            {
                string backupPath = Path.Combine(directory, $"{Path.GetFileName(_settingsPath)}.bak");
                File.Replace(tempPath, _settingsPath, backupPath, ignoreMetadataErrors: true);
                TryDeleteFile(backupPath);
            }
            else
            {
                File.Move(tempPath, _settingsPath);
            }
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
