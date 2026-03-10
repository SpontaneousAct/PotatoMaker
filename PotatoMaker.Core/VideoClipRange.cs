namespace PotatoMaker.Core;

/// <summary>
/// Describes the selected portion of a source video.
/// </summary>
public readonly record struct VideoClipRange(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Duration => End - Start;

    public VideoClipRange Normalize(TimeSpan totalDuration)
    {
        TimeSpan max = totalDuration < TimeSpan.Zero ? TimeSpan.Zero : totalDuration;
        TimeSpan start = Clamp(Start, TimeSpan.Zero, max);
        TimeSpan end = Clamp(End, start, max);
        return new VideoClipRange(start, end);
    }

    public bool CoversFullDuration(TimeSpan totalDuration)
    {
        VideoClipRange normalized = Normalize(totalDuration);
        return normalized.Start == TimeSpan.Zero && normalized.End == totalDuration;
    }

    public static VideoClipRange Full(TimeSpan duration)
    {
        TimeSpan normalizedDuration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        return new VideoClipRange(TimeSpan.Zero, normalizedDuration);
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
    {
        if (value < min)
            return min;

        return value > max ? max : value;
    }
}
