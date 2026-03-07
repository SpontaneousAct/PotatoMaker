namespace PotatoMaker.Core;

/// <summary>
/// All user-configurable options for a compression run.
/// Front-ends populate this from their own config source (CLI args, GUI, JSON file).
/// Defaults match the original hardcoded constants.
/// </summary>
public record EncodeSettings
{
    /// <summary>Encoder selection. Default tries NVENC first with CPU fallback.</summary>
    public EncoderChoice Encoder { get; init; } = EncoderChoice.Nvenc;

    /// <summary>Hard file-size limit in MB (used only for summary display).</summary>
    public double TargetSizeMb { get; init; } = 9.5;

    /// <summary>Effective budget used for bitrate math (slightly under hard limit for mux overhead).</summary>
    public double EffectiveTargetMb { get; init; } = 9.0;

    /// <summary>Audio bitrate in kbps (subtracted from video budget).</summary>
    public int AudioBitrateKbps { get; init; } = 128;

    /// <summary>Absolute floor for video bitrate — clamped to this value.</summary>
    public int MinVideoBitrateKbps { get; init; } = 100;

    /// <summary>Minimum video bitrate before splitting into parts (720p tier).</summary>
    public int HdFloorKbps { get; init; } = 500;

    /// <summary>Bitrate threshold for keeping 1080p resolution.</summary>
    public int FullHdFloorKbps { get; init; } = 1000;

    /// <summary>Maximum number of split parts.</summary>
    public int MaxParts { get; init; } = 10;

    /// <summary>Skip crop detection entirely.</summary>
    public bool SkipCropDetect { get; init; }
}
