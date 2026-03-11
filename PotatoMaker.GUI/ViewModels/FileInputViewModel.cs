using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PotatoMaker.Core;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Holds file selection state.
/// </summary>
public partial class FileInputViewModel : ViewModelBase
{
    public const string LockedSelectionMessage = "Wait for compression to finish before changing the source video.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile))]
    [NotifyPropertyChangedFor(nameof(HasNoFile))]
    private string? _inputFilePath;

    [ObservableProperty]
    private string? _fileName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string? _validationMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelectFile))]
    [NotifyPropertyChangedFor(nameof(CanClearFile))]
    private bool _isSourceSelectionLocked;

    public bool HasFile => !string.IsNullOrEmpty(InputFilePath);

    public bool HasNoFile => !HasFile;

    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);

    public bool CanSelectFile => !IsSourceSelectionLocked;

    public bool CanClearFile => HasFile && !IsSourceSelectionLocked;

    /// <summary>
    /// Set by the view to open the native file picker dialog.
    /// </summary>
    public Action? FilePickerRequested { get; set; }

    /// <summary>
    /// Raised after a valid file path has been set.
    /// </summary>
    public event Action<string>? FileSelected;

    public event Action? FileCleared;

    [RelayCommand(CanExecute = nameof(CanSelectFile))]
    private void SelectFile()
    {
        if (!CanSelectFile)
            return;

        FilePickerRequested?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanClearFile))]
    private void ClearFile()
    {
        if (!CanClearFile)
            return;

        Clear();
        FileCleared?.Invoke();
    }

    partial void OnInputFilePathChanged(string? value)
    {
        SelectFileCommand.NotifyCanExecuteChanged();
        ClearFileCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSourceSelectionLockedChanged(bool value)
    {
        SelectFileCommand.NotifyCanExecuteChanged();
        ClearFileCommand.NotifyCanExecuteChanged();
    }

    public bool SetFile(string path)
    {
        if (IsSourceSelectionLocked)
        {
            ValidationMessage = LockedSelectionMessage;
            return false;
        }

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

    public void RejectFileSelection(string message) => ValidationMessage = message;

    public void Clear()
    {
        InputFilePath = null;
        FileName = null;
        ValidationMessage = null;
    }
}
