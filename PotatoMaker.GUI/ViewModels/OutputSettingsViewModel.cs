using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PotatoMaker.Core;

namespace PotatoMaker.GUI.ViewModels;

public sealed class CpuEncodePresetOption
{
    public CpuEncodePresetOption(int value, string label)
    {
        Value = value;
        Label = label;
    }

    public int Value { get; }

    public string Label { get; }

    public override string ToString() => Label;
}

public sealed class FrameRateOption
{
    public FrameRateOption(EncodeFrameRateMode value, string label)
    {
        Value = value;
        Label = label;
    }

    public EncodeFrameRateMode Value { get; }

    public string Label { get; }

    public override string ToString() => Label;
}

/// <summary>
/// Stores output folder, file naming, and encoder preferences.
/// </summary>
public partial class OutputSettingsViewModel : ViewModelBase
{
    private static readonly int[] AvailableCpuPresets =
    [
        6,
        8,
        10
    ];

    public OutputSettingsViewModel()
    {
        SelectedCpuEncodePreset = CpuEncodePresetOptions.First(option => option.Value == EncodeSettings.DefaultSvtAv1Preset);
        SelectedFrameRateOption = FrameRateOptions.First(option => option.Value == EncodeSettings.DefaultFrameRateMode);
    }

    [ObservableProperty]
    private bool _useNvencEncoder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuEncodePreset))]
    private CpuEncodePresetOption? _selectedCpuEncodePreset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrameRateMode))]
    private FrameRateOption? _selectedFrameRateOption;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputFileNamePreview))]
    private string _outputNamePrefix = EncodeSettings.DefaultOutputNamePrefix;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputFileNamePreview))]
    private string _outputNameSuffix = EncodeSettings.DefaultOutputNameSuffix;

    public string? OutputFolderPath =>
        string.IsNullOrWhiteSpace(CustomOutputFolder) ? SourceFolder : CustomOutputFolder;

    public string OutputFolderDisplay => OutputFolderPath ?? "Source file folder";

    public string OutputFolderDisplayWrapped => AddPathWrapHints(OutputFolderDisplay);

    public bool CanUseNvenc => IsNvencSupportKnown && IsNvencSupported;

    public string OutputFileNamePreview =>
        $"{EncodeSettings.NormalizeOutputNameAffix(OutputNamePrefix)}example-video{EncodeSettings.NormalizeOutputNameAffix(OutputNameSuffix)}.mp4";

    public int OutputNameAffixMaxLength => EncodeSettings.MaxOutputNameAffixLength;

    public string NvencSupportSummary =>
        !IsNvencSupportKnown
            ? "Checking NVENC AV1 support..."
            : IsNvencSupported
                ? "NVENC AV1 is available on this system."
                : "NVENC AV1 is not available on this system.";

    public IReadOnlyList<CpuEncodePresetOption> CpuEncodePresetOptions { get; } = CreateCpuEncodePresetOptions();

    public IReadOnlyList<FrameRateOption> FrameRateOptions { get; } = CreateFrameRateOptions();

    public int CpuEncodePreset => SelectedCpuEncodePreset?.Value ?? EncodeSettings.DefaultSvtAv1Preset;

    public EncodeFrameRateMode FrameRateMode => SelectedFrameRateOption?.Value ?? EncodeSettings.DefaultFrameRateMode;

    public bool CanResetOutputFolder => !string.IsNullOrWhiteSpace(CustomOutputFolder);

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

    partial void OnOutputNamePrefixChanged(string value)
    {
        string normalizedValue = EncodeSettings.NormalizeOutputNameAffix(value);
        if (!string.Equals(value, normalizedValue, StringComparison.Ordinal))
            OutputNamePrefix = normalizedValue;
    }

    partial void OnOutputNameSuffixChanged(string value)
    {
        string normalizedValue = EncodeSettings.NormalizeOutputNameAffix(value);
        if (!string.Equals(value, normalizedValue, StringComparison.Ordinal))
            OutputNameSuffix = normalizedValue;
    }

    public void SetSourceFolder(string? folder)
    {
        SourceFolder = NormalizeFolderPath(folder);
    }

    public void SetCustomOutputFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return;

        CustomOutputFolder = NormalizeFolderPath(folder);
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

    public void SetCpuEncodePreset(int preset)
    {
        int normalizedPreset = EncodeSettings.NormalizeSvtAv1Preset(preset);
        SelectedCpuEncodePreset = CpuEncodePresetOptions
            .OrderBy(option => Math.Abs(option.Value - normalizedPreset))
            .ThenBy(option => option.Value)
            .First();
    }

    public void SetFrameRateMode(EncodeFrameRateMode mode)
    {
        SelectedFrameRateOption = FrameRateOptions.FirstOrDefault(option => option.Value == mode)
            ?? FrameRateOptions[0];
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

    private static IReadOnlyList<CpuEncodePresetOption> CreateCpuEncodePresetOptions() =>
        AvailableCpuPresets
            .Select(CreateCpuEncodePresetOption)
            .ToArray();

    private static IReadOnlyList<FrameRateOption> CreateFrameRateOptions() =>
    [
        new FrameRateOption(EncodeFrameRateMode.Fps30, "30 FPS"),
        new FrameRateOption(EncodeFrameRateMode.Fps60, "60 FPS"),
        new FrameRateOption(EncodeFrameRateMode.Original, "Leave As Original")
    ];

    private static CpuEncodePresetOption CreateCpuEncodePresetOption(int preset)
    {
        string description = preset switch
        {
            EncodeSettings.DefaultSvtAv1Preset => "Balanced (default)",
            8 => "Faster",
            10 => "Fastest",
            _ => "Custom"
        };

        return new CpuEncodePresetOption(preset, $"{preset} - {description}");
    }
}
