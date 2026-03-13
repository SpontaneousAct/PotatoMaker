using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
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

    static JsonAppSettingsService()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    private readonly string _settingsPath;
    private readonly string _legacySettingsPath;
    private readonly string _packagedDefaultsPath;

    public JsonAppSettingsService(
        string? settingsPath = null,
        string? legacySettingsPath = null,
        string? packagedDefaultsPath = null)
    {
        string settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PotatoMaker");

        _settingsPath = settingsPath ?? Path.Combine(settingsDirectory, "appsettings.json");
        _legacySettingsPath = legacySettingsPath ?? Path.Combine(Path.GetDirectoryName(_settingsPath) ?? settingsDirectory, "settings.json");
        _packagedDefaultsPath = packagedDefaultsPath ?? Path.Combine(AppContext.BaseDirectory, "appsettings.defaults.json");
    }

    public AppSettings Load()
    {
        string? settingsPath = ResolveSettingsPathForLoad();
        try
        {
            JsonObject mergedSettings = new();
            bool hasSettingsData = false;

            if (TryLoadSettingsObject(_packagedDefaultsPath, out JsonObject packagedDefaults))
            {
                MergeInto(mergedSettings, packagedDefaults);
                hasSettingsData = true;
            }

            if (settingsPath is not null && TryLoadSettingsObject(settingsPath, out JsonObject persistedSettings))
            {
                MergeInto(mergedSettings, persistedSettings);
                hasSettingsData = true;
            }

            if (!hasSettingsData)
                return new AppSettings();

            return mergedSettings.Deserialize<AppSettings>(JsonOptions) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            Trace.TraceWarning("Failed to parse settings data. Default settings will be used. {0}", ex.Message);
            return new AppSettings();
        }
        catch (IOException ex)
        {
            Trace.TraceWarning("Failed to read settings data. Default settings will be used. {0}", ex.Message);
            return new AppSettings();
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.TraceWarning("Settings data is not accessible. Default settings will be used. {0}", ex.Message);
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

    private static void MergeInto(JsonObject target, JsonObject source)
    {
        foreach ((string key, JsonNode? value) in source)
        {
            target[key] = value?.DeepClone();
        }
    }

    private static bool TryLoadSettingsObject(string path, out JsonObject settingsObject)
    {
        settingsObject = null!;
        if (!File.Exists(path))
            return false;

        string json = File.ReadAllText(path);
        JsonNode? rootNode = JsonNode.Parse(json);
        if (rootNode is null)
            return false;

        JsonNode? nodeToDeserialize = rootNode is JsonObject rootObject &&
                                      rootObject.TryGetPropertyValue(AppSettingsSectionName, out JsonNode? sectionNode)
            ? sectionNode
            : rootNode;

        if (nodeToDeserialize is not JsonObject settingsRoot)
            return false;

        settingsObject = (JsonObject)settingsRoot.DeepClone();
        return true;
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
