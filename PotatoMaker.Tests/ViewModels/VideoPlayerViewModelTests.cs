using System.Reflection;
using PotatoMaker.Core;
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

    [Theory]
    [InlineData(true, false, false, false, true)]
    [InlineData(false, true, false, false, true)]
    [InlineData(false, false, true, false, true)]
    [InlineData(false, false, false, true, true)]
    [InlineData(false, false, false, false, false)]
    public void ShouldIgnorePlayerTimelineUpdate_SuppressesStalePlayerPositions(
        bool isSeekInteractionActive,
        bool hasPendingQueuedSeek,
        bool pauseAfterSeekTargetPending,
        bool pauseAfterSeekPauseInFlight,
        bool expected)
    {
        MethodInfo? method = typeof(VideoPlayerViewModel).GetMethod(
            "ShouldIgnorePlayerTimelineUpdate",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        bool result = Assert.IsType<bool>(method!.Invoke(
            null,
            [isSeekInteractionActive, hasPendingQueuedSeek, pauseAfterSeekTargetPending, pauseAfterSeekPauseInFlight]));

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(4_900, 5_000, 1, true)]
    [InlineData(4_700, 5_000, 1, false)]
    [InlineData(5_080, 5_000, 0, true)]
    [InlineData(5_220, 5_000, 0, false)]
    [InlineData(5_120, 5_000, -1, true)]
    [InlineData(5_260, 5_000, -1, false)]
    public void HasReachedPauseAfterSeekTarget_UsesDirectionAwareTolerance(
        long currentTimeMilliseconds,
        long targetTimeMilliseconds,
        int direction,
        bool expected)
    {
        MethodInfo? method = typeof(VideoPlayerViewModel).GetMethod(
            "HasReachedPauseAfterSeekTarget",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        bool result = Assert.IsType<bool>(method!.Invoke(
            null,
            [currentTimeMilliseconds, targetTimeMilliseconds, direction]));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void LoadSource_BeforeInitialization_ShowsDeferredLoadingState()
    {
        var viewModel = new VideoPlayerViewModel(initializePlayer: false);

        viewModel.LoadSource(
            @"C:\videos\startup.mp4",
            TimeSpan.FromSeconds(95),
            VideoClipRange.Full(TimeSpan.FromSeconds(95)));

        Assert.Equal("startup.mp4", viewModel.LoadedFileName);
        Assert.Equal(95, viewModel.DurationSeconds);
        Assert.False(viewModel.HasMedia);
        Assert.Equal("Preparing video preview...", viewModel.StatusMessage);
        Assert.Null(viewModel.PlayerErrorMessage);
    }

    [Fact]
    public void Clear_BeforeInitialization_RestoresIdleStatus()
    {
        var viewModel = new VideoPlayerViewModel(initializePlayer: false);

        viewModel.LoadSource(
            @"C:\videos\startup.mp4",
            TimeSpan.FromSeconds(95),
            VideoClipRange.Full(TimeSpan.FromSeconds(95)));

        viewModel.Clear();

        Assert.Equal("No video selected", viewModel.LoadedFileName);
        Assert.Equal("Select a video to preview it.", viewModel.StatusMessage);
        Assert.Null(viewModel.PlayerErrorMessage);
    }
}
