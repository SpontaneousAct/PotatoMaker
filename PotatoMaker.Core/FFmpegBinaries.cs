using FFMpegCore;
using System.Diagnostics;

namespace PotatoMaker.Core;

/// <summary>
/// Resolves FFmpeg/FFprobe binaries from a bundled folder when present,
/// and falls back to PATH otherwise.
/// </summary>
public static class FFmpegBinaries
{
    private const string FfmpegDirEnvironmentVariable = "POTATOMAKER_FFMPEG_DIR";

    private static readonly object Sync = new();
    private static bool _configured;
    private static string? _binaryFolder;
    private static readonly SemaphoreSlim VersionSync = new(1, 1);
    private static string? _versionSummary;

    /// <summary>
    /// Configures FFMpegCore global options once per process.
    /// Returns the resolved binary folder when bundled binaries are found; otherwise null.
    /// </summary>
    public static string? EnsureConfigured()
    {
        lock (Sync)
        {
            if (_configured)
                return _binaryFolder;

            _binaryFolder = ResolveBinaryFolder();
            if (!string.IsNullOrWhiteSpace(_binaryFolder))
                GlobalFFOptions.Configure(options => options.BinaryFolder = _binaryFolder);

            _configured = true;
            return _binaryFolder;
        }
    }

    public static string FfmpegExecutable() => ResolveExecutablePath("ffmpeg");

    public static string FfprobeExecutable() => ResolveExecutablePath("ffprobe");

    /// <summary>
    /// Returns a cached one-line summary of ffmpeg/ffprobe versions and source location.
    /// </summary>
    public static async Task<string> GetVersionSummaryAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(_versionSummary))
            return _versionSummary;

        await VersionSync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrWhiteSpace(_versionSummary))
                return _versionSummary;

            string ffmpegPath = FfmpegExecutable();
            string ffprobePath = FfprobeExecutable();
            string ffmpegVersion = await ReadVersionLineAsync(ffmpegPath, ct).ConfigureAwait(false) ?? "unavailable";
            string ffprobeVersion = await ReadVersionLineAsync(ffprobePath, ct).ConfigureAwait(false) ?? "unavailable";
            string source = !string.IsNullOrWhiteSpace(_binaryFolder) ? _binaryFolder : "PATH";

            _versionSummary = $"source={source}; {ffmpegVersion}; {ffprobeVersion}";
            return _versionSummary;
        }
        finally
        {
            VersionSync.Release();
        }
    }

    private static string ResolveExecutablePath(string name)
    {
        string? folder = EnsureConfigured();
        string? bundled = FindExecutableInFolder(folder, name);
        return bundled ?? name;
    }

    private static string? ResolveBinaryFolder()
    {
        string[] candidates =
        [
            Environment.GetEnvironmentVariable(FfmpegDirEnvironmentVariable) ?? string.Empty,
            Path.Combine(AppContext.BaseDirectory, "ffmpeg"),
            AppContext.BaseDirectory,
            Path.Combine(Environment.CurrentDirectory, "ffmpeg")
        ];

        foreach (string candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            string fullPath = Path.GetFullPath(candidate);
            if (ContainsBundledBinaries(fullPath))
                return fullPath;
        }

        return null;
    }

    private static bool ContainsBundledBinaries(string folder) =>
        FindExecutableInFolder(folder, "ffmpeg") is not null &&
        FindExecutableInFolder(folder, "ffprobe") is not null;

    private static string? FindExecutableInFolder(string? folder, string name)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return null;

        string executableName = OperatingSystem.IsWindows() ? $"{name}.exe" : name;
        string archSubfolder = Environment.Is64BitProcess ? "x64" : "x86";

        string[] paths =
        [
            Path.Combine(folder, archSubfolder, executableName),
            Path.Combine(folder, executableName)
        ];

        return paths.FirstOrDefault(File.Exists);
    }

    private static async Task<string?> ReadVersionLineAsync(string executablePath, CancellationToken ct)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
                return null;

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);
            string raw = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
            string firstLine = FirstNonEmptyLine(raw);

            if (string.IsNullOrWhiteSpace(firstLine))
                return null;

            string toolLabel = Path.GetFileNameWithoutExtension(executablePath).ToLowerInvariant();
            return firstLine.StartsWith(toolLabel, StringComparison.OrdinalIgnoreCase)
                ? firstLine.Trim()
                : $"{toolLabel} {firstLine.Trim()}";
        }
        catch
        {
            return null;
        }
    }

    private static string FirstNonEmptyLine(string value)
    {
        using var reader = new StringReader(value);
        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
                return line;
        }

        return string.Empty;
    }
}
