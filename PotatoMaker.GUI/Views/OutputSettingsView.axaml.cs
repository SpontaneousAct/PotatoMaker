using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PotatoMaker.GUI.ViewModels;
using System;
using System.IO;
using System.Linq;

namespace PotatoMaker.GUI.Views;

public partial class OutputSettingsView : UserControl
{
    public OutputSettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private OutputSettingsViewModel? Vm => DataContext as OutputSettingsViewModel;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        AttachPickerHandler();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (Vm?.OutputFolderPickerRequested == OpenFolderPickerAsync)
            Vm.OutputFolderPickerRequested = null;
    }

    private async void OpenFolderPickerAsync()
    {
        if (Vm is not { } vm)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        IStorageFolder? startLocation = null;
        var currentFolder = vm.OutputFolderPath;
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
            vm.SetCustomOutputFolder(path);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        AttachPickerHandler();
    }

    private void AttachPickerHandler()
    {
        if (Vm is not null)
            Vm.OutputFolderPickerRequested = OpenFolderPickerAsync;
    }
}
