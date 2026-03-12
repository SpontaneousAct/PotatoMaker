using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PotatoMaker.GUI.Services;

public enum UpdateSourceMode
{
    Disabled,
    GitHub,
    File
}

/// <summary>
/// Configures where the desktop app checks for packaged Velopack releases.
/// </summary>
public sealed record UpdateSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter<UpdateSourceMode>))]
    public UpdateSourceMode Mode { get; init; } = UpdateSourceMode.Disabled;

    public string? GitHubRepositoryUrl { get; init; }

    public string? GitHubAccessToken { get; init; }

    public bool AllowPrerelease { get; init; }

    public string? LocalReleasePath { get; init; }

    public string? ExplicitChannel { get; init; } = "win-x64";

    public bool CheckOnStartup { get; init; } = true;

    public int StartupDelaySeconds { get; init; } = 5;
}

/// <summary>
/// Describes the app's current update state.
/// </summary>
public sealed record AppUpdateSnapshot(
    bool IsConfigured,
    bool CanSelfUpdate,
    bool IsUpdateAvailable,
    bool IsUpdatePendingRestart,
    string? AvailableVersion = null,
    string? ReleaseNotesMarkdown = null)
{
    public static AppUpdateSnapshot Disabled { get; } = new(
        IsConfigured: false,
        CanSelfUpdate: false,
        IsUpdateAvailable: false,
        IsUpdatePendingRestart: false);
}

public interface IUpdateSettingsProvider
{
    UpdateSettings Load();
}

/// <summary>
/// Loads updater settings from packaged defaults, user overrides, and environment variables.
/// </summary>
public sealed class JsonUpdateSettingsProvider : IUpdateSettingsProvider
{
    private const string UpdateSettingsSectionName = "UpdateSettings";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string _settingsPath;
    private readonly string _legacySettingsPath;
    private readonly string _packagedDefaultsPath;

    public JsonUpdateSettingsProvider(
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

    public UpdateSettings Load()
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

            UpdateSettings settings = hasSettingsData
                ? mergedSettings.Deserialize<UpdateSettings>(JsonOptions) ?? new UpdateSettings()
                : new UpdateSettings();

            return ApplyEnvironmentOverrides(settings);
        }
        catch (JsonException ex)
        {
            Trace.TraceWarning("Failed to parse update settings. Updates will be disabled. {0}", ex.Message);
            return ApplyEnvironmentOverrides(new UpdateSettings());
        }
        catch (IOException ex)
        {
            Trace.TraceWarning("Failed to read update settings. Updates will be disabled. {0}", ex.Message);
            return ApplyEnvironmentOverrides(new UpdateSettings());
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.TraceWarning("Update settings are not accessible. Updates will be disabled. {0}", ex.Message);
            return ApplyEnvironmentOverrides(new UpdateSettings());
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

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static UpdateSettings ApplyEnvironmentOverrides(UpdateSettings settings)
    {
        if (TryGetEnvironmentValue("POTATOMAKER_UPDATE_MODE", out string? updateMode) &&
            Enum.TryParse(updateMode, ignoreCase: true, out UpdateSourceMode parsedMode))
        {
            settings = settings with { Mode = parsedMode };
        }

        if (TryGetEnvironmentValue("POTATOMAKER_UPDATE_REPO", out string? repoUrl))
            settings = settings with { GitHubRepositoryUrl = repoUrl };

        if (TryGetEnvironmentValue("POTATOMAKER_UPDATE_GITHUB_TOKEN", out string? token))
            settings = settings with { GitHubAccessToken = token };

        if (TryGetEnvironmentValue("POTATOMAKER_UPDATE_PATH", out string? localPath))
            settings = settings with { LocalReleasePath = localPath };

        if (TryGetEnvironmentValue("POTATOMAKER_UPDATE_CHANNEL", out string? channel))
            settings = settings with { ExplicitChannel = channel };

        if (TryGetEnvironmentValue("POTATOMAKER_UPDATE_PRERELEASE", out string? prerelease) &&
            bool.TryParse(prerelease, out bool allowPrerelease))
        {
            settings = settings with { AllowPrerelease = allowPrerelease };
        }

        if (TryGetEnvironmentValue("POTATOMAKER_UPDATE_CHECK_ON_STARTUP", out string? checkOnStartup) &&
            bool.TryParse(checkOnStartup, out bool parsedCheckOnStartup))
        {
            settings = settings with { CheckOnStartup = parsedCheckOnStartup };
        }

        if (TryGetEnvironmentValue("POTATOMAKER_UPDATE_DELAY_SECONDS", out string? delaySeconds) &&
            int.TryParse(delaySeconds, out int parsedDelaySeconds) &&
            parsedDelaySeconds >= 0)
        {
            settings = settings with { StartupDelaySeconds = parsedDelaySeconds };
        }

        return settings;
    }

    private static bool TryGetEnvironmentValue(string key, out string? value)
    {
        value = Environment.GetEnvironmentVariable(key)?.Trim();
        return !string.IsNullOrWhiteSpace(value);
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
                                      rootObject.TryGetPropertyValue(UpdateSettingsSectionName, out JsonNode? sectionNode)
            ? sectionNode
            : rootNode;

        if (nodeToDeserialize is not JsonObject settingsRoot)
            return false;

        settingsObject = (JsonObject)settingsRoot.DeepClone();
        return true;
    }
}
