using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PotatoMaker.GUI.ViewModels;

namespace PotatoMaker.GUI.Views;

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

    private async void OpenFilePickerAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a video file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Video files") { Patterns = ["*.mp4", "*.mkv", "*.avi", "*.mov", "*.webm", "*.wmv", "*.flv"] },
                new FilePickerFileType("All files") { Patterns = ["*.*"] }
            ]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
            Vm.SetFile(path);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var dropZone = this.FindControl<Border>("DropZone");

        bool hasFiles = e.Data.Contains(DataFormats.Files);
        e.DragEffects = hasFiles
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        if (dropZone is not null)
        {
            if (hasFiles)
                dropZone.Classes.Add("drag-over");
            else
                dropZone.Classes.Remove("drag-over");
        }
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e)
    {
        var dropZone = this.FindControl<Border>("DropZone");
        dropZone?.Classes.Remove("drag-over");
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var dropZone = this.FindControl<Border>("DropZone");
        dropZone?.Classes.Remove("drag-over");

#pragma warning disable CS0618
        var files = e.Data.GetFiles()?.ToList();
#pragma warning restore CS0618

        var path = files?.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
            Vm.SetFile(path);
    }
}
