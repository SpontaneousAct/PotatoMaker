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

    public const int DefaultMaxVideoBitrateKbps = 10_000;

    public const double DefaultOutputSizeLimitMb = 20.0;

    public const double MinOutputSizeLimitMb = 1.0;

    public const double MaxOutputSizeLimitMb = 1024.0;

    private const double TargetSizeRatio = 0.95;

    private const double EffectiveTargetSizeRatio = 0.90;

    public EncoderChoice Encoder { get; init; } = EncoderChoice.Nvenc;

    public string OutputNamePrefix { get; init; } = DefaultOutputNamePrefix;

    public string OutputNameSuffix { get; init; } = DefaultOutputNameSuffix;

    public EncodeFrameRateMode FrameRateMode { get; init; } = DefaultFrameRateMode;

    public double TargetSizeMb { get; init; } = CalculateTargetSizeMb(DefaultOutputSizeLimitMb);

    public double EffectiveTargetMb { get; init; } = CalculateEffectiveTargetMb(DefaultOutputSizeLimitMb);

    public double OutputSizeLimitMb => TargetSizeMb / TargetSizeRatio;

    public int AudioBitrateKbps { get; init; } = 128;

    public int SvtAv1Preset { get; init; } = DefaultSvtAv1Preset;

    public int MinVideoBitrateKbps { get; init; } = 100;

    public int MaxVideoBitrateKbps { get; init; } = DefaultMaxVideoBitrateKbps;

    public int HdFloorKbps { get; init; } = 500;

    public int FullHdFloorKbps { get; init; } = 1000;

    public int MaxParts { get; init; } = 10;

    public bool SkipCropDetect { get; init; }

    public static double NormalizeOutputSizeLimitMb(double sizeLimitMb)
    {
        if (double.IsNaN(sizeLimitMb) || double.IsInfinity(sizeLimitMb))
            return DefaultOutputSizeLimitMb;

        return Math.Clamp(sizeLimitMb, MinOutputSizeLimitMb, MaxOutputSizeLimitMb);
    }

    public static double CalculateTargetSizeMb(double outputSizeLimitMb) =>
        NormalizeOutputSizeLimitMb(outputSizeLimitMb) * TargetSizeRatio;

    public static double CalculateEffectiveTargetMb(double outputSizeLimitMb) =>
        NormalizeOutputSizeLimitMb(outputSizeLimitMb) * EffectiveTargetSizeRatio;

    public EncodeSettings WithOutputSizeLimit(double outputSizeLimitMb) => this with
    {
        TargetSizeMb = CalculateTargetSizeMb(outputSizeLimitMb),
        EffectiveTargetMb = CalculateEffectiveTargetMb(outputSizeLimitMb)
    };

    public static int NormalizeSvtAv1Preset(int preset) => Math.Clamp(preset, MinSvtAv1Preset, MaxSvtAv1Preset);

    public static string NormalizeOutputNameAffix(string? affix)
    {
        if (string.IsNullOrWhiteSpace(affix))
            return string.Empty;

        string trimmedAffix = affix.Trim();
        return trimmedAffix[..Math.Min(trimmedAffix.Length, MaxOutputNameAffixLength)];
    }
}
