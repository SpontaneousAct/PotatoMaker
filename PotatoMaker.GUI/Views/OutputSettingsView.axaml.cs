using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PotatoMaker.GUI.ViewModels;

namespace PotatoMaker.GUI.Views;

/// <summary>
/// Handles output-folder selection UI.
/// </summary>
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
        if (topLevel is null)
            return;

        IStorageFolder? startLocation = null;
        string? currentFolder = vm.OutputFolderPath;
        if (!string.IsNullOrWhiteSpace(currentFolder) && Directory.Exists(currentFolder))
            startLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(currentFolder);

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select output folder",
            AllowMultiple = false,
            SuggestedStartLocation = startLocation
        });

        string? path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
            vm.SetCustomOutputFolder(path);
    }

    private void OnDataContextChanged(object? sender, EventArgs e) => AttachPickerHandler();

    private void OnOutputFolderHostPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left || Vm?.OutputFolderPath is not { } folderPath)
            return;

        if (!Directory.Exists(folderPath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folderPath}\"",
                UseShellExecute = true
            });
            e.Handled = true;
        }
        catch
        {
            // Ignore shell launch failures so a bad click never crashes the UI.
        }
    }

    private void AttachPickerHandler()
    {
        if (Vm is not null)
            Vm.OutputFolderPickerRequested = OpenFolderPickerAsync;
    }
}
