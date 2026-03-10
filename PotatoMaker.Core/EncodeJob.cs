namespace PotatoMaker.Core;

/// <summary>
/// Represents a single FFmpeg encode job.
/// </summary>
public record EncodeJob(
    string InputPath,
    string OutputPath,
    TimeSpan TotalDuration,
    int VideoBitrateKbps,
    int AudioBitrateKbps,
    string? VideoFilter,
    double StartOffsetSecs = 0,
    double? SegmentSecs = null);
