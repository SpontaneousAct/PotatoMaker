namespace PotatoMaker.Core;

/// <summary>
/// Stores settings for one encode run.
/// </summary>
public record EncodeSettings
{
    public const int MaxOutputNameAffixLength = 64;

    public const string DefaultOutputNamePrefix = "";

    public const string DefaultOutputNameSuffix = "_discord";

    public const EncodeFrameRateMode DefaultFrameRateMode = EncodeFrameRateMode.Original;

    public const int MinSvtAv1Preset = 0;

    public const int MaxSvtAv1Preset = 13;

    public const int DefaultSvtAv1Preset = 6;

    public EncoderChoice Encoder { get; init; } = EncoderChoice.Nvenc;

    public string OutputNamePrefix { get; init; } = DefaultOutputNamePrefix;

    public string OutputNameSuffix { get; init; } = DefaultOutputNameSuffix;

    public EncodeFrameRateMode FrameRateMode { get; init; } = DefaultFrameRateMode;

    public double TargetSizeMb { get; init; } = 9.5;

    public double EffectiveTargetMb { get; init; } = 9.0;

    public int AudioBitrateKbps { get; init; } = 128;

    public int SvtAv1Preset { get; init; } = DefaultSvtAv1Preset;

    public int MinVideoBitrateKbps { get; init; } = 100;

    public int HdFloorKbps { get; init; } = 500;

    public int FullHdFloorKbps { get; init; } = 1000;

    public int MaxParts { get; init; } = 10;

    public bool SkipCropDetect { get; init; }

    public static int NormalizeSvtAv1Preset(int preset) => Math.Clamp(preset, MinSvtAv1Preset, MaxSvtAv1Preset);

    public static string NormalizeOutputNameAffix(string? affix)
    {
        if (string.IsNullOrWhiteSpace(affix))
            return string.Empty;

        string trimmedAffix = affix.Trim();
        return trimmedAffix[..Math.Min(trimmedAffix.Length, MaxOutputNameAffixLength)];
    }
}
