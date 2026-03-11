using System.Reflection;
using PotatoMaker.GUI.ViewModels;
using Xunit;

namespace PotatoMaker.Tests.ViewModels;

public sealed class VideoPlayerViewModelTests
{
    [Theory]
    [InlineData(true, false, false, false, false, true)]
    [InlineData(true, true, false, false, false, false)]
    [InlineData(true, false, true, false, false, false)]
    [InlineData(true, false, false, true, false, false)]
    [InlineData(true, false, false, false, true, false)]
    [InlineData(false, false, false, false, false, false)]
    public void ShouldExposePlayingState_HidesAutomaticResetTransitions(
        bool mediaPlayerIsPlaying,
        bool isPrimingInitialFrame,
        bool isResettingToFirstFrame,
        bool startedPlaybackForSeekPreview,
        bool pauseAfterSeekPauseInFlight,
        bool expected)
    {
        MethodInfo? method = typeof(VideoPlayerViewModel).GetMethod(
            "ShouldExposePlayingState",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        bool result = Assert.IsType<bool>(method!.Invoke(null, [mediaPlayerIsPlaying, isPrimingInitialFrame, isResettingToFirstFrame, startedPlaybackForSeekPreview, pauseAfterSeekPauseInFlight]));

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(95.0, 95.0, true)]
    [InlineData(94.96, 95.0, true)]
    [InlineData(94.90, 95.0, false)]
    [InlineData(0.0, 0.0, false)]
    public void IsAtPlaybackEndPosition_DetectsOnlyNearEnd(double timelineSeconds, double durationSeconds, bool expected)
    {
        MethodInfo? method = typeof(VideoPlayerViewModel).GetMethod(
            "IsAtPlaybackEndPosition",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        bool result = Assert.IsType<bool>(method!.Invoke(null, [timelineSeconds, durationSeconds]));

        Assert.Equal(expected, result);
    }
}
