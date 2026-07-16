using PotatoMaker.Core;
using Xunit;

namespace PotatoMaker.Tests.Services;

public sealed class FfmpegRuntimeLocatorTests
{
    [Fact]
    public void CandidateFolders_DoNotTreatMissingPreferredFolderAsPathBeforeManagedRuntime()
    {
        IReadOnlyList<string?> candidates = FfmpegRuntimeLocator.BuildCandidateFolders(
            preferredFolder: null,
            environmentFolder: null,
            managedFolder: "managed",
            legacyPackagedFolder: "legacy");

        Assert.Equal(["managed", null, "legacy"], candidates);
    }

    [Fact]
    public void CandidateFolders_PreferUserChoiceThenEnvironmentThenManagedRuntime()
    {
        IReadOnlyList<string?> candidates = FfmpegRuntimeLocator.BuildCandidateFolders(
            preferredFolder: "chosen",
            environmentFolder: "environment",
            managedFolder: "managed",
            legacyPackagedFolder: "legacy");

        Assert.Equal(["chosen", "environment", "managed", null, "legacy"], candidates);
    }

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
