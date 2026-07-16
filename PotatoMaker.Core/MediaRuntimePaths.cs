namespace PotatoMaker.Core;

/// <summary>
/// Stable per-user installation paths for media tools provisioned by PotatoMaker.
/// These paths are outside the versioned application directory so app updates do not remove them.
/// </summary>
public static class MediaRuntimePaths
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PotatoMaker",
        "runtimes");

    public static string FfmpegRoot => Path.Combine(Root, "ffmpeg");

    public static string LibVlcRoot => Path.Combine(Root, "libvlc");

    public static void TryRemoveAll()
    {
        try
        {
            string root = Path.GetFullPath(Root);
            string expectedParent = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PotatoMaker"));
            string prefix = expectedParent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!root.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return;

            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch
        {
            // Uninstall cleanup is best effort and must not prevent app removal.
        }
    }
}
