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

    private static string CreateRuntimeLayout()
    {
        string root = Path.Combine(Path.GetTempPath(), $"potatomaker-libvlc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "plugins"));
        File.WriteAllBytes(Path.Combine(root, "libvlc.dll"), []);
        File.WriteAllBytes(Path.Combine(root, "libvlccore.dll"), []);
        return root;
    }
}
