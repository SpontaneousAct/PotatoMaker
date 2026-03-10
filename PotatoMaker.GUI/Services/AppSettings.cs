namespace PotatoMaker.GUI.Services;

/// <summary>
/// Stores persisted GUI preferences.
/// </summary>
public sealed record AppSettings
{
    public bool IsDarkMode { get; init; }

    public bool UseNvencEncoder { get; init; } = true;

    public string? LastOutputFolder { get; init; }
}
