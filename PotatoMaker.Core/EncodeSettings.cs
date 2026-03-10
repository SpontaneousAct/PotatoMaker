namespace PotatoMaker.Core;

/// <summary>
/// Stores settings for one encode run.
/// </summary>
public record EncodeSettings
{
    public EncoderChoice Encoder { get; init; } = EncoderChoice.Nvenc;

    public double TargetSizeMb { get; init; } = 9.5;

    public double EffectiveTargetMb { get; init; } = 9.0;

    public int AudioBitrateKbps { get; init; } = 128;

    public int MinVideoBitrateKbps { get; init; } = 100;

    public int HdFloorKbps { get; init; } = 500;

    public int FullHdFloorKbps { get; init; } = 1000;

    public int MaxParts { get; init; } = 10;

    public bool SkipCropDetect { get; init; }
}
