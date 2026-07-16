using System.Diagnostics;

namespace PotatoMaker.Core;

public sealed record FfmpegRuntimeValidationResult(
    bool IsValid,
    string? BinaryFolder,
    string DisplayName,
    string Message)
{
    public static FfmpegRuntimeValidationResult Invalid(string message) =>
        new(false, null, "FFmpeg unavailable", message);
}

/// <summary>
/// Checks that an FFmpeg installation contains the tools and capabilities used by PotatoMaker.
/// </summary>
public static class FfmpegRuntimeValidator
{
    public static async Task<FfmpegRuntimeValidationResult> ValidateAsync(
        string? folder,
        CancellationToken ct = default)
    {
        string? normalizedFolder = NormalizeBinaryFolder(folder);
        if (!string.IsNullOrWhiteSpace(folder) && normalizedFolder is null)
        {
            return FfmpegRuntimeValidationResult.Invalid(
                "That folder does not contain ffmpeg and ffprobe. Choose its bin folder instead.");
        }

        string ffmpeg = ExecutablePath(normalizedFolder, "ffmpeg");
        string ffprobe = ExecutablePath(normalizedFolder, "ffprobe");

        ProcessResult ffmpegVersion = await RunAsync(ffmpeg, "-hide_banner -version", ct).ConfigureAwait(false);
        if (!ffmpegVersion.Succeeded)
            return FfmpegRuntimeValidationResult.Invalid(LaunchFailure("ffmpeg", ffmpegVersion));

        ProcessResult ffprobeVersion = await RunAsync(ffprobe, "-hide_banner -version", ct).ConfigureAwait(false);
        if (!ffprobeVersion.Succeeded)
            return FfmpegRuntimeValidationResult.Invalid(LaunchFailure("ffprobe", ffprobeVersion));

        ProcessResult encoders = await RunAsync(ffmpeg, "-hide_banner -encoders", ct).ConfigureAwait(false);
        ProcessResult decoders = await RunAsync(ffmpeg, "-hide_banner -decoders", ct).ConfigureAwait(false);
        ProcessResult filters = await RunAsync(ffmpeg, "-hide_banner -filters", ct).ConfigureAwait(false);

        if (!encoders.Succeeded || !decoders.Succeeded || !filters.Succeeded)
            return FfmpegRuntimeValidationResult.Invalid("FFmpeg could not report its available codecs and filters.");

        var missing = new List<string>();
        if (!ContainsCapability(encoders.Output, "libsvtav1"))
            missing.Add("SVT-AV1 encoding");
        if (!ContainsCapability(encoders.Output, "aac"))
            missing.Add("AAC audio encoding");
        if (!ContainsCapability(decoders.Output, "libdav1d"))
            missing.Add("dav1d AV1 decoding");
        if (!ContainsCapability(filters.Output, "cropdetect"))
            missing.Add("crop detection");

        if (missing.Count > 0)
        {
            return FfmpegRuntimeValidationResult.Invalid(
                $"This FFmpeg build is missing {string.Join(", ", missing)}. Choose a full GPL build instead.");
        }

        string versionLine = FirstNonEmptyLine(ffmpegVersion.Output);
        string displayName = string.IsNullOrWhiteSpace(versionLine) ? "FFmpeg" : versionLine.Trim();
        string source = normalizedFolder ?? "PATH";
        return new FfmpegRuntimeValidationResult(true, normalizedFolder, displayName, $"Using FFmpeg from {source}.");
    }

    public static string? NormalizeBinaryFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return null;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(folder);
        }
        catch
        {
            return null;
        }

        string[] candidates = [fullPath, Path.Combine(fullPath, "bin")];
        return candidates.FirstOrDefault(FFmpegBinaries.ContainsBinaries);
    }

    private static string ExecutablePath(string? folder, string name) =>
        folder is null
            ? name
            : Path.Combine(folder, OperatingSystem.IsWindows() ? $"{name}.exe" : name);

    private static bool ContainsCapability(string output, string capability) =>
        output.Contains(capability, StringComparison.OrdinalIgnoreCase);

    private static string LaunchFailure(string tool, ProcessResult result)
    {
        if (result.Exception is not null)
            return $"{tool} could not be started: {result.Exception.Message}";

        string detail = FirstNonEmptyLine(result.Output);
        return string.IsNullOrWhiteSpace(detail)
            ? $"{tool} exited with code {result.ExitCode}."
            : $"{tool} failed: {detail}";
    }

    private static async Task<ProcessResult> RunAsync(string fileName, string arguments, CancellationToken ct)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
                return new ProcessResult(-1, string.Empty, new InvalidOperationException("Process did not start."));

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(ct);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

            string output = string.Join(Environment.NewLine, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
            return new ProcessResult(process.ExitCode, output, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new ProcessResult(-1, string.Empty, ex);
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

    private sealed record ProcessResult(int ExitCode, string Output, Exception? Exception)
    {
        public bool Succeeded => Exception is null && ExitCode == 0;
    }
}
