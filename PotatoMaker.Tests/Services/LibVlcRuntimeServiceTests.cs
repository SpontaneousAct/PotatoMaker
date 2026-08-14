using System.IO.Compression;
using PotatoMaker.Core;
using PotatoMaker.GUI.Services;
using Xunit;

namespace PotatoMaker.Tests.Services;

public sealed class LibVlcRuntimeServiceTests
{
    [Fact]
    public void ResolveRuntimeDirectory_AcceptsVlcInstallFolder()
    {
        string tempDirectory = CreateRuntimeLayout();
        try
        {
            string? result = LibVlcRuntimeValidator.ResolveRuntimeDirectory(tempDirectory);

            Assert.Equal(Path.GetFullPath(tempDirectory), result);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ValidateDirectory_RejectsIncompleteFolderWithActionableMessage()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"potatomaker-libvlc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            LibVlcRuntimeValidationResult result = LibVlcRuntimeValidator.ValidateDirectory(tempDirectory);

            Assert.False(result.IsValid);
            Assert.Contains("libvlc.dll", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void OfficialDownloadUsesVideoLanHttpsPage()
    {
        Assert.StartsWith("https://download.videolan.org/", LibVlcRuntimePackage.DownloadUrl);
        Assert.Equal(64, LibVlcRuntimePackage.ArchiveSha256.Length);
    }

    [Fact]
    public void ManagedRuntimeUsesFlatDirectory()
    {
        Assert.Equal(MediaRuntimePaths.LibVlcRoot, LibVlcRuntimePackage.DefaultRuntimeDirectory);

        using var installer = new LibVlcRuntimeInstaller(managedRoot: @"C:\media-tools\libvlc");
        Assert.Equal(@"C:\media-tools\libvlc", installer.RuntimeDirectory);
        Assert.Equal(
            Path.Combine(@"C:\media-tools\libvlc", LibVlcRuntimePackage.RuntimeId),
            installer.LegacyRuntimeDirectory);
    }

    [Fact]
    public async Task ExtractRuntime_WritesVlcContentsDirectlyIntoDestination()
    {
        string root = Path.Combine(Path.GetTempPath(), $"potatomaker-vlc-extract-{Guid.NewGuid():N}");
        string archivePath = Path.Combine(root, "vlc.zip");
        string destination = Path.Combine(root, "runtime");
        Directory.CreateDirectory(root);

        try
        {
            using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, $"{LibVlcRuntimePackage.ArchiveRoot}libvlc.dll", "libvlc");
                WriteEntry(archive, $"{LibVlcRuntimePackage.ArchiveRoot}libvlccore.dll", "core");
                WriteEntry(archive, $"{LibVlcRuntimePackage.ArchiveRoot}plugins/video/plugin.dll", "plugin");
                WriteEntry(archive, $"{LibVlcRuntimePackage.ArchiveRoot}vlc.exe", "ignored");
            }

            await LibVlcRuntimeInstaller.ExtractRuntimeAsync(archivePath, destination);

            Assert.Equal("libvlc", File.ReadAllText(Path.Combine(destination, "libvlc.dll")));
            Assert.Equal("core", File.ReadAllText(Path.Combine(destination, "libvlccore.dll")));
            Assert.Equal("plugin", File.ReadAllText(Path.Combine(destination, "plugins", "video", "plugin.dll")));
            Assert.False(Directory.Exists(Path.Combine(destination, "vlc-3.0.23")));
            Assert.False(File.Exists(Path.Combine(destination, "vlc.exe")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRuntimeLayout()
    {
        string root = Path.Combine(Path.GetTempPath(), $"potatomaker-libvlc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "plugins"));
        File.WriteAllBytes(Path.Combine(root, "libvlc.dll"), []);
        File.WriteAllBytes(Path.Combine(root, "libvlccore.dll"), []);
        return root;
    }

    private static void WriteEntry(ZipArchive archive, string path, string contents)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path);
        using StreamWriter writer = new(entry.Open());
        writer.Write(contents);
    }
}
