using System.Windows.Input;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Presents application and encoding preferences in the shell settings screen.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly Func<bool> _getIsDarkMode;
    private readonly Action<bool> _setIsDarkMode;
    private readonly Func<bool> _getIsUpdateSectionVisible;
    private readonly Func<string> _getUpdateTitle;
    private readonly Func<string> _getUpdateDescription;
    private readonly Func<string> _getUpdateActionText;

    public SettingsViewModel(
        OutputSettingsViewModel outputSettings,
        Func<bool> getIsDarkMode,
        Action<bool> setIsDarkMode,
        Func<bool> getIsUpdateSectionVisible,
        Func<string> getUpdateTitle,
        Func<string> getUpdateDescription,
        Func<string> getUpdateActionText,
        ICommand applyUpdateCommand)
    {
        ArgumentNullException.ThrowIfNull(outputSettings);
        ArgumentNullException.ThrowIfNull(getIsDarkMode);
        ArgumentNullException.ThrowIfNull(setIsDarkMode);
        ArgumentNullException.ThrowIfNull(getIsUpdateSectionVisible);
        ArgumentNullException.ThrowIfNull(getUpdateTitle);
        ArgumentNullException.ThrowIfNull(getUpdateDescription);
        ArgumentNullException.ThrowIfNull(getUpdateActionText);
        ArgumentNullException.ThrowIfNull(applyUpdateCommand);

        OutputSettings = outputSettings;
        _getIsDarkMode = getIsDarkMode;
        _setIsDarkMode = setIsDarkMode;
        _getIsUpdateSectionVisible = getIsUpdateSectionVisible;
        _getUpdateTitle = getUpdateTitle;
        _getUpdateDescription = getUpdateDescription;
        _getUpdateActionText = getUpdateActionText;
        ApplyUpdateCommand = applyUpdateCommand;
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

    public bool IsUpdateSectionVisible => _getIsUpdateSectionVisible();

    public string UpdateTitle => _getUpdateTitle();

    public string UpdateDescription => _getUpdateDescription();

    public string UpdateActionText => _getUpdateActionText();

    public ICommand ApplyUpdateCommand { get; }

    public void NotifyThemeChanged() => OnPropertyChanged(nameof(IsDarkMode));

    public void NotifyUpdateChanged()
    {
        OnPropertyChanged(nameof(IsUpdateSectionVisible));
        OnPropertyChanged(nameof(UpdateTitle));
        OnPropertyChanged(nameof(UpdateDescription));
        OnPropertyChanged(nameof(UpdateActionText));
    }
}
