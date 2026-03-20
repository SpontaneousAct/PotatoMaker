using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PotatoMaker.GUI.Services;
using Avalonia.Input;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

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
    private readonly IRecentVideoThumbnailService _recentVideoThumbnailService;
    private readonly IProcessedVideoTracker _processedVideoTracker;
    private readonly CompressionQueueViewModel _compressionQueue;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private bool _isApplyingSettings;
    private CancellationTokenSource? _recentVideosThumbnailCts;

    public MainWindowViewModel()
        : this(
            new EncodeWorkspaceViewModel(),
            new AvaloniaThemeService(),
            null,
            new RecentVideoDiscoveryService(),
            new RecentVideoThumbnailService(),
            DisabledProcessedVideoTracker.Instance,
            new CompressionQueueViewModel(),
            new DisabledAppUpdateService(),
            new AssemblyAppVersionService())
    {
    }

    internal MainWindowViewModel(
        EncodeWorkspaceViewModel workspace,
        IThemeService themeService,
        IAppSettingsCoordinator? settingsCoordinator,
        IRecentVideoDiscoveryService recentVideoDiscoveryService,
        IAppUpdateService? updateService,
        IAppVersionService? appVersionService = null)
        : this(
            workspace,
            themeService,
            settingsCoordinator,
            recentVideoDiscoveryService,
            DisabledRecentVideoThumbnailService.Instance,
            DisabledProcessedVideoTracker.Instance,
            null,
            updateService,
            appVersionService)
    {
    }

    public MainWindowViewModel(
        EncodeWorkspaceViewModel workspace,
        IThemeService themeService,
        IAppSettingsCoordinator? settingsCoordinator,
        IRecentVideoDiscoveryService recentVideoDiscoveryService,
        IRecentVideoThumbnailService recentVideoThumbnailService,
        IProcessedVideoTracker processedVideoTracker,
        IAppUpdateService? updateService,
        IAppVersionService? appVersionService = null)
        : this(
            workspace,
            themeService,
            settingsCoordinator,
            recentVideoDiscoveryService,
            recentVideoThumbnailService,
            processedVideoTracker,
            null,
            updateService,
            appVersionService)
    {
    }

    public MainWindowViewModel(
        EncodeWorkspaceViewModel workspace,
        IThemeService themeService,
        IAppSettingsCoordinator? settingsCoordinator,
        IRecentVideoDiscoveryService recentVideoDiscoveryService,
        IRecentVideoThumbnailService recentVideoThumbnailService,
        IProcessedVideoTracker processedVideoTracker,
        CompressionQueueViewModel? compressionQueue,
        IAppUpdateService? updateService,
        IAppVersionService? appVersionService = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(recentVideoDiscoveryService);
        ArgumentNullException.ThrowIfNull(recentVideoThumbnailService);
        ArgumentNullException.ThrowIfNull(processedVideoTracker);

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
        Queue = compressionQueue ?? new CompressionQueueViewModel();
        VersionText = (appVersionService ?? new AssemblyAppVersionService()).DisplayVersion;
        _themeService = themeService;
        _settingsCoordinator = settingsCoordinator;
        _recentVideoDiscoveryService = recentVideoDiscoveryService;
        _recentVideoThumbnailService = recentVideoThumbnailService;
        _processedVideoTracker = processedVideoTracker;
        _compressionQueue = Queue;
        _updateService = updateService ?? new DisabledAppUpdateService();
        RecentVideos.CollectionChanged += OnRecentVideosCollectionChanged;
        Workspace.OutputSettings.PropertyChanged += OnOutputSettingsChanged;
        _processedVideoTracker.ProcessedVideosChanged += OnProcessedVideosChanged;

        ApplyInitialSettings();
    }

    public EncodeWorkspaceViewModel Workspace { get; }

    public SettingsViewModel Settings { get; }

    public HelpViewModel Help { get; }

    public CompressionQueueViewModel Queue { get; }

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
    [NotifyPropertyChangedFor(nameof(IsQueueViewSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsViewSelected))]
    [NotifyPropertyChangedFor(nameof(IsHelpViewSelected))]
    private ShellViewKind _selectedView = ShellViewKind.Main;

    public object CurrentView => SelectedView switch
    {
        ShellViewKind.Queue => Queue,
        ShellViewKind.Settings => Settings,
        ShellViewKind.Help => Help,
        _ => Workspace
    };

    public bool IsMainViewSelected => SelectedView == ShellViewKind.Main;

    public bool IsQueueViewSelected => SelectedView == ShellViewKind.Queue;

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

    partial void OnIsRecentVideosPanelOpenChanged(bool value)
    {
        if (!value)
            CancelRecentVideoThumbnailLoading();
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
    private void ShowQueueView()
    {
        IsRecentVideosPanelOpen = false;
        SelectedView = ShellViewKind.Queue;
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

        IsRecentVideosPanelOpen = true;
        RefreshRecentVideos();
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
        CancelRecentVideoThumbnailLoading();

        IReadOnlyList<RecentVideoFile> recentVideos = _recentVideoDiscoveryService.GetRecentVideos(new RecentVideoQuery(
            RecentVideosDirectory,
            Workspace.OutputSettings.OutputNamePrefix,
            Workspace.OutputSettings.OutputNameSuffix));

        DisposeRecentVideoItems();
        RecentVideos.Clear();
        List<RecentVideoItemViewModel> recentItems = [];
        foreach (RecentVideoFile video in recentVideos)
        {
            bool isProcessed = _processedVideoTracker.IsProcessed(video.FullPath, video.LastModified);
            var item = new RecentVideoItemViewModel(
                video.FullPath,
                video.FileName,
                video.LastModified,
                isProcessed,
                OpenRecentVideo);
            RecentVideos.Add(item);
            recentItems.Add(item);
        }

        if (recentItems.Count > 0 && IsRecentVideosPanelOpen)
            _ = LoadRecentVideoThumbnailsAsync(recentItems, CreateRecentVideoThumbnailToken());
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
        CancelRecentVideoThumbnailLoading();
        DisposeRecentVideoItems();
        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();
        RecentVideos.CollectionChanged -= OnRecentVideosCollectionChanged;
        Workspace.OutputSettings.PropertyChanged -= OnOutputSettingsChanged;
        _processedVideoTracker.ProcessedVideosChanged -= OnProcessedVideosChanged;
        Workspace.Dispose();
        _compressionQueue.Dispose();
    }

    private void OnRecentVideosCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasRecentVideos));
        OnPropertyChanged(nameof(IsRecentVideosEmpty));
    }

    private void OnOutputSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsRecentVideosPanelOpen)
            return;

        if (e.PropertyName is nameof(OutputSettingsViewModel.OutputNamePrefix) or nameof(OutputSettingsViewModel.OutputNameSuffix))
            RefreshRecentVideos();
    }

    private void OnProcessedVideosChanged(object? sender, EventArgs e)
    {
        if (!IsRecentVideosPanelOpen)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (IsRecentVideosPanelOpen)
                RefreshRecentVideos();
        });
    }

    private CancellationToken CreateRecentVideoThumbnailToken()
    {
        CancelRecentVideoThumbnailLoading();
        _recentVideosThumbnailCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        return _recentVideosThumbnailCts.Token;
    }

    private async Task LoadRecentVideoThumbnailsAsync(
        IReadOnlyList<RecentVideoItemViewModel> recentItems,
        CancellationToken ct)
    {
        try
        {
            foreach (RecentVideoItemViewModel item in recentItems)
            {
                ct.ThrowIfCancellationRequested();

                var thumbnail = await _recentVideoThumbnailService
                    .GetThumbnailAsync(item.FullPath, ct)
                    .ConfigureAwait(false);

                if (thumbnail is null)
                    continue;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ct.IsCancellationRequested)
                        item.SetThumbnail(thumbnail);
                    else
                        thumbnail.Dispose();
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelRecentVideoThumbnailLoading()
    {
        if (_recentVideosThumbnailCts is null)
            return;

        try
        {
            _recentVideosThumbnailCts.Cancel();
        }
        catch
        {
        }
        finally
        {
            _recentVideosThumbnailCts.Dispose();
            _recentVideosThumbnailCts = null;
        }
    }

    private void DisposeRecentVideoItems()
    {
        foreach (RecentVideoItemViewModel item in RecentVideos)
            item.Dispose();
    }

    public enum UpdateIndicatorState
    {
        Hidden,
        Available,
        Downloading,
        PendingRestart
    }
}
