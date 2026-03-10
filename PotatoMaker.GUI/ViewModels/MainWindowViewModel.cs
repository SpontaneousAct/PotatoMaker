using CommunityToolkit.Mvvm.ComponentModel;
using PotatoMaker.GUI.Services;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Hosts shell-level UI state for the main window.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IThemeService _themeService;
    private readonly IAppSettingsCoordinator? _settingsCoordinator;
    private bool _isApplyingSettings;

    public MainWindowViewModel()
        : this(new EncodeWorkspaceViewModel(), new HelpModalViewModel(), new AvaloniaThemeService(), null)
    {
    }

    public MainWindowViewModel(
        EncodeWorkspaceViewModel workspace,
        HelpModalViewModel helpModal,
        IThemeService themeService,
        IAppSettingsCoordinator? settingsCoordinator)
    {
        Workspace = workspace;
        HelpModal = helpModal;
        VersionText = $"v{GetType().Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";
        _themeService = themeService;
        _settingsCoordinator = settingsCoordinator;

        ApplyInitialSettings();
    }

    public EncodeWorkspaceViewModel Workspace { get; }

    public HelpModalViewModel HelpModal { get; }

    public string VersionText { get; }

    [ObservableProperty]
    private bool _isDarkMode;

    partial void OnIsDarkModeChanged(bool value)
    {
        _themeService.ApplyTheme(value);

        if (_isApplyingSettings || _settingsCoordinator is null)
            return;

        _ = PersistThemePreferenceAsync();
    }

    public bool TryLoadStartupFiles(IEnumerable<string> startupArgs)
    {
        ArgumentNullException.ThrowIfNull(startupArgs);

        foreach (string startupArg in startupArgs)
        {
            if (string.IsNullOrWhiteSpace(startupArg))
                continue;

            if (Workspace.FileInput.SetFile(startupArg))
                return true;
        }

        return false;
    }

    private void ApplyInitialSettings()
    {
        bool initialIsDarkMode = _settingsCoordinator?.Current.IsDarkMode
            ?? _themeService.IsDarkModeEnabled();

        _isApplyingSettings = true;
        try
        {
            IsDarkMode = initialIsDarkMode;
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private async Task PersistThemePreferenceAsync()
    {
        try
        {
            await _settingsCoordinator!.UpdateAsync(settings => settings with
            {
                IsDarkMode = IsDarkMode
            }).ConfigureAwait(false);
        }
        catch
        {
            // Ignore persistence failures and keep the in-memory selection.
        }
    }
}
