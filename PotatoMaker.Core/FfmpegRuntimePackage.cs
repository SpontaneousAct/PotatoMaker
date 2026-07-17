namespace PotatoMaker.Core;

/// <summary>
/// Describes the verified Windows FFmpeg package offered by PotatoMaker.
/// The archive is downloaded directly from the upstream build provider.
/// </summary>
public static class FfmpegRuntimePackage
{
    public const string RuntimeId = "btbn-ffmpeg-n8.1.2-21-gce3c09c101-win64-gpl-8.1";
    public const long ArchiveSizeBytes = 166_372_072;
    public const string ArchiveSha256 = "682361e32c9631caec09e5d9f09077101c9ed90c14e275f62014fefa6d397990";
    public const string DownloadUrl =
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-06-30-13-34/ffmpeg-n8.1.2-21-gce3c09c101-win64-gpl-8.1.zip";
    public static string DefaultManagedRoot => MediaRuntimePaths.FfmpegRoot;

    public static string DefaultManagedBinaryFolder => Path.Combine(DefaultManagedRoot, RuntimeId, "bin");
}
