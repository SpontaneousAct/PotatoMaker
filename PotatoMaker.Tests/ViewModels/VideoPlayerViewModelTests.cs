using System.Reflection;
using PotatoMaker.Core;
using PotatoMaker.GUI.ViewModels;
using Xunit;

namespace PotatoMaker.Tests.ViewModels;

public sealed class VideoPlayerViewModelTests
{
    [Theory]
    [InlineData(true, false, false, false, true, true)]
    [InlineData(true, true, false, false, false, false)]
    [InlineData(true, false, true, false, false, false)]
    [InlineData(true, false, false, true, false, false)]
    [InlineData(false, false, false, false, false, false)]
    [InlineData(true, false, false, true, true, true)]
    public void ShouldExposePlayingState_HidesAutomaticResetTransitions(
        bool mediaPlayerIsPlaying,
        bool isPrimingInitialFrame,
        bool isResettingToFirstFrame,
        bool isSeekPreviewPlaybackManaged,
        bool resumePlaybackAfterSeek,
        bool expected)
    {
        MethodInfo? method = typeof(VideoPlayerViewModel).GetMethod(
            "ShouldExposePlayingState",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        bool result = Assert.IsType<bool>(method!.Invoke(null, [
            mediaPlayerIsPlaying,
            isPrimingInitialFrame,
            isResettingToFirstFrame,
            isSeekPreviewPlaybackManaged,
            resumePlaybackAfterSeek
        ]));

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
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void ShouldIgnorePlayerTimelineUpdate_SuppressesStalePlayerPositions(
        bool isSeekInteractionActive,
        bool pendingSeekPreviewTarget,
        bool expected)
    {
        MethodInfo? method = typeof(VideoPlayerViewModel).GetMethod(
            "ShouldIgnorePlayerTimelineUpdate",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        bool result = Assert.IsType<bool>(method!.Invoke(
            null,
            [isSeekInteractionActive, pendingSeekPreviewTarget]));

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 8, true)]
    [InlineData(1, 10, false)]
    public void AreSeekPositionsEquivalent_UsesConfiguredTolerance(
        int leftMilliseconds,
        int rightMilliseconds,
        bool expected)
    {
        MethodInfo? method = typeof(VideoPlayerViewModel).GetMethod(
            "AreSeekPositionsEquivalent",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        bool result = Assert.IsType<bool>(method!.Invoke(
            null,
            [
                TimeSpan.FromMilliseconds(leftMilliseconds),
                TimeSpan.FromMilliseconds(rightMilliseconds)
            ]));

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(3000, 3000, 3004, true)]
    [InlineData(1000, 2995, 3000, true)]
    [InlineData(1000, -1, 3000, false)]
    [InlineData(1000, 1004, 3000, false)]
    public void IsPreviewSeekConfirmed_RequiresANonStalePlayerResult(
        long beforeTimeMilliseconds,
        long afterTimeMilliseconds,
        long targetTimeMilliseconds,
        bool expected)
    {
        MethodInfo? method = typeof(VideoPlayerViewModel).GetMethod(
            "IsPreviewSeekConfirmed",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        bool result = Assert.IsType<bool>(method!.Invoke(
            null,
            [beforeTimeMilliseconds, afterTimeMilliseconds, targetTimeMilliseconds]));

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(true, 33)]
    [InlineData(false, 75)]
    public void GetSeekPreviewDispatchInterval_UsesPlaybackAwareThrottle(
        bool resumePlaybackAfterSeek,
        int expectedMilliseconds)
    {
        MethodInfo? method = typeof(VideoPlayerViewModel).GetMethod(
            "GetSeekPreviewDispatchInterval",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        TimeSpan result = Assert.IsType<TimeSpan>(method!.Invoke(null, [resumePlaybackAfterSeek]));

        Assert.Equal(TimeSpan.FromMilliseconds(expectedMilliseconds), result);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShouldUsePauseToggleFallback_OnlyWhenPlaybackStillLooksActive(
        bool mediaPlayerIsPlaying,
        bool expected)
    {
        MethodInfo? method = typeof(VideoPlayerViewModel).GetMethod(
            "ShouldUsePauseToggleFallback",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        bool result = Assert.IsType<bool>(method!.Invoke(null, [mediaPlayerIsPlaying]));

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0.5, 5.0, 10.0, true)]
    [InlineData(0.5007, 5.0, 10.0, true)]
    [InlineData(0.51, 5.0, 10.0, false)]
    [InlineData(float.NaN, 5.0, 10.0, false)]
    [InlineData(0.5, 5.0, 0.0, false)]
    public void IsSeekPositionReached_UsesDurationScaledTolerance(
        float currentPosition,
        double targetSeconds,
        double durationSeconds,
        bool expected)
    {
        MethodInfo? method = typeof(VideoPlayerViewModel).GetMethod(
            "IsSeekPositionReached",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        bool result = Assert.IsType<bool>(method!.Invoke(
            null,
            [currentPosition, TimeSpan.FromSeconds(targetSeconds), durationSeconds]));

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(false, false, false, false, true)]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    public void ShouldApplyPausedSeekRefresh_OnlyWhenStillPausedAndIdle(
        bool isSeekInteractionActive,
        bool resumePlaybackAfterSeek,
        bool isSeekPlaybackRestorePending,
        bool mediaPlayerIsPlaying,
        bool expected)
    {
        MethodInfo? method = typeof(VideoPlayerViewModel).GetMethod(
            "ShouldApplyPausedSeekRefresh",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        bool result = Assert.IsType<bool>(method!.Invoke(
            null,
            [
                isSeekInteractionActive,
                resumePlaybackAfterSeek,
                isSeekPlaybackRestorePending,
                mediaPlayerIsPlaying
            ]));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void HandleMediaPlayerPlaying_ClearsSeekPlaybackRestoreFlags()
    {
        var viewModel = new VideoPlayerViewModel(initializePlayer: false);
        FieldInfo? pendingField = typeof(VideoPlayerViewModel).GetField(
            "_isSeekPlaybackRestorePending",
            BindingFlags.NonPublic | BindingFlags.Instance);
        FieldInfo? resumeField = typeof(VideoPlayerViewModel).GetField(
            "_resumePlaybackAfterSeek",
            BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo? method = typeof(VideoPlayerViewModel).GetMethod(
            "HandleMediaPlayerPlaying",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(pendingField);
        Assert.NotNull(resumeField);
        Assert.NotNull(method);

        pendingField!.SetValue(viewModel, true);
        resumeField!.SetValue(viewModel, true);

        method!.Invoke(viewModel, []);

        Assert.False(Assert.IsType<bool>(pendingField.GetValue(viewModel)));
        Assert.False(Assert.IsType<bool>(resumeField.GetValue(viewModel)));
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
