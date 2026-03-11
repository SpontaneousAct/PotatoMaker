using PotatoMaker.Core;
using Xunit;

namespace PotatoMaker.Tests.Core;

public sealed class OutputFileNameBuilderTests
{
    [Fact]
    public void BuildOutputPath_UsesConfiguredPrefixAndSuffix()
    {
        string outputPath = OutputFileNameBuilder.BuildOutputPath(
            "C:\\encoded",
            "clip",
            new EncodeSettings
            {
                OutputNamePrefix = "share_",
                OutputNameSuffix = "_mobile"
            });

        Assert.Equal(Path.Combine("C:\\encoded", "share_clip_mobile.mp4"), outputPath);
    }

    [Fact]
    public void BuildOutputPath_ForSplitOutput_AppendsPartNumberAfterSuffix()
    {
        string outputPath = OutputFileNameBuilder.BuildOutputPath(
            "C:\\encoded",
            "clip",
            new EncodeSettings
            {
                OutputNamePrefix = "share_",
                OutputNameSuffix = "_mobile"
            },
            partNumber: 2);

        Assert.Equal(Path.Combine("C:\\encoded", "share_clip_mobile_part2.mp4"), outputPath);
    }
}
