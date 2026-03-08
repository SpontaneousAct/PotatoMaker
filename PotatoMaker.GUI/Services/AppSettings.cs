namespace PotatoMaker.GUI.Services;

/// <summary>
/// Persisted GUI preferences.
/// </summary>
public sealed class AppSettings
{
    public bool IsDarkMode { get; set; }

    public bool UseCpuEncoder { get; set; }

    public string? LastOutputFolder { get; set; }
}
