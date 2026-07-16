using System.Xml.Linq;
using Xunit;

namespace PotatoMaker.Tests.Services;

public sealed class PackagingComplianceTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void RequiredThirdPartyLicenseMaterialsAreVersioned()
    {
        string noticesDirectory = Path.Combine(RepositoryRoot, "third_party", "notices");

        Assert.Contains("FFmpeg", File.ReadAllText(Path.Combine(noticesDirectory, "THIRD-PARTY-NOTICES.txt")));
        AssertCanonicalLicense(Path.Combine(noticesDirectory, "licenses", "GPL-2.0.txt"), "Version 2, June 1991");
        AssertCanonicalLicense(Path.Combine(noticesDirectory, "licenses", "GPL-3.0.txt"), "Version 3, 29 June 2007");
        AssertCanonicalLicense(Path.Combine(noticesDirectory, "licenses", "LGPL-2.1.txt"), "Version 2.1, February 1999");
        Assert.True(File.Exists(Path.Combine(noticesDirectory, "licenses", "FFMpegCore-MIT.txt")));
    }

    [Fact]
    public void FfmpegRuntimeDownloadIsPinnedAndReleaseScriptsDoNotBundleIt()
    {
        Assert.StartsWith("https://github.com/BtbN/FFmpeg-Builds/releases/download/", PotatoMaker.Core.FfmpegRuntimePackage.DownloadUrl);
        Assert.Equal(64, PotatoMaker.Core.FfmpegRuntimePackage.ArchiveSha256.Length);
        Assert.True(PotatoMaker.Core.FfmpegRuntimePackage.ArchiveSizeBytes > 0);

        string portableScript = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "publish-portable.ps1"));
        string velopackScript = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "publish-velopack.ps1"));
        string releaseWorkflow = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", "release.yml"));
        Assert.DoesNotContain("FfmpegDir", portableScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FfmpegDir", velopackScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("build-ffmpeg-runtime", releaseWorkflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NativeLibVlcIsNotAPackageOrReleaseAsset()
    {
        string projectPath = Path.Combine(RepositoryRoot, "PotatoMaker.GUI", "PotatoMaker.GUI.csproj");
        XDocument project = XDocument.Load(projectPath);
        string projectText = project.ToString();
        string portableScript = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "publish-portable.ps1"));
        string releaseWorkflow = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", "release.yml"));

        Assert.Contains("LibVLCSharp", projectText, StringComparison.Ordinal);
        Assert.Contains("LibVLCSharp.Avalonia", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("VideoLAN.LibVLC.Windows", projectText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("VlcWindowsX64", projectText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("build-libvlc-source-bundle", releaseWorkflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("libvlc_source", releaseWorkflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Native LibVLC must not be bundled", portableScript, StringComparison.Ordinal);
        Assert.StartsWith("https://download.videolan.org/", PotatoMaker.GUI.Services.LibVlcRuntimePackage.DownloadUrl);
        Assert.Equal(64, PotatoMaker.GUI.Services.LibVlcRuntimePackage.ArchiveSha256.Length);
        Assert.True(PotatoMaker.GUI.Services.LibVlcRuntimePackage.ArchiveSizeBytes > 0);
    }

    private static void AssertCanonicalLicense(string path, string versionMarker)
    {
        string text = File.ReadAllText(path);
        Assert.Contains(versionMarker, text, StringComparison.Ordinal);
        Assert.Contains("NO WARRANTY", text, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PotatoMaker.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the PotatoMaker repository root.");
    }
}
