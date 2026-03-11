using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace PotatoMaker.Core;

/// <summary>
/// Detects crop filters that remove black bars.
/// </summary>
public static class CropDetector
{
    private const int CropLimit = 64;
    private const int CropRound = 2;
    private const int CropReset = 0;
    private const int SampleFrames = 100;
    private const double StartOffsetPercent = 0.05;

    public static async Task<string?> DetectAsync(
        string inputPath,
        TimeSpan totalDuration,
        int srcWidth,
        int srcHeight,
        ILogger logger,
        TimeSpan startOffset,
        CancellationToken ct = default)
    {
        string ffmpegPath = FFmpegBinaries.FfmpegExecutable();
        double seekSecs = startOffset.TotalSeconds + (totalDuration.TotalSeconds * StartOffsetPercent);
        string seekArg = seekSecs > 1.0 ? $"-ss {FormatSeconds(seekSecs)} " : string.Empty;

        string arguments = $"{seekArg}-i \"{inputPath}\" -frames:v {SampleFrames} -vf cropdetect={CropLimit}:{CropRound}:{CropReset} -an -f null NUL";
        logger.LogInformation("  {Ffmpeg} {Arguments}", Path.GetFileName(ffmpegPath), arguments);

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = arguments,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        string stderr;
        try
        {
            using var proc = Process.Start(psi)!;
            using CancellationTokenRegistration reg = ct.Register(() =>
            {
                try
                {
                    if (!proc.HasExited)
                        proc.Kill(entireProcessTree: true);
                }
                catch
                {
                }
            });
            stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError("  cropdetect failed to launch ffmpeg: {Message}", ex.Message);
            return null;
        }

        var matches = Regex.Matches(stderr, @"crop=(\d+):(\d+):(\d+):(\d+)");
        if (matches.Count == 0)
        {
            logger.LogWarning("  cropdetect produced no values.");
            foreach (string line in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(5))
            {
                logger.LogInformation("  ffmpeg: {Line}", line.Trim());
            }
            return null;
        }

        Match last = matches[^1];
        int cropW = int.Parse(last.Groups[1].Value);
        int cropH = int.Parse(last.Groups[2].Value);
        int cropX = int.Parse(last.Groups[3].Value);
        int cropY = int.Parse(last.Groups[4].Value);

        int removedW = srcWidth - cropW;
        int removedH = srcHeight - cropH;
        bool hasPillarbox = removedW >= srcWidth * 0.05 && Math.Abs(cropX - removedW / 2.0) <= 8;
        bool hasLetterbox = removedH >= srcHeight * 0.05 && Math.Abs(cropY - removedH / 2.0) <= 8;

        if (!hasPillarbox && !hasLetterbox)
        {
            logger.LogInformation("  No black bars detected - full {Width}x{Height} frame is picture.", srcWidth, srcHeight);
            return null;
        }

        if (hasPillarbox)
        {
            logger.LogWarning(
                "  Pillarbox: {SrcW}x{SrcH} ({SrcAspect}) -> picture {CropW}x{CropH} ({CropAspect})",
                srcWidth,
                srcHeight,
                AspectLabel(srcWidth, srcHeight),
                cropW,
                cropH,
                AspectLabel(cropW, cropH));
        }

        if (hasLetterbox)
        {
            logger.LogWarning(
                "  Letterbox: {SrcW}x{SrcH} ({SrcAspect}) -> picture {CropW}x{CropH} ({CropAspect})",
                srcWidth,
                srcHeight,
                AspectLabel(srcWidth, srcHeight),
                cropW,
                cropH,
                AspectLabel(cropW, cropH));
        }

        logger.LogInformation("  Filter: crop={CropW}:{CropH}:{CropX}:{CropY}", cropW, cropH, cropX, cropY);
        return $"crop={cropW}:{cropH}:{cropX}:{cropY}";
    }

    public static string AspectLabel(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return "unknown";

        int gcd = Gcd(width, height);
        return $"{width / gcd}:{height / gcd}";
    }

    private static string FormatSeconds(double seconds) =>
        seconds.ToString("F1", CultureInfo.InvariantCulture);

    private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
}
