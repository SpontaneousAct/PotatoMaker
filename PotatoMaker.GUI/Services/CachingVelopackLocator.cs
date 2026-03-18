using NuGet.Versioning;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using System.IO.Compression;
using System.Xml.Linq;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Wraps Velopack's Windows locator and caches local package discovery for the current process.
/// </summary>
internal sealed class CachingVelopackLocator : IVelopackLocator
{
    private readonly object _cacheGate = new();
    private readonly IVelopackLocator _inner;
    private List<VelopackAsset>? _localPackages;
    private VelopackAsset? _latestLocalFullPackage;
    private bool _hasLoadedLocalPackages;

    private CachingVelopackLocator(IVelopackLocator inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public string? AppId => _inner.AppId;

    public string? RootAppDir => _inner.RootAppDir;

    public string? PackagesDir => _inner.PackagesDir;

    public string? AppContentDir => _inner.AppContentDir;

    public string? AppTempDir => _inner.AppTempDir;

    public string? UpdateExePath => _inner.UpdateExePath;

    public SemanticVersion? CurrentlyInstalledVersion => _inner.CurrentlyInstalledVersion;

    public string? ThisExeRelativePath => _inner.ThisExeRelativePath;

    public string? Channel => _inner.Channel;

    public IVelopackLogger Log => _inner.Log;

    public bool IsPortable => _inner.IsPortable;

    public string ProcessExePath => _inner.ProcessExePath;

    public uint ProcessId => _inner.ProcessId;

    public static IVelopackLocator? CreateForCurrentProcess()
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(Environment.ProcessPath))
            return null;

        var inner = new WindowsVelopackLocator(Environment.ProcessPath, (uint)Environment.ProcessId, customLog: null);
        return new CachingVelopackLocator(inner);
    }

    public List<VelopackAsset> GetLocalPackages()
    {
        EnsureLocalPackagesLoaded();
        return _localPackages is null ? [] : [.. _localPackages];
    }

    public VelopackAsset? GetLatestLocalFullPackage()
    {
        EnsureLocalPackagesLoaded();
        return _latestLocalFullPackage;
    }

    public Guid? GetOrCreateStagedUserId() => _inner.GetOrCreateStagedUserId();

    public void InvalidateLocalPackageCache()
    {
        lock (_cacheGate)
        {
            _localPackages = null;
            _latestLocalFullPackage = null;
            _hasLoadedLocalPackages = false;
        }
    }

    private List<VelopackAsset> LoadLocalPackages()
    {
        try
        {
            if (CurrentlyInstalledVersion is null || string.IsNullOrWhiteSpace(PackagesDir) || !Directory.Exists(PackagesDir))
                return [];

            List<VelopackAsset> packages = [];
            foreach (string packagePath in Directory.EnumerateFiles(PackagesDir, "*.nupkg"))
            {
                try
                {
                    if (TryReadLocalPackage(packagePath) is { } asset)
                        packages.Add(asset);
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, "Error while reading local package '" + packagePath + "'.");
                }
            }

            return packages;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while reading local packages.");
            return [];
        }
    }

    private void EnsureLocalPackagesLoaded()
    {
        if (_hasLoadedLocalPackages)
            return;

        lock (_cacheGate)
        {
            if (_hasLoadedLocalPackages)
                return;

            List<VelopackAsset> packages = LoadLocalPackages();
            _localPackages = packages;
            _latestLocalFullPackage = packages
                .Where(asset => asset.Type == VelopackAssetType.Full)
                .OrderByDescending(asset => asset.Version)
                .FirstOrDefault();
            _hasLoadedLocalPackages = true;
        }
    }

    private static VelopackAsset? TryReadLocalPackage(string packagePath)
    {
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry? manifestEntry = package.Entries
            .FirstOrDefault(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is null)
            return null;

        using Stream stream = manifestEntry.Open();
        XDocument document = XDocument.Load(stream, LoadOptions.None);
        XElement? metadata = document.Root?
            .Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "metadata", StringComparison.OrdinalIgnoreCase));
        if (metadata is null)
            return null;

        string? packageId = GetMetadataValue(metadata, "id");
        string? versionText = GetMetadataValue(metadata, "version");
        if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(versionText))
            return null;

        return new VelopackAsset
        {
            PackageId = packageId.Trim(),
            Version = SemanticVersion.Parse(versionText.Trim()),
            Type = IsDeltaFile(packagePath) ? VelopackAssetType.Delta : VelopackAssetType.Full,
            FileName = Path.GetFileName(packagePath),
            Size = new FileInfo(packagePath).Length,
            SHA1 = string.Empty,
            SHA256 = string.Empty,
            NotesMarkdown = GetMetadataValue(metadata, "releaseNotes") ?? string.Empty,
            NotesHTML = GetMetadataValue(metadata, "releaseNotesHtml") ?? string.Empty
        };
    }

    private static string? GetMetadataValue(XElement metadata, string name) =>
        metadata.Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    private static bool IsDeltaFile(string packagePath) =>
        Path.GetFileNameWithoutExtension(packagePath)
            .EndsWith("-delta", StringComparison.OrdinalIgnoreCase);
}
