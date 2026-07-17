using PotatoMaker.Core;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Describes the verified official VLC archive installed for PotatoMaker preview and trimming.
/// </summary>
public static class LibVlcRuntimePackage
{
    public const string RuntimeId = "videolan-vlc-3.0.23-win64";
    public const long ArchiveSizeBytes = 79_893_405;
    public const string ArchiveSha256 = "992d19dbd0b8a7cde9167d2f7780b1ef6f92acc8a71acfa736101a21f35181e1";
    public const string DownloadUrl =
        "https://download.videolan.org/pub/videolan/vlc/3.0.23/win64/vlc-3.0.23-win64.zip";
    public const string ArchiveRoot = "vlc-3.0.23/";

    public static string DefaultManagedRoot => MediaRuntimePaths.LibVlcRoot;

    public static string DefaultRuntimeDirectory => Path.Combine(DefaultManagedRoot, RuntimeId);
}
