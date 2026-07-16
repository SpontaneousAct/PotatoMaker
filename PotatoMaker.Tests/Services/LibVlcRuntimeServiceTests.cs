using PotatoMaker.GUI.Services;
using Xunit;

namespace PotatoMaker.Tests.Services;

public sealed class LibVlcRuntimeServiceTests
{
    [Fact]
    public void ResolveRuntimeDirectory_AcceptsVlcInstallFolder()
    {
        string tempDirectory = CreateRuntimeLayout(nested: false);
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
    public void ResolveRuntimeDirectory_AcceptsLegacyPackagedLayout()
    {
        string tempDirectory = CreateRuntimeLayout(nested: true);
        try
        {
            string expected = Path.Combine(
                tempDirectory,
                "libvlc",
                Environment.Is64BitProcess ? "win-x64" : "win-x86");

            string? result = LibVlcRuntimeValidator.ResolveRuntimeDirectory(tempDirectory);

            Assert.Equal(Path.GetFullPath(expected), result);
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
        Assert.Equal("VLC 3.0.23", LibVlcRuntimePackage.VersionLabel);
    }

    private static string CreateRuntimeLayout(bool nested)
    {
        string root = Path.Combine(Path.GetTempPath(), $"potatomaker-libvlc-{Guid.NewGuid():N}");
        string runtimeDirectory = nested
            ? Path.Combine(root, "libvlc", Environment.Is64BitProcess ? "win-x64" : "win-x86")
            : root;
        Directory.CreateDirectory(Path.Combine(runtimeDirectory, "plugins"));
        File.WriteAllBytes(Path.Combine(runtimeDirectory, "libvlc.dll"), []);
        File.WriteAllBytes(Path.Combine(runtimeDirectory, "libvlccore.dll"), []);
        return root;
    }
}
