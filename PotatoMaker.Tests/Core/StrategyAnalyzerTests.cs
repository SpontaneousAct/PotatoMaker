using PotatoMaker.Core;
using Xunit;

namespace PotatoMaker.Tests.Core;

public sealed class StrategyAnalyzerTests
{
    [Fact]
    public void BuildAnalysis_ShortClip_CapsBitrateToSourceVideoBitrate()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"potatomaker-{Guid.NewGuid():N}.mp4");
        File.WriteAllText(inputPath, "video");

        try
        {
            VideoInfo info = new(TimeSpan.FromSeconds(95), 1920, 1080, 59.94, 4500);
            VideoClipRange clipRange = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10.1));

            StrategyAnalysis analysis = StrategyAnalyzer.BuildAnalysis(inputPath, info, new EncodeSettings(), cropFilter: null, clipRange);

            Assert.Equal(4500, analysis.Plan.VideoBitrateKbps);
            Assert.True(analysis.Plan.IsBitrateCappedToSource);
            Assert.Equal(4500, analysis.Plan.SourceVideoBitrateKbps);
            Assert.Equal(1, analysis.Plan.Parts);
        }
        finally
        {
            if (File.Exists(inputPath))
                File.Delete(inputPath);
        }
    }
}
