using Avalonia;
using Avalonia.Styling;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Applies the app theme.
/// </summary>
public interface IThemeService
{
    bool IsDarkModeEnabled();

    void ApplyTheme(bool isDarkMode);
}

/// <summary>
/// Bridges theme changes to the Avalonia application.
/// </summary>
public sealed class AvaloniaThemeService : IThemeService
{
    public bool IsDarkModeEnabled() => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    public void ApplyTheme(bool isDarkMode)
    {
        if (Application.Current is null)
            return;

        Application.Current.RequestedThemeVariant = isDarkMode
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
    }
}
