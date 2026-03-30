using PotatoMaker.Core;
using Xunit;

namespace PotatoMaker.Tests.Core;

public sealed class EncodePlannerTests
{
    [Fact]
    public void PlanStrategy_ZeroDuration_ThrowsFriendlyError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            EncodePlanner.PlanStrategy(0, 1080, 60, new EncodeSettings()));

        Assert.Equal("The selected clip has no duration. Choose a valid source video or a longer clip.", ex.Message);
    }

    [Fact]
    public void PlanStrategy_LowerFrameRateMode_CanImproveResolutionChoice()
    {
        EncodeSettings settings = new()
        {
            FrameRateMode = EncodeFrameRateMode.Fps30
        };

        EncodePlanner.EncodePlan plan = EncodePlanner.PlanStrategy(90, 1440, 60, settings);

        Assert.Equal(1, plan.Parts);
        Assert.Equal("1080p (downscaled)", plan.ResolutionLabel);
    }

    [Fact]
    public void PlanStrategy_LowerFrameRateMode_CanAvoidSplittingLongerClips()
    {
        EncodeSettings originalSettings = new();
        EncodeSettings reducedFrameRateSettings = new()
        {
            FrameRateMode = EncodeFrameRateMode.Fps30
        };

        EncodePlanner.EncodePlan originalPlan = EncodePlanner.PlanStrategy(170, 1080, 60, originalSettings);
        EncodePlanner.EncodePlan reducedFrameRatePlan = EncodePlanner.PlanStrategy(170, 1080, 60, reducedFrameRateSettings);

        Assert.True(originalPlan.Parts > 1);
        Assert.Equal(1, reducedFrameRatePlan.Parts);
        Assert.Equal("720p (downscaled)", reducedFrameRatePlan.ResolutionLabel);
    }

    [Fact]
    public void ApplySourceVideoBitrateCap_ShortClip_UsesSourceVideoBitrate()
    {
        EncodePlanner.EncodePlan uncappedPlan = EncodePlanner.PlanStrategy(0.1, 1080, 60, new EncodeSettings());

        EncodePlanner.EncodePlan cappedPlan = EncodePlanner.ApplySourceVideoBitrateCap(uncappedPlan, 4500);

        Assert.Equal(4500, cappedPlan.VideoBitrateKbps);
        Assert.True(cappedPlan.IsBitrateCappedToSource);
        Assert.Equal(4500, cappedPlan.SourceVideoBitrateKbps);
        Assert.Equal(uncappedPlan.Parts, cappedPlan.Parts);
        Assert.Equal(uncappedPlan.ResolutionLabel, cappedPlan.ResolutionLabel);
    }

    [Fact]
    public void BuildFrameRateFilter_OnlyReturnsFilterWhenFrameRateDrops()
    {
        EncodeSettings cappedSettings = new()
        {
            FrameRateMode = EncodeFrameRateMode.Fps30
        };

        Assert.Equal("fps=30", EncodePlanner.BuildFrameRateFilter(60, cappedSettings));
        Assert.Null(EncodePlanner.BuildFrameRateFilter(29.97, cappedSettings));
    }

    [Fact]
    public void BuildCenteredCropFilterForAspectRatio_MatchingAspectRatio_ReturnsNull()
    {
        Assert.Null(EncodePlanner.BuildCenteredCropFilterForAspectRatio(1920, 1080, 16, 9));
    }

    [Fact]
    public void BuildCenteredCropFilterForAspectRatio_LandscapeSource_UsesCenteredEvenCrop()
    {
        string? cropFilter = EncodePlanner.BuildCenteredCropFilterForAspectRatio(1920, 1080, 21, 9);

        Assert.Equal("crop=1920:820:0:130", cropFilter);
    }

    [Fact]
    public void BuildCenteredCropFilterForAspectRatio_4kSource_ScalesToSourceResolution()
    {
        string? cropFilter = EncodePlanner.BuildCenteredCropFilterForAspectRatio(3840, 2160, 21, 9);

        Assert.Equal("crop=3840:1644:0:258", cropFilter);
    }
}
