using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PotatoMaker.Core;
using PotatoMaker.GUI.ViewModels;

namespace PotatoMaker.GUI.Views;

/// <summary>
/// Handles file picker and drag-drop interactions for source files.
/// </summary>
public partial class FileInputView : UserControl
{
    public FileInputView()
    {
        InitializeComponent();

        var dropZone = this.FindControl<Border>("DropZone")!;
        dropZone.AddHandler(DragDrop.DropEvent, OnDrop);
        dropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        dropZone.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
    }

    private FileInputViewModel Vm => (FileInputViewModel)DataContext!;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Vm.FilePickerRequested = OpenFilePickerAsync;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (Vm.FilePickerRequested == OpenFilePickerAsync)
            Vm.FilePickerRequested = null;
    }

    private async void OpenFilePickerAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a video file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Supported video files")
                {
                    Patterns = InputMediaSupport.FileDialogPatterns.ToArray()
                }
            ]
        });

        string? path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
            Vm.SetFile(path);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        Border? dropZone = this.FindControl<Border>("DropZone");

        bool canSelectFile = Vm.CanSelectFile;
        bool hasSupportedFile = canSelectFile &&
            TryGetSingleLocalFilePath(e.DataTransfer, out string? path) &&
            InputMediaSupport.IsSupportedPath(path);

        e.DragEffects = hasSupportedFile
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        if (dropZone is not null)
        {
            if (hasSupportedFile)
                dropZone.Classes.Add("drag-over");
            else
                dropZone.Classes.Remove("drag-over");
        }
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e)
    {
        Border? dropZone = this.FindControl<Border>("DropZone");
        dropZone?.Classes.Remove("drag-over");
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        Border? dropZone = this.FindControl<Border>("DropZone");
        dropZone?.Classes.Remove("drag-over");

        if (!Vm.CanSelectFile)
        {
            Vm.RejectFileSelection(FileInputViewModel.LockedSelectionMessage);
            return;
        }

        if (!TryGetSingleLocalFilePath(e.DataTransfer, out string? path) || path is null)
        {
            Vm.RejectFileSelection("Drop exactly one supported video file.");
            return;
        }

        Vm.SetFile(path);
    }

    private static bool TryGetSingleLocalFilePath(IDataTransfer dataTransfer, out string? path)
    {
        path = null;

        if (!dataTransfer.Contains(DataFormat.File))
            return false;

        var files = dataTransfer.TryGetFiles()?.ToList();
        if (files is null || files.Count != 1)
            return false;

        path = files[0].TryGetLocalPath();
        return path is not null;
    }
}
