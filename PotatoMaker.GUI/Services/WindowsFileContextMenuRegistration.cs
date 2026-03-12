using Microsoft.Win32;
using Velopack.Locators;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Registers a per-user Explorer command for common video file types.
/// </summary>
internal static class WindowsFileContextMenuRegistration
{
    internal const string MenuDisplayName = "Compress with PotatoMaker";
    internal const string VerbName = "PotatoMaker.Compress";
    internal const string MainExecutableName = "PotatoMaker.GUI.exe";

    private static readonly string[] SupportedVideoExtensions =
    [
        ".mp4",
        ".m4v",
        ".mov",
        ".mkv",
        ".webm",
        ".avi",
        ".wmv",
        ".mpeg",
        ".mpg",
        ".ts",
        ".m2ts",
        ".flv",
        ".3gp"
    ];

    internal static IReadOnlyList<string> GetRegistryKeyPaths() =>
        SupportedVideoExtensions
            .Select(extension => $@"Software\Classes\SystemFileAssociations\{extension}\shell\{VerbName}")
            .ToArray();

    internal static string BuildCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return $"\"{executablePath}\" \"%1\"";
    }

    internal static void RegisterForInstalledApp()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string? executablePath = TryGetInstalledExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            return;

        foreach (string keyPath in GetRegistryKeyPaths())
        {
            using RegistryKey? menuKey = Registry.CurrentUser.CreateSubKey(keyPath);
            if (menuKey is null)
                continue;

            menuKey.SetValue(null, MenuDisplayName, RegistryValueKind.String);
            menuKey.SetValue("Icon", executablePath, RegistryValueKind.String);
            menuKey.SetValue("MultiSelectModel", "Single", RegistryValueKind.String);

            using RegistryKey? commandKey = menuKey.CreateSubKey("command");
            commandKey?.SetValue(null, BuildCommand(executablePath), RegistryValueKind.String);
        }
    }

    internal static void RemoveForInstalledApp()
    {
        if (!OperatingSystem.IsWindows())
            return;

        foreach (string keyPath in GetRegistryKeyPaths())
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        }
    }

    private static string? TryGetInstalledExecutablePath()
    {
        if (VelopackLocator.IsCurrentSet && VelopackLocator.Current is { } locator)
        {
            if (!string.IsNullOrWhiteSpace(locator.AppContentDir))
            {
                string installedExecutablePath = Path.Combine(locator.AppContentDir, MainExecutableName);
                if (File.Exists(installedExecutablePath))
                    return installedExecutablePath;
            }
        }

        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath) &&
            string.Equals(Path.GetFileName(Environment.ProcessPath), MainExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            return Environment.ProcessPath;
        }

        return null;
    }
}
