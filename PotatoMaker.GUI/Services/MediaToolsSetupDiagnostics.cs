using System.Text;
using PotatoMaker.Core;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Persists automatic-setup failures because the setup window cannot provide a full stack trace.
/// </summary>
internal static class MediaToolsSetupDiagnostics
{
    public static string? TryWriteFailure(
        Exception exception,
        MediaToolsDownloadProgress? progress,
        string? logDirectory = null)
    {
        try
        {
            string directory = logDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PotatoMaker",
                "Logs");
            Directory.CreateDirectory(directory);
            string logPath = Path.Combine(directory, "media-tools-setup.log");

            var entry = new StringBuilder()
                .AppendLine(new string('=', 72))
                .Append("Time (UTC): ").AppendLine(DateTimeOffset.UtcNow.ToString("u"))
                .Append("FFmpeg source: ").AppendLine(FfmpegRuntimePackage.DownloadUrl)
                .Append("FFmpeg destination: ").AppendLine(MediaRuntimePaths.FfmpegRoot)
                .Append("VLC source: ").AppendLine(LibVlcRuntimePackage.DownloadUrl)
                .Append("VLC destination: ").AppendLine(MediaRuntimePaths.LibVlcRoot);

            if (progress is not null)
            {
                entry.Append("Last stage: ")
                    .Append(progress.Tool)
                    .Append(" — ")
                    .Append(progress.Stage)
                    .Append(" — ")
                    .Append(progress.Percent)
                    .AppendLine("%");
            }

            entry.AppendLine(exception.ToString());
            File.AppendAllText(logPath, entry.ToString(), Encoding.UTF8);
            return logPath;
        }
        catch
        {
            return null;
        }
    }
}
