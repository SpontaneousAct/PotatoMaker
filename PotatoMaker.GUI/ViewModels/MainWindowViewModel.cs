using CommunityToolkit.Mvvm.ComponentModel;
using PotatoMaker.GUI.Services;
using System.ComponentModel;
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
        HelpModal.PropertyChanged += OnHelpModalPropertyChanged;

        ApplyInitialSettings();
        SyncOverlayState();
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

    public bool TryHandleGlobalShortcut(Key key, KeyModifiers modifiers)
    {
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
            _ => false
        };
    }

    public static bool IsGlobalShortcut(Key key, KeyModifiers modifiers) =>
        modifiers == KeyModifiers.None &&
        key is Key.Space or Key.A or Key.D;

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

    private void OnHelpModalPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HelpModalViewModel.IsOpen))
            SyncOverlayState();
    }

    private void SyncOverlayState()
    {
        Workspace.VideoPlayer.SuppressVideoSurface = HelpModal.IsOpen;
    }

    private static bool ExecuteShortcut(System.Windows.Input.ICommand command)
    {
        command.Execute(null);
        return true;
    }

    public void Dispose()
    {
        HelpModal.PropertyChanged -= OnHelpModalPropertyChanged;
        Workspace.Dispose();
    }
}
