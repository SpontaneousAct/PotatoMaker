using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PotatoMaker.GUI.ViewModels;

public partial class FileInputViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile))]
    private string? _inputFilePath;

    [ObservableProperty]
    private string? _fileName;

    public bool HasFile => !string.IsNullOrEmpty(InputFilePath);

    /// <summary>
    /// Set by the View to open the native file picker dialog.
    /// </summary>
    public Action? FilePickerRequested { get; set; }

    /// <summary>
    /// Raised after a valid file path has been set so the parent can coordinate probing.
    /// </summary>
    public event Action<string>? FileSelected;
    public event Action? FileCleared;

    [RelayCommand]
    private void SelectFile()
    {
        FilePickerRequested?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(HasFile))]
    private void ClearFile()
    {
        Clear();
        FileCleared?.Invoke();
    }

    partial void OnInputFilePathChanged(string? value)
    {
        ClearFileCommand.NotifyCanExecuteChanged();
    }

    public void SetFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        string fullPath = Path.GetFullPath(path);
        InputFilePath = fullPath;
        FileName = Path.GetFileName(fullPath);
        FileSelected?.Invoke(fullPath);
    }

    public void Clear()
    {
        InputFilePath = null;
        FileName = null;
    }
}
