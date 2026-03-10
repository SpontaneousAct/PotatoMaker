using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Stores output folder and encoder preferences.
/// </summary>
public partial class OutputSettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _useNvencEncoder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseNvenc))]
    [NotifyPropertyChangedFor(nameof(NvencSupportSummary))]
    private bool _isNvencSupportKnown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseNvenc))]
    [NotifyPropertyChangedFor(nameof(NvencSupportSummary))]
    private bool _isNvencSupported;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputFolderPath))]
    [NotifyPropertyChangedFor(nameof(OutputFolderDisplay))]
    [NotifyPropertyChangedFor(nameof(OutputFolderDisplayWrapped))]
    [NotifyPropertyChangedFor(nameof(CanResetOutputFolder))]
    private string? _customOutputFolder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputFolderPath))]
    [NotifyPropertyChangedFor(nameof(OutputFolderDisplay))]
    [NotifyPropertyChangedFor(nameof(OutputFolderDisplayWrapped))]
    private string? _sourceFolder;

    public string? OutputFolderPath =>
        string.IsNullOrWhiteSpace(CustomOutputFolder) ? SourceFolder : CustomOutputFolder;

    public string OutputFolderDisplay => OutputFolderPath ?? "Source file folder";

    public string OutputFolderDisplayWrapped => AddPathWrapHints(OutputFolderDisplay);

    public bool CanUseNvenc => IsNvencSupportKnown && IsNvencSupported;

    public string NvencSupportSummary =>
        !IsNvencSupportKnown
            ? "Checking NVENC AV1 support..."
            : IsNvencSupported
                ? "NVENC AV1 is available on this system."
                : "NVENC AV1 is not available on this system.";

    public bool CanResetOutputFolder =>
        !string.IsNullOrWhiteSpace(CustomOutputFolder) &&
        !PathsEqual(CustomOutputFolder, SourceFolder);

    /// <summary>
    /// Set by the view to open the native folder picker dialog.
    /// </summary>
    public Action? OutputFolderPickerRequested { get; set; }

    [RelayCommand]
    private void SelectOutputFolder() => OutputFolderPickerRequested?.Invoke();

    [RelayCommand(CanExecute = nameof(CanResetOutputFolder))]
    private void ResetOutputFolder() => CustomOutputFolder = null;

    partial void OnCustomOutputFolderChanged(string? value) => ResetOutputFolderCommand.NotifyCanExecuteChanged();

    partial void OnSourceFolderChanged(string? value) => ResetOutputFolderCommand.NotifyCanExecuteChanged();

    public void SetSourceFolder(string? folder)
    {
        SourceFolder = NormalizeFolderPath(folder);

        if (PathsEqual(CustomOutputFolder, SourceFolder))
            CustomOutputFolder = null;
    }

    public void SetCustomOutputFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return;

        string? normalizedFolder = NormalizeFolderPath(folder);
        CustomOutputFolder = PathsEqual(normalizedFolder, SourceFolder)
            ? null
            : normalizedFolder;
    }

    public void SetNvencSupport(bool supported)
    {
        IsNvencSupported = supported;
        IsNvencSupportKnown = true;

        if (!supported)
            UseNvencEncoder = false;
    }

    partial void OnUseNvencEncoderChanged(bool value)
    {
        if (value && IsNvencSupportKnown && !IsNvencSupported)
            UseNvencEncoder = false;
    }

    private static string? NormalizeFolderPath(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return null;

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
    }

    private static bool PathsEqual(string? left, string? right)
    {
        string? normalizedLeft = NormalizeFolderPath(left);
        string? normalizedRight = NormalizeFolderPath(right);

        return normalizedLeft is not null &&
               normalizedRight is not null &&
               string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string AddPathWrapHints(string path) =>
        path.Replace("\\", "\\\u200B", StringComparison.Ordinal)
            .Replace("/", "/\u200B", StringComparison.Ordinal);
}
