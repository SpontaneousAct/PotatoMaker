namespace PotatoMaker.GUI.Views;

/// <summary>
/// Describes the normalized portion of a video's timeline that is currently visible.
/// </summary>
internal readonly record struct TimelineViewport(double Start, double Span)
{
    internal const double MaximumZoom = 64d;
    private const double ZoomStep = 1.25d;
    private const double RangeViewportFraction = 0.8d;
    private const double VisibilityTolerance = 0.0000001d;

    public static TimelineViewport Full => new(0, 1);

    public double End => Start + Span;

    public double Zoom => 1d / Span;

    public bool IsZoomed => Zoom > 1.0001d;

    public TimelineViewport ZoomToRange(double wheelDelta, double rangeStart, double rangeEnd)
    {
        if (!double.IsFinite(wheelDelta) ||
            !double.IsFinite(rangeStart) ||
            !double.IsFinite(rangeEnd) ||
            Math.Abs(wheelDelta) < double.Epsilon)
        {
            return this;
        }

        double startBoundary = Clamp(Math.Min(rangeStart, rangeEnd), 0, 1);
        double endBoundary = Clamp(Math.Max(rangeStart, rangeEnd), 0, 1);
        double rangeSpan = endBoundary - startBoundary;
        double maximumRangeZoom = rangeSpan > 0
            ? RangeViewportFraction / rangeSpan
            : MaximumZoom;
        double maximumZoom = Clamp(maximumRangeZoom, 1, MaximumZoom);
        double zoom = Clamp(Zoom * Math.Pow(ZoomStep, wheelDelta), 1, maximumZoom);
        double span = 1d / zoom;
        double rangeCenter = startBoundary + (rangeSpan / 2d);
        double start = Clamp(rangeCenter - (span / 2d), 0, 1 - span);

        return new TimelineViewport(start, span);
    }

    public TimelineViewport CenterOnRange(double rangeStart, double rangeEnd)
    {
        if (!double.IsFinite(rangeStart) || !double.IsFinite(rangeEnd))
            return this;

        double startBoundary = Clamp(Math.Min(rangeStart, rangeEnd), 0, 1);
        double endBoundary = Clamp(Math.Max(rangeStart, rangeEnd), 0, 1);
        double rangeSpan = endBoundary - startBoundary;
        double minimumSpan = Clamp(
            rangeSpan / RangeViewportFraction,
            1d / MaximumZoom,
            1);
        double span = Math.Max(Span, minimumSpan);
        double rangeCenter = startBoundary + (rangeSpan / 2d);
        double start = Clamp(rangeCenter - (span / 2d), 0, 1 - span);

        return new TimelineViewport(start, span);
    }

    public double MapViewportPositionToTimeline(double viewportPosition) =>
        Start + (Clamp(viewportPosition, 0, 1) * Span);

    public double MapTimelineToViewportPosition(double timelinePosition) =>
        (timelinePosition - Start) / Span;

    public bool Contains(double timelinePosition) =>
        timelinePosition >= Start - VisibilityTolerance &&
        timelinePosition <= End + VisibilityTolerance;

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
            return min;

        return value > max ? max : value;
    }
}
