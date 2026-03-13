using System.Globalization;
using System.Reflection;
using PotatoMaker.Core;
using Xunit;

namespace PotatoMaker.Tests.Core;

public sealed class CropDetectorTests
{
    [Fact]
    public void EvaluateCrop_RejectsMeaningfulOffCenterRemoval()
    {
        CropEvaluation evaluation = CropDetector.EvaluateCrop(
            new CropRect(4308, 1312, 752, 56),
            5120,
            1440);

        Assert.False(evaluation.IsValid);
        Assert.False(evaluation.HasCrop);
    }

    [Fact]
    public void EvaluateCrop_NormalizesMinorNoiseOnUnusedAxis()
    {
        CropEvaluation evaluation = CropDetector.EvaluateCrop(
            new CropRect(1912, 800, 4, 140),
            1920,
            1080);

        Assert.True(evaluation.IsValid);
        Assert.True(evaluation.HasCrop);
        Assert.True(evaluation.HasLetterbox);
        Assert.False(evaluation.HasPillarbox);
        Assert.Equal(new CropRect(1920, 800, 0, 140), evaluation.NormalizedCrop);
    }

    [Fact]
    public void SelectWindowCrop_PrefersStableCropOverSingleLateOutlier()
    {
        CropRect[] observedCrops =
        [
            ..Enumerable.Repeat(new CropRect(1920, 800, 0, 140), 8),
            new CropRect(1920, 804, 0, 138)
        ];

        CropRect? crop = CropDetector.SelectWindowCrop(observedCrops, 1920, 1080);

        Assert.Equal(new CropRect(1920, 800, 0, 140), crop);
    }

    [Fact]
    public void SelectWindowCrop_RejectsCropThatDoesNotBeatFullFrameObservations()
    {
        CropRect[] observedCrops =
        [
            ..Enumerable.Repeat(new CropRect(1920, 1080, 0, 0), 5),
            ..Enumerable.Repeat(new CropRect(1920, 800, 0, 140), 4)
        ];

        CropRect? crop = CropDetector.SelectWindowCrop(observedCrops, 1920, 1080);

        Assert.Null(crop);
    }

    [Fact]
    public void SelectFinalCrop_RequiresMajorityAgreementAcrossSamples()
    {
        CropRect sampleCrop = new(1920, 800, 0, 140);
        CropRect?[] sampleCrops = [sampleCrop, sampleCrop, null, null, null];

        CropRect? finalCrop = CropDetector.SelectFinalCrop(sampleCrops);

        Assert.Null(finalCrop);
    }

    [Fact]
    public void SelectFinalCrop_ReturnsConsensusCropWhenMajorityAgrees()
    {
        CropRect sampleCrop = new(1920, 800, 0, 140);
        CropRect?[] sampleCrops =
        [
            sampleCrop,
            sampleCrop,
            sampleCrop,
            null,
            new CropRect(1440, 1080, 240, 0)
        ];

        CropRect? finalCrop = CropDetector.SelectFinalCrop(sampleCrops);

        Assert.Equal(sampleCrop, finalCrop);
    }

    [Fact]
    public void BuildSampleTimes_SpreadsAcrossSelection()
    {
        IReadOnlyList<double> sampleTimes = CropDetector.BuildSampleTimes(
            TimeSpan.FromSeconds(12),
            TimeSpan.FromSeconds(90));

        Assert.Equal([21.0, 39.0, 57.0, 75.0, 93.0], sampleTimes);
    }

    [Fact]
    public void FormatSeconds_UsesInvariantCulture()
    {
        MethodInfo? formatMethod = typeof(CropDetector).GetMethod("FormatSeconds", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(formatMethod);

        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
            string formatted = Assert.IsType<string>(formatMethod!.Invoke(null, [12.5d]));

            Assert.Equal("12.5", formatted);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
