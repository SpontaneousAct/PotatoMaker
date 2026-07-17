using PotatoMaker.Core;
using Xunit;

namespace PotatoMaker.Tests.Services;

public sealed class FfmpegRuntimeValidatorTests
{
    [Fact]
    public void NormalizeBinaryFolder_AcceptsAParentContainingBinDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), $"potatomaker-ffmpeg-folder-{Guid.NewGuid():N}");
        string bin = Path.Combine(root, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "ffmpeg.exe"), string.Empty);
        File.WriteAllText(Path.Combine(bin, "ffprobe.exe"), string.Empty);

        try
        {
            Assert.Equal(bin, FfmpegRuntimeValidator.NormalizeBinaryFolder(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
