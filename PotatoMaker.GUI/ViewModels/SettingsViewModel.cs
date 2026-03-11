namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Presents application and encoding preferences in the shell settings screen.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly Func<bool> _getIsDarkMode;
    private readonly Action<bool> _setIsDarkMode;

    public SettingsViewModel(
        OutputSettingsViewModel outputSettings,
        Func<bool> getIsDarkMode,
        Action<bool> setIsDarkMode)
    {
        ArgumentNullException.ThrowIfNull(outputSettings);
        ArgumentNullException.ThrowIfNull(getIsDarkMode);
        ArgumentNullException.ThrowIfNull(setIsDarkMode);

        OutputSettings = outputSettings;
        _getIsDarkMode = getIsDarkMode;
        _setIsDarkMode = setIsDarkMode;
    }

    public OutputSettingsViewModel OutputSettings { get; }

    public bool IsDarkMode
    {
        get => _getIsDarkMode();
        set
        {
            if (value == _getIsDarkMode())
                return;

            _setIsDarkMode(value);
            OnPropertyChanged();
        }
    }

    public void NotifyThemeChanged() => OnPropertyChanged(nameof(IsDarkMode));
}
