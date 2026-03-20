using Avalonia.Styling;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Supported appearance modes for the desktop app.
/// </summary>
public enum AppTheme
{
    Light,
    Dark,
    Sepia
}

/// <summary>
/// Custom Avalonia theme variants used by the app.
/// </summary>
public static class AppThemeVariants
{
    public static ThemeVariant Sepia { get; } = new(nameof(AppTheme.Sepia), ThemeVariant.Light);
}
