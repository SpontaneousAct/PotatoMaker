using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PotatoMaker.GUI.ViewModels;
using System.IO;
using System.Linq;

namespace PotatoMaker.GUI.Views;

public partial class OutputSettingsView : UserControl
{
    public OutputSettingsView()
    {
        InitializeComponent();
    }

    private OutputSettingsViewModel Vm => (OutputSettingsViewModel)DataContext!;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Vm.OutputFolderPickerRequested = OpenFolderPickerAsync;
    }

    private async void OpenFolderPickerAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        IStorageFolder? startLocation = null;
        var currentFolder = Vm.OutputFolderPath;
        if (!string.IsNullOrWhiteSpace(currentFolder) && Directory.Exists(currentFolder))
            startLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(currentFolder);

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select output folder",
            AllowMultiple = false,
            SuggestedStartLocation = startLocation
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
            Vm.SetCustomOutputFolder(path);
    }
}
