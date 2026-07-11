using PotatoMaker.GUI.Views;
using Xunit;

namespace PotatoMaker.Tests.Views;

public sealed class TimelineViewportTests
{
    [Fact]
    public void ZoomToRange_CentersTheSelectionAndKeepsBothHandlesVisible()
    {
        TimelineViewport zoomed = TimelineViewport.Full.ZoomToRange(1000, 0.49d, 0.51d);

        Assert.True(zoomed.IsZoomed);
        Assert.Equal(0.1d, zoomed.MapTimelineToViewportPosition(0.49d), precision: 10);
        Assert.Equal(0.9d, zoomed.MapTimelineToViewportPosition(0.51d), precision: 10);
        Assert.True(zoomed.Contains(0.49d));
        Assert.True(zoomed.Contains(0.51d));
    }

    [Fact]
    public void ZoomToRange_ClampsAtTheBeginningWithoutHidingTheSelection()
    {
        TimelineViewport zoomed = TimelineViewport.Full.ZoomToRange(1000, 0, 0.02d);

        Assert.Equal(0, zoomed.Start);
        Assert.True(zoomed.Contains(0));
        Assert.True(zoomed.Contains(0.02d));
    }

    [Fact]
    public void ZoomToRange_ClampsAtTheMaximumZoomForAPointSelection()
    {
        TimelineViewport zoomed = TimelineViewport.Full.ZoomToRange(1000, 0.5d, 0.5d);

        Assert.Equal(TimelineViewport.MaximumZoom, zoomed.Zoom, precision: 8);
        Assert.InRange(zoomed.Start, 0, 1);
        Assert.InRange(zoomed.End, 0, 1);
    }

    [Fact]
    public void ZoomToRange_DoesNotZoomWhenTheFullTimelineIsSelected()
    {
        TimelineViewport viewport = TimelineViewport.Full.ZoomToRange(1000, 0, 1);

        Assert.Equal(TimelineViewport.Full, viewport);
    }

    [Fact]
    public void ZoomToRange_StopsWhenTheSelectionFillsTheAllowedViewport()
    {
        TimelineViewport limited = TimelineViewport.Full.ZoomToRange(1000, 0.4d, 0.6d);

        TimelineViewport unchanged = limited.ZoomToRange(1, 0.4d, 0.6d);

        Assert.Equal(limited, unchanged);
    }

    [Fact]
    public void ZoomToRange_ZoomingBackOutRestoresTheFullTimeline()
    {
        TimelineViewport zoomed = TimelineViewport.Full.ZoomToRange(8, 0.49d, 0.51d);

        TimelineViewport restored = zoomed.ZoomToRange(-1000, 0.49d, 0.51d);

        Assert.False(restored.IsZoomed);
        Assert.Equal(TimelineViewport.Full, restored);
    }

    [Fact]
    public void Mapping_ExpandsNearbyTimelinePositionsWhenZoomed()
    {
        TimelineViewport zoomed = TimelineViewport.Full.ZoomToRange(10, 0.49d, 0.51d);

        double left = zoomed.MapTimelineToViewportPosition(0.49d);
        double right = zoomed.MapTimelineToViewportPosition(0.51d);

        Assert.True(right - left > 0.02d);
        Assert.True(zoomed.Contains(0.5d));
    }

    [Fact]
    public void CenterOnRange_AutomaticallyPansAndZoomsOutForChangedTrimBoundaries()
    {
        TimelineViewport zoomed = TimelineViewport.Full.ZoomToRange(1000, 0.49d, 0.51d);

        TimelineViewport centered = zoomed.CenterOnRange(0.2d, 0.8d);

        Assert.Equal(0.1d, centered.MapTimelineToViewportPosition(0.2d), precision: 10);
        Assert.Equal(0.9d, centered.MapTimelineToViewportPosition(0.8d), precision: 10);
        Assert.True(centered.Contains(0.2d));
        Assert.True(centered.Contains(0.8d));
    }
}
