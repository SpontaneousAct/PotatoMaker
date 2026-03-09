namespace PotatoMaker.GUI.Services;

/// <summary>
/// Persisted GUI preferences.
/// </summary>
public sealed class AppSettings
{
    public bool IsDarkMode { get; set; }

    /// <summary>
    /// Preferred encoder for the desktop app.
    /// True -> NVENC AV1, False -> CPU (libsvtav1).
    /// </summary>
    public bool? UseNvencEncoder { get; set; }

    /// <summary>
    /// Legacy setting retained for backward-compatible migration.
    /// </summary>
    public bool UseCpuEncoder { get; set; } = true;

    public string? LastOutputFolder { get; set; }
}
