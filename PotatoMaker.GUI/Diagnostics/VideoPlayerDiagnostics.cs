using System.Text;

namespace PotatoMaker.GUI.Diagnostics;

internal static class VideoPlayerDiagnostics
{
    private const long MaxLogBytes = 5 * 1024 * 1024;
    private static readonly object Sync = new();
    private static bool _initialized;
    private static int _writeCounter;

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PotatoMaker",
        "logs",
        "video-player.log");

    public static void Log(string source, string message)
    {
        try
        {
            lock (Sync)
            {
                EnsureInitialized();
                if ((_writeCounter++ & 0x3F) == 0)
                    RotateIfNeeded();

                File.AppendAllText(
                    LogPath,
                    $"{DateTimeOffset.Now:O} [T{Environment.CurrentManagedThreadId}] {source}: {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics must never break playback flow.
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
            return;

        string? directory = Path.GetDirectoryName(LogPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        RotateIfNeeded();
        File.AppendAllText(
            LogPath,
            $"{Environment.NewLine}=== Session {DateTimeOffset.Now:O} pid={Environment.ProcessId} ==={Environment.NewLine}",
            Encoding.UTF8);

        _initialized = true;
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogPath))
            return;

        var fileInfo = new FileInfo(LogPath);
        if (fileInfo.Length <= MaxLogBytes)
            return;

        string archivedPath = Path.Combine(
            fileInfo.DirectoryName ?? string.Empty,
            $"video-player-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        File.Move(LogPath, archivedPath, overwrite: true);
    }
}
