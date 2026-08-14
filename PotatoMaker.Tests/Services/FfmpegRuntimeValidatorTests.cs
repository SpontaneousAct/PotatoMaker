using System.IO.Compression;
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

    [Fact]
    public void ManagedRuntimeUsesFlatBinaryFolder()
    {
        Assert.Equal(MediaRuntimePaths.FfmpegRoot, FfmpegRuntimePackage.DefaultManagedBinaryFolder);

        using var installer = new FfmpegRuntimeInstaller(managedRoot: @"C:\media-tools\ffmpeg");
        Assert.Equal(@"C:\media-tools\ffmpeg", installer.BinaryFolder);
        Assert.Equal(
            Path.Combine(@"C:\media-tools\ffmpeg", FfmpegRuntimePackage.RuntimeId, "bin"),
            installer.LegacyBinaryFolder);
    }

    [Fact]
    public void OfficialDownloadUsesBtbNGitHubHttpsAsset()
    {
        Assert.StartsWith("https://github.com/BtbN/FFmpeg-Builds/", FfmpegRuntimePackage.DownloadUrl);
        Assert.Equal(64, FfmpegRuntimePackage.ArchiveSha256.Length);
    }

    [Fact]
    public async Task ExtractTools_WritesExecutablesDirectlyIntoDestination()
    {
        string root = Path.Combine(Path.GetTempPath(), $"potatomaker-ffmpeg-extract-{Guid.NewGuid():N}");
        string archivePath = Path.Combine(root, "ffmpeg.zip");
        string destination = Path.Combine(root, "runtime");
        Directory.CreateDirectory(root);

        try
        {
            using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "ffmpeg-test/bin/ffmpeg.exe", "ffmpeg");
                WriteEntry(archive, "ffmpeg-test/bin/ffprobe.exe", "ffprobe");
                WriteEntry(archive, "ffmpeg-test/doc/readme.txt", "ignored");
            }

            await FfmpegRuntimeInstaller.ExtractToolsAsync(archivePath, destination);

            Assert.Equal("ffmpeg", File.ReadAllText(Path.Combine(destination, "ffmpeg.exe")));
            Assert.Equal("ffprobe", File.ReadAllText(Path.Combine(destination, "ffprobe.exe")));
            Assert.False(Directory.Exists(Path.Combine(destination, "bin")));
            Assert.False(File.Exists(Path.Combine(destination, "readme.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteEntry(ZipArchive archive, string path, string contents)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path);
        using StreamWriter writer = new(entry.Open());
        writer.Write(contents);
    }
}
