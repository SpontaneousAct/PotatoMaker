using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PotatoMaker.GUI.Services;
using Avalonia.Input;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Hosts shell-level UI state for the main window.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IThemeService _themeService;
    private readonly IAppSettingsCoordinator? _settingsCoordinator;
    private bool _isApplyingSettings;

    public MainWindowViewModel()
        : this(new EncodeWorkspaceViewModel(), new AvaloniaThemeService(), null)
    {
    }

    public MainWindowViewModel(
        EncodeWorkspaceViewModel workspace,
        IThemeService themeService,
        IAppSettingsCoordinator? settingsCoordinator)
    {
        Workspace = workspace;
        Settings = new SettingsViewModel(workspace.OutputSettings, () => IsDarkMode, value => IsDarkMode = value);
        Help = new HelpViewModel();
        VersionText = $"v{GetType().Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";
        _themeService = themeService;
        _settingsCoordinator = settingsCoordinator;

        ApplyInitialSettings();
    }

    public EncodeWorkspaceViewModel Workspace { get; }

    public SettingsViewModel Settings { get; }

    public HelpViewModel Help { get; }

    public string VersionText { get; }

    [ObservableProperty]
    private bool _isDarkMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentView))]
    [NotifyPropertyChangedFor(nameof(IsMainViewSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsViewSelected))]
    [NotifyPropertyChangedFor(nameof(IsHelpViewSelected))]
    private ShellViewKind _selectedView = ShellViewKind.Main;

    public object CurrentView => SelectedView switch
    {
        ShellViewKind.Settings => Settings,
        ShellViewKind.Help => Help,
        _ => Workspace
    };

    public bool IsMainViewSelected => SelectedView == ShellViewKind.Main;

    public bool IsSettingsViewSelected => SelectedView == ShellViewKind.Settings;

    public bool IsHelpViewSelected => SelectedView == ShellViewKind.Help;

    partial void OnIsDarkModeChanged(bool value)
    {
        _themeService.ApplyTheme(value);
        Settings.NotifyThemeChanged();

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

    public bool TryHandleGlobalShortcut(Key key, KeyModifiers modifiers)
    {
        if (SelectedView != ShellViewKind.Main)
            return false;

        if (!IsGlobalShortcut(key, modifiers))
            return false;

        return key switch
        {
            Key.Space when Workspace.VideoPlayer.TogglePlaybackCommand.CanExecute(null) =>
                ExecuteShortcut(Workspace.VideoPlayer.TogglePlaybackCommand),
            Key.A when Workspace.VideoPlayer.SetTrimStartCommand.CanExecute(null) =>
                ExecuteShortcut(Workspace.VideoPlayer.SetTrimStartCommand),
            Key.D when Workspace.VideoPlayer.SetTrimEndCommand.CanExecute(null) =>
                ExecuteShortcut(Workspace.VideoPlayer.SetTrimEndCommand),
            Key.W when Workspace.VideoPlayer.StepForwardFrameCommand.CanExecute(null) =>
                ExecuteShortcut(Workspace.VideoPlayer.StepForwardFrameCommand),
            Key.S when Workspace.VideoPlayer.StepBackwardFrameCommand.CanExecute(null) =>
                ExecuteShortcut(Workspace.VideoPlayer.StepBackwardFrameCommand),
            _ => false
        };
    }

    public static bool IsGlobalShortcut(Key key, KeyModifiers modifiers) =>
        modifiers == KeyModifiers.None &&
        key is Key.Space or Key.A or Key.D or Key.W or Key.S;

    public static bool IsRepeatableGlobalShortcut(Key key) =>
        key is Key.W or Key.S;

    [RelayCommand]
    private void ShowMainView() => SelectedView = ShellViewKind.Main;

    [RelayCommand]
    private void ShowSettingsView() => SelectedView = ShellViewKind.Settings;

    [RelayCommand]
    private void ShowHelpView() => SelectedView = ShellViewKind.Help;

    partial void OnSelectedViewChanged(ShellViewKind value)
    {
        if (value != ShellViewKind.Main)
            Workspace.VideoPlayer.PausePlaybackIfPlaying();

        OnPropertyChanged(nameof(CurrentView));
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

    private static bool ExecuteShortcut(System.Windows.Input.ICommand command)
    {
        command.Execute(null);
        return true;
    }

    public void Dispose()
    {
        Workspace.Dispose();
    }
}
