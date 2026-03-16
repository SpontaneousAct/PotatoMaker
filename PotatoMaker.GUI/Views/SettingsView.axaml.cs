using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PotatoMaker.GUI.ViewModels;

namespace PotatoMaker.GUI.Views;

/// <summary>
/// Displays the app settings screen.
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        AttachPickerHandler();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (Vm?.RecentVideosDirectoryPickerRequested == OpenFolderPickerAsync)
            Vm.RecentVideosDirectoryPickerRequested = null;
    }

    private async void OpenFolderPickerAsync()
    {
        if (Vm is not { } vm)
            return;

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        IStorageFolder? startLocation = null;
        string currentFolder = vm.RecentVideosDirectory;
        if (!string.IsNullOrWhiteSpace(currentFolder) && Directory.Exists(currentFolder))
            startLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(currentFolder);

        IReadOnlyList<IStorageFolder> folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select recent videos folder",
            AllowMultiple = false,
            SuggestedStartLocation = startLocation
        });

        string? path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
            vm.RecentVideosDirectory = path;
    }

    private void OnDataContextChanged(object? sender, EventArgs e) => AttachPickerHandler();

    private void AttachPickerHandler()
    {
        if (Vm is not null)
            Vm.RecentVideosDirectoryPickerRequested = OpenFolderPickerAsync;
    }
}
