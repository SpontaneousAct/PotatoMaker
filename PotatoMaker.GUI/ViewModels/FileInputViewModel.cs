using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PotatoMaker.Core;

namespace PotatoMaker.GUI.ViewModels;

public partial class FileInputViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile))]
    private string? _inputFilePath;

    [ObservableProperty]
    private string? _fileName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string? _validationMessage;

    public bool HasFile => !string.IsNullOrEmpty(InputFilePath);
    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);

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

    public bool SetFile(string path)
    {
        if (!InputMediaSupport.TryValidatePath(path, out string errorMessage))
        {
            ValidationMessage = errorMessage;
            return false;
        }

        string fullPath = Path.GetFullPath(path);
        ValidationMessage = null;
        InputFilePath = fullPath;
        FileName = Path.GetFileName(fullPath);
        FileSelected?.Invoke(fullPath);
        return true;
    }

    public void RejectFileSelection(string message)
    {
        ValidationMessage = message;
    }

    public void Clear()
    {
        InputFilePath = null;
        FileName = null;
        ValidationMessage = null;
    }
}
