using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PotatoMaker.GUI.Services;
using Avalonia.Input;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Hosts shell-level UI state for the main window.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IThemeService _themeService;
    private readonly IAppSettingsCoordinator? _settingsCoordinator;
    private readonly IAppUpdateService _updateService;
    private readonly IRecentVideoDiscoveryService _recentVideoDiscoveryService;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private bool _isApplyingSettings;

    public MainWindowViewModel()
        : this(
            new EncodeWorkspaceViewModel(),
            new AvaloniaThemeService(),
            null,
            new RecentVideoDiscoveryService(),
            new DisabledAppUpdateService(),
            new AssemblyAppVersionService())
    {
    }

    public MainWindowViewModel(
        EncodeWorkspaceViewModel workspace,
        IThemeService themeService,
        IAppSettingsCoordinator? settingsCoordinator,
        IRecentVideoDiscoveryService recentVideoDiscoveryService,
        IAppUpdateService? updateService,
        IAppVersionService? appVersionService = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(recentVideoDiscoveryService);

        Workspace = workspace;
        Settings = new SettingsViewModel(
            workspace.OutputSettings,
            () => IsDarkMode,
            value => IsDarkMode = value,
            () => IsUpdateSectionVisible,
            () => SettingsUpdateTitle,
            () => SettingsUpdateDescription,
            () => SettingsUpdateActionText,
            () => RecentVideosDirectory,
            value => RecentVideosDirectory = value,
            ApplyUpdateCommand);
        Help = new HelpViewModel();
        VersionText = (appVersionService ?? new AssemblyAppVersionService()).DisplayVersion;
        _themeService = themeService;
        _settingsCoordinator = settingsCoordinator;
        _recentVideoDiscoveryService = recentVideoDiscoveryService;
        _updateService = updateService ?? new DisabledAppUpdateService();
        RecentVideos.CollectionChanged += OnRecentVideosCollectionChanged;

        ApplyInitialSettings();
    }

    public EncodeWorkspaceViewModel Workspace { get; }

    public SettingsViewModel Settings { get; }

    public HelpViewModel Help { get; }

    public string VersionText { get; }

    public ObservableCollection<RecentVideoItemViewModel> RecentVideos { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateBadgeVisible))]
    [NotifyPropertyChangedFor(nameof(IsUpdateSectionVisible))]
    [NotifyPropertyChangedFor(nameof(SettingsUpdateTitle))]
    [NotifyPropertyChangedFor(nameof(SettingsUpdateDescription))]
    [NotifyPropertyChangedFor(nameof(SettingsUpdateActionText))]
    [NotifyCanExecuteChangedFor(nameof(ApplyUpdateCommand))]
    private UpdateIndicatorState _updateButtonState;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettingsUpdateTitle))]
    [NotifyPropertyChangedFor(nameof(SettingsUpdateDescription))]
    private string? _availableUpdateVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettingsUpdateDescription))]
    private int _updateProgressPercent;

    [ObservableProperty]
    private bool _isDarkMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecentVideos))]
    [NotifyPropertyChangedFor(nameof(IsRecentVideosEmpty))]
    private bool _isRecentVideosPanelOpen;

    [ObservableProperty]
    private string _recentVideosDirectory = AppSettings.DefaultRecentVideosDirectory;

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

    public bool HasRecentVideos => RecentVideos.Count > 0;

    public bool IsRecentVideosEmpty => RecentVideos.Count == 0;

    public bool IsUpdateBadgeVisible => UpdateButtonState is UpdateIndicatorState.Available or UpdateIndicatorState.PendingRestart;

    public bool IsUpdateSectionVisible => UpdateButtonState != UpdateIndicatorState.Hidden;

    public string SettingsUpdateTitle => UpdateButtonState switch
    {
        UpdateIndicatorState.Available when !string.IsNullOrWhiteSpace(AvailableUpdateVersion) =>
            $"Update available: v{AvailableUpdateVersion}",
        UpdateIndicatorState.Available =>
            "Update available",
        UpdateIndicatorState.Downloading when !string.IsNullOrWhiteSpace(AvailableUpdateVersion) =>
            $"Downloading v{AvailableUpdateVersion}",
        UpdateIndicatorState.Downloading =>
            "Downloading update",
        UpdateIndicatorState.PendingRestart when !string.IsNullOrWhiteSpace(AvailableUpdateVersion) =>
            $"Update ready: v{AvailableUpdateVersion}",
        UpdateIndicatorState.PendingRestart =>
            "Update ready",
        _ => string.Empty
    };

    public string SettingsUpdateDescription => UpdateButtonState switch
    {
        UpdateIndicatorState.Available when !string.IsNullOrWhiteSpace(AvailableUpdateVersion) =>
            $"PotatoMaker {AvailableUpdateVersion} is ready to download.",
        UpdateIndicatorState.Available =>
            "A newer packaged version of PotatoMaker is available.",
        UpdateIndicatorState.Downloading when UpdateProgressPercent > 0 =>
            $"Downloading the update package now. Progress: {UpdateProgressPercent}%.",
        UpdateIndicatorState.Downloading =>
            "Downloading the update package now.",
        UpdateIndicatorState.PendingRestart when !string.IsNullOrWhiteSpace(AvailableUpdateVersion) =>
            $"PotatoMaker {AvailableUpdateVersion} is downloaded and ready.",
        UpdateIndicatorState.PendingRestart =>
            "The update is downloaded and ready.",
        _ => string.Empty
    };

    public string SettingsUpdateActionText => UpdateButtonState switch
    {
        UpdateIndicatorState.Downloading => "Downloading...",
        UpdateIndicatorState.PendingRestart => "Apply update",
        _ => "Download update"
    };

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

    public bool OpenExternalFiles(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string[] candidates = args
            .Where(argument => !string.IsNullOrWhiteSpace(argument))
            .ToArray();

        bool loaded = TryLoadStartupFiles(candidates);
        if (loaded || candidates.Length > 0)
        {
            IsRecentVideosPanelOpen = false;
            SelectedView = ShellViewKind.Main;
        }

        return loaded;
    }

    public async Task InitializeAsync()
    {
        try
        {
            AppUpdateSnapshot currentSnapshot = await _updateService.GetCurrentStateAsync(_lifetimeCts.Token);
            ApplyUpdateSnapshot(currentSnapshot);

            if (!_updateService.ShouldCheckOnStartup ||
                !currentSnapshot.IsConfigured ||
                !currentSnapshot.CanSelfUpdate ||
                currentSnapshot.IsUpdatePendingRestart)
            {
                return;
            }

            if (_updateService.StartupCheckDelay > TimeSpan.Zero)
                await Task.Delay(_updateService.StartupCheckDelay, _lifetimeCts.Token);

            AppUpdateSnapshot latestSnapshot = await _updateService.CheckForUpdatesAsync(_lifetimeCts.Token);
            ApplyUpdateSnapshot(latestSnapshot);
        }
        catch (OperationCanceledException)
        {
        }
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
            Key.Q when Workspace.VideoPlayer.SeekBackwardTenSecondsCommand.CanExecute(null) =>
                ExecuteShortcut(Workspace.VideoPlayer.SeekBackwardTenSecondsCommand),
            Key.A when Workspace.VideoPlayer.SetTrimStartCommand.CanExecute(null) =>
                ExecuteShortcut(Workspace.VideoPlayer.SetTrimStartCommand),
            Key.D when Workspace.VideoPlayer.SetTrimEndCommand.CanExecute(null) =>
                ExecuteShortcut(Workspace.VideoPlayer.SetTrimEndCommand),
            Key.E when Workspace.VideoPlayer.SeekForwardTenSecondsCommand.CanExecute(null) =>
                ExecuteShortcut(Workspace.VideoPlayer.SeekForwardTenSecondsCommand),
            _ => false
        };
    }

    partial void OnRecentVideosDirectoryChanged(string value)
    {
        Settings.NotifyRecentVideosDirectoryChanged();

        if (IsRecentVideosPanelOpen)
            RefreshRecentVideos();

        if (_isApplyingSettings || _settingsCoordinator is null)
            return;

        _ = PersistRecentVideosDirectoryAsync();
    }

    public static bool IsGlobalShortcut(Key key, KeyModifiers modifiers) =>
        modifiers == KeyModifiers.None &&
        key is Key.Space or Key.Q or Key.A or Key.D or Key.E;

    [RelayCommand]
    private void ShowMainView()
    {
        IsRecentVideosPanelOpen = false;
        SelectedView = ShellViewKind.Main;
    }

    [RelayCommand]
    private void ShowSettingsView()
    {
        IsRecentVideosPanelOpen = false;
        SelectedView = ShellViewKind.Settings;
    }

    [RelayCommand]
    private void ShowHelpView()
    {
        IsRecentVideosPanelOpen = false;
        SelectedView = ShellViewKind.Help;
    }

    [RelayCommand]
    private void ToggleRecentVideosPanel()
    {
        if (IsRecentVideosPanelOpen)
        {
            IsRecentVideosPanelOpen = false;
            return;
        }

        RefreshRecentVideos();
        IsRecentVideosPanelOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanApplyUpdate), AllowConcurrentExecutions = false)]
    private async Task ApplyUpdateAsync()
    {
        UpdateIndicatorState previousState = UpdateButtonState;

        try
        {
            if (UpdateButtonState == UpdateIndicatorState.PendingRestart)
            {
                _updateService.ApplyPendingUpdateAndRestart();
                return;
            }

            if (UpdateButtonState == UpdateIndicatorState.Available)
            {
                UpdateProgressPercent = 0;
                UpdateButtonState = UpdateIndicatorState.Downloading;
            }

            await _updateService.ApplyUpdateAsync(
                progress => Dispatcher.UIThread.Post(() => UpdateProgressPercent = progress),
                _lifetimeCts.Token);

            AppUpdateSnapshot currentSnapshot = await _updateService.GetCurrentStateAsync(_lifetimeCts.Token);
            ApplyUpdateSnapshot(currentSnapshot);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            UpdateButtonState = previousState;
        }
    }

    partial void OnSelectedViewChanged(ShellViewKind value)
    {
        if (value != ShellViewKind.Main)
            Workspace.VideoPlayer.PausePlaybackIfPlaying();

        OnPropertyChanged(nameof(CurrentView));
    }

    partial void OnUpdateButtonStateChanged(UpdateIndicatorState value) => Settings.NotifyUpdateChanged();

    partial void OnAvailableUpdateVersionChanged(string? value) => Settings.NotifyUpdateChanged();

    partial void OnUpdateProgressPercentChanged(int value) => Settings.NotifyUpdateChanged();

    private void ApplyInitialSettings()
    {
        bool initialIsDarkMode = _settingsCoordinator?.Current.IsDarkMode
            ?? _themeService.IsDarkModeEnabled();
        string initialRecentVideosDirectory = NormalizeRecentVideosDirectory(_settingsCoordinator?.Current.RecentVideosDirectory);

        _isApplyingSettings = true;
        try
        {
            IsDarkMode = initialIsDarkMode;
            RecentVideosDirectory = initialRecentVideosDirectory;
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

    private async Task PersistRecentVideosDirectoryAsync()
    {
        try
        {
            await _settingsCoordinator!.UpdateAsync(settings => settings with
            {
                RecentVideosDirectory = NormalizeRecentVideosDirectory(RecentVideosDirectory)
            }).ConfigureAwait(false);
        }
        catch
        {
            // Ignore persistence failures and keep the in-memory value.
        }
    }

    private static bool ExecuteShortcut(System.Windows.Input.ICommand command)
    {
        command.Execute(null);
        return true;
    }

    private bool CanApplyUpdate() =>
        UpdateButtonState is UpdateIndicatorState.Available or UpdateIndicatorState.PendingRestart;

    private void RefreshRecentVideos()
    {
        IReadOnlyList<RecentVideoFile> recentVideos = _recentVideoDiscoveryService.GetRecentVideos(RecentVideosDirectory);

        RecentVideos.Clear();
        foreach (RecentVideoFile video in recentVideos)
        {
            RecentVideos.Add(new RecentVideoItemViewModel(video.FullPath, video.FileName, video.LastModified, OpenRecentVideo));
        }
    }

    private void OpenRecentVideo(string path)
    {
        if (!Workspace.FileInput.SetFile(path))
        {
            RefreshRecentVideos();
            return;
        }

        SelectedView = ShellViewKind.Main;
        IsRecentVideosPanelOpen = false;
    }

    private static string NormalizeRecentVideosDirectory(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? AppSettings.DefaultRecentVideosDirectory
            : value.Trim();

    private void ApplyUpdateSnapshot(AppUpdateSnapshot snapshot)
    {
        AvailableUpdateVersion = snapshot.AvailableVersion;
        UpdateProgressPercent = 0;

        UpdateButtonState = snapshot switch
        {
            { IsUpdatePendingRestart: true } => UpdateIndicatorState.PendingRestart,
            { IsUpdateAvailable: true } => UpdateIndicatorState.Available,
            _ => UpdateIndicatorState.Hidden
        };
    }

    public void Dispose()
    {
        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();
        RecentVideos.CollectionChanged -= OnRecentVideosCollectionChanged;
        Workspace.Dispose();
    }

    private void OnRecentVideosCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasRecentVideos));
        OnPropertyChanged(nameof(IsRecentVideosEmpty));
    }

    public enum UpdateIndicatorState
    {
        Hidden,
        Available,
        Downloading,
        PendingRestart
    }
}
