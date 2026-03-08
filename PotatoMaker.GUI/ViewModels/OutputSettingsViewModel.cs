using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;

namespace PotatoMaker.GUI.ViewModels;

public partial class OutputSettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _useCpuEncoder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputFolderPath))]
    [NotifyPropertyChangedFor(nameof(OutputFolderSummary))]
    [NotifyPropertyChangedFor(nameof(CanResetOutputFolder))]
    private string? _customOutputFolder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputFolderPath))]
    [NotifyPropertyChangedFor(nameof(OutputFolderSummary))]
    [NotifyPropertyChangedFor(nameof(HasSourceFolder))]
    private string? _sourceFolder;

    public string? OutputFolderPath =>
        string.IsNullOrWhiteSpace(CustomOutputFolder) ? SourceFolder : CustomOutputFolder;

    public string OutputFolderSummary =>
        OutputFolderPath is { Length: > 0 } folder
            ? $"Output folder: {folder}"
            : "Output folder: source file folder";

    public bool HasSourceFolder => !string.IsNullOrWhiteSpace(SourceFolder);

    public bool CanResetOutputFolder => !string.IsNullOrWhiteSpace(CustomOutputFolder);

    /// <summary>
    /// Set by the View to open the native folder picker dialog.
    /// </summary>
    public Action? OutputFolderPickerRequested { get; set; }

    [RelayCommand]
    private void SelectOutputFolder()
    {
        OutputFolderPickerRequested?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanResetOutputFolder))]
    private void ResetOutputFolder()
    {
        CustomOutputFolder = null;
    }

    partial void OnCustomOutputFolderChanged(string? value)
    {
        ResetOutputFolderCommand.NotifyCanExecuteChanged();
    }

    partial void OnSourceFolderChanged(string? value)
    {
        ResetOutputFolderCommand.NotifyCanExecuteChanged();
    }

    public void SetSourceFolder(string? folder)
    {
        SourceFolder = folder;
    }

    public void SetCustomOutputFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return;

        CustomOutputFolder = Path.GetFullPath(folder);
    }
}
