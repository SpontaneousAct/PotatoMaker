using System.Text.Json;
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
        Assert.True(File.Exists(Path.Combine(noticesDirectory, "licenses", "SVT-AV1-BSD-3-Clause-Clear.txt")));
        string nvidiaNotice = File.ReadAllText(Path.Combine(noticesDirectory, "licenses", "NVIDIA-Codec-Headers-MIT.txt"));
        Assert.Contains("Copyright (c) 2010-2024 NVIDIA Corporation", nvidiaNotice);
        Assert.Contains("Copyright (c) 2016", nvidiaNotice);
        Assert.Contains("Jean-loup Gailly", File.ReadAllText(Path.Combine(noticesDirectory, "licenses", "zlib.txt")));
    }

    [Fact]
    public void ApprovedFfmpegSourceManifestPinsRedistributableSourcesAndCapabilities()
    {
        string manifestPath = Path.Combine(
            RepositoryRoot,
            "third_party",
            "ffmpeg",
            "manifests",
            "source-win-x64.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement root = document.RootElement;

        Assert.Equal("GPL-2.0-or-later", root.GetProperty("license").GetString());
        Assert.All(root.GetProperty("sources").EnumerateArray(), source =>
        {
            Assert.StartsWith("https://", source.GetProperty("url").GetString());
            Assert.Equal(64, source.GetProperty("sha256").GetString()!.Length);
        });
        Assert.Contains(root.GetProperty("requiredEncoders").EnumerateArray(), value => value.GetString() == "libsvtav1");
        Assert.Contains(root.GetProperty("requiredEncoders").EnumerateArray(), value => value.GetString() == "av1_nvenc");
        Assert.Contains(root.GetProperty("requiredDecoders").EnumerateArray(), value => value.GetString() == "h264");
        Assert.Contains(root.GetProperty("requiredConfigurationFlags").EnumerateArray(), value => value.GetString() == "--enable-gpl");
        Assert.Contains(root.GetProperty("requiredFilters").EnumerateArray(), value => value.GetString() == "cropdetect");
        Assert.Contains(
            root.GetProperty("forbiddenConfigurationFlags").EnumerateArray(),
            value => value.GetString() == "--enable-nonfree");
    }

    [Fact]
    public void LibVlcManifestPinsBinarySourcePackagingAndExcludedPlugins()
    {
        string manifestPath = Path.Combine(RepositoryRoot, "third_party", "libvlc", "manifests", "win-x64.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement root = document.RootElement;

        Assert.Equal("3.0.23", root.GetProperty("version").GetString());
        Assert.Equal(64, root.GetProperty("nugetSha256").GetString()!.Length);
        Assert.Equal(64, root.GetProperty("sourceSha256").GetString()!.Length);
        Assert.Equal(40, root.GetProperty("packagingCommit").GetString()!.Length);
        Assert.Contains(root.GetProperty("excludedPlugins").EnumerateArray(), value =>
            value.GetString()!.Contains("dolby_surround", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GuiProjectExcludesKnownGplOnlyLibVlcPlugins()
    {
        string projectPath = Path.Combine(RepositoryRoot, "PotatoMaker.GUI", "PotatoMaker.GUI.csproj");
        XDocument project = XDocument.Load(projectPath);
        string projectText = project.ToString();

        Assert.Contains("libdolby_surround_decoder_plugin.dll", projectText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("libheadphone_channel_mixer_plugin.dll", projectText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VlcWindowsX64ExcludeFiles", projectText, StringComparison.Ordinal);
        Assert.Contains("VlcWindowsX86ExcludeFiles", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("CopyLibVlcRuntimeAfterPublish", projectText, StringComparison.Ordinal);
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
