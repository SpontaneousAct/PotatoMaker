using PotatoMaker.Core;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Stores persisted GUI preferences.
/// </summary>
public sealed record AppSettings
{
    public bool IsDarkMode { get; init; }

    public bool UseNvencEncoder { get; init; } = true;

    public string OutputNamePrefix { get; init; } = EncodeSettings.DefaultOutputNamePrefix;

    public string OutputNameSuffix { get; init; } = EncodeSettings.DefaultOutputNameSuffix;

    public EncodeFrameRateMode FrameRateMode { get; init; } = EncodeSettings.DefaultFrameRateMode;

    public double PreviewVolumePercent { get; init; } = 100;

    public int SvtAv1Preset { get; init; } = EncodeSettings.DefaultSvtAv1Preset;

    public string? LastOutputFolder { get; init; }
}
