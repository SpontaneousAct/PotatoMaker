using PotatoMaker.GUI.ViewModels;
using Xunit;

namespace PotatoMaker.Tests.ViewModels;

public sealed class ClipRangeViewModelTests
{
    [Fact]
    public void SetBoundary_MovingStartPastEnd_ShiftsWholeSelectionForward()
    {
        var viewModel = new ClipRangeViewModel();
        viewModel.SetSourceDuration(TimeSpan.FromSeconds(100));
        viewModel.StartSeconds = 10;
        viewModel.EndSeconds = 30;

        viewModel.SetBoundary(ClipBoundary.Start, TimeSpan.FromSeconds(35));

        Assert.Equal(TimeSpan.FromSeconds(35), viewModel.Start);
        Assert.Equal(TimeSpan.FromSeconds(55), viewModel.End);
    }

    [Fact]
    public void SetBoundary_MovingEndPastStart_ShiftsWholeSelectionBackward()
    {
        var viewModel = new ClipRangeViewModel();
        viewModel.SetSourceDuration(TimeSpan.FromSeconds(100));
        viewModel.StartSeconds = 30;
        viewModel.EndSeconds = 50;

        viewModel.SetBoundary(ClipBoundary.End, TimeSpan.FromSeconds(25));

        Assert.Equal(TimeSpan.FromSeconds(5), viewModel.Start);
        Assert.Equal(TimeSpan.FromSeconds(25), viewModel.End);
    }

    [Fact]
    public void SetBoundary_ShiftedSelection_StaysWithinSourceBounds()
    {
        var viewModel = new ClipRangeViewModel();
        viewModel.SetSourceDuration(TimeSpan.FromSeconds(100));
        viewModel.StartSeconds = 70;
        viewModel.EndSeconds = 90;

        viewModel.SetBoundary(ClipBoundary.Start, TimeSpan.FromSeconds(95));

        Assert.Equal(TimeSpan.FromSeconds(80), viewModel.Start);
        Assert.Equal(TimeSpan.FromSeconds(100), viewModel.End);
    }

    [Fact]
    public void EndSeconds_CannotCollapseSelectionBelowMinimumGapAtStart()
    {
        var viewModel = new ClipRangeViewModel();
        viewModel.SetSourceDuration(TimeSpan.FromSeconds(100));
        viewModel.StartSeconds = 0;
        viewModel.EndSeconds = 0;

        Assert.Equal(TimeSpan.Zero, viewModel.Start);
        Assert.Equal(TimeSpan.FromSeconds(0.1), viewModel.End);
        Assert.Equal(TimeSpan.FromSeconds(0.1), viewModel.SelectedDuration);
    }
}
