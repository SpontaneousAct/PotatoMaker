using System.Text.Json;
using System.Text.Json.Nodes;
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
/// Persists <see cref="AppSettings"/> in <c>%APPDATA%/PotatoMaker/appsettings.json</c>.
/// </summary>
public sealed class JsonAppSettingsService : IAppSettingsService
{
    private const string AppSettingsSectionName = "AppSettings";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;
    private readonly string _legacySettingsPath;

    public JsonAppSettingsService(string? settingsPath = null, string? legacySettingsPath = null)
    {
        string settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PotatoMaker");

        _settingsPath = settingsPath ?? Path.Combine(settingsDirectory, "appsettings.json");
        _legacySettingsPath = legacySettingsPath ?? Path.Combine(Path.GetDirectoryName(_settingsPath) ?? settingsDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        string? settingsPath = ResolveSettingsPathForLoad();
        if (settingsPath is null)
            return new AppSettings();

        try
        {
            string json = File.ReadAllText(settingsPath);
            JsonNode? rootNode = JsonNode.Parse(json);
            if (rootNode is null)
                return new AppSettings();

            JsonNode? settingsNode = rootNode is JsonObject rootObject &&
                                     rootObject.TryGetPropertyValue(AppSettingsSectionName, out JsonNode? sectionNode)
                ? sectionNode
                : rootNode;

            return settingsNode.Deserialize<AppSettings>(JsonOptions) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            Trace.TraceWarning("Failed to parse settings file '{0}'. Default settings will be used. {1}", settingsPath, ex.Message);
            return new AppSettings();
        }
        catch (IOException ex)
        {
            Trace.TraceWarning("Failed to read settings file '{0}'. Default settings will be used. {1}", settingsPath, ex.Message);
            return new AppSettings();
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.TraceWarning("Settings file '{0}' is not accessible. Default settings will be used. {1}", settingsPath, ex.Message);
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        string directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(directory, $"{Path.GetFileName(_settingsPath)}.{Guid.NewGuid():N}.tmp");
        JsonObject root = await LoadExistingRootObjectAsync(ct).ConfigureAwait(false) ?? new JsonObject();
        root[AppSettingsSectionName] = JsonSerializer.SerializeToNode(settings, JsonOptions);
        string json = root.ToJsonString(JsonOptions);
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

    private string? ResolveSettingsPathForLoad()
    {
        if (File.Exists(_settingsPath))
            return _settingsPath;

        return File.Exists(_legacySettingsPath)
            ? _legacySettingsPath
            : null;
    }

    private async Task<JsonObject?> LoadExistingRootObjectAsync(CancellationToken ct)
    {
        if (!File.Exists(_settingsPath))
            return null;

        try
        {
            string json = await File.ReadAllTextAsync(_settingsPath, ct).ConfigureAwait(false);
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException ex)
        {
            Trace.TraceWarning("Failed to parse existing settings file '{0}' before save. It will be overwritten. {1}", _settingsPath, ex.Message);
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
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
