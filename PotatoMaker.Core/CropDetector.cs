using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace PotatoMaker.Core;

public static class CropDetector
{
    private const int    CropLimit          = 64;   // higher than default 24 for NVIDIA limited-range
    private const int    CropRound          = 2;    // AV1 needs even dimensions
    private const int    CropReset          = 0;    // accumulate stable max crop
    private const int    SampleFrames       = 100;
    private const double StartOffsetPercent = 0.05; // skip first 5% of clip

    /// <summary>
    /// Runs FFmpeg cropdetect on <see cref="SampleFrames"/> frames starting at
    /// <see cref="StartOffsetPercent"/> into the clip. Returns a "crop=W:H:X:Y"
    /// filter string, or null if no significant symmetric black bars are found.
    /// </summary>
    public static async Task<string?> DetectAsync(
        string            inputPath,
        TimeSpan          totalDuration,
        int               srcWidth,
        int               srcHeight,
        ILogger           logger,
        CancellationToken ct = default)
    {
        string ffmpegPath = FFmpegBinaries.FfmpegExecutable();
        double seekSecs = totalDuration.TotalSeconds * StartOffsetPercent;
        string seekArg  = seekSecs > 1.0 ? $"-ss {seekSecs:F1} " : "";

        string arguments = $"{seekArg}-i \"{inputPath}\" -frames:v {SampleFrames} -vf cropdetect={CropLimit}:{CropRound}:{CropReset} -an -f null NUL";
        logger.LogInformation("  {Ffmpeg} {Arguments}", Path.GetFileName(ffmpegPath), arguments);

        var psi = new ProcessStartInfo
        {
            FileName              = ffmpegPath,
            Arguments             = arguments,
            RedirectStandardError = true,
            UseShellExecute       = false,
            CreateNoWindow        = true
        };

        string stderr;
        try
        {
            using var proc = Process.Start(psi)!;
            await using var reg = ct.Register(() =>
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            });
            stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError("  cropdetect failed to launch ffmpeg: {Message}", ex.Message);
            return null;
        }

        // cropdetect writes one line per frame:
        //   [Parsed_cropdetect_0 @ 0x...] ... crop=3440:1440:840:0
        // The last line has the most stable accumulated value.
        var matches = Regex.Matches(stderr, @"crop=(\d+):(\d+):(\d+):(\d+)");

        if (matches.Count == 0)
        {
            logger.LogWarning("  cropdetect produced no values.");

            foreach (string l in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(5)) {
                logger.LogInformation("  ffmpeg: {Line}", l.Trim());
            }
            return null;
        }

        var last  = matches[^1];
        int cropW = int.Parse(last.Groups[1].Value);
        int cropH = int.Parse(last.Groups[2].Value);
        int cropX = int.Parse(last.Groups[3].Value);
        int cropY = int.Parse(last.Groups[4].Value);

        int  removedW     = srcWidth  - cropW;
        int  removedH     = srcHeight - cropH;
        bool hasPillarbox = removedW >= srcWidth  * 0.05 && Math.Abs(cropX - removedW / 2.0) <= 8;
        bool hasLetterbox = removedH >= srcHeight * 0.05 && Math.Abs(cropY - removedH / 2.0) <= 8;

        if (!hasPillarbox && !hasLetterbox)
        {
            logger.LogInformation("  No black bars detected — full {Width}x{Height} frame is picture.", srcWidth, srcHeight);
            return null;
        }

        if (hasPillarbox) 
        {
            logger.LogWarning("  Pillarbox: {SrcW}x{SrcH} ({SrcAspect}) -> picture {CropW}x{CropH} ({CropAspect})",
                srcWidth, srcHeight, AspectLabel(srcWidth, srcHeight), cropW, cropH, AspectLabel(cropW, cropH));
        }

        if (hasLetterbox) {
            logger.LogWarning("  Letterbox: {SrcW}x{SrcH} ({SrcAspect}) -> picture {CropW}x{CropH} ({CropAspect})",
                srcWidth, srcHeight, AspectLabel(srcWidth, srcHeight), cropW, cropH, AspectLabel(cropW, cropH));
        }

        logger.LogInformation("  Filter: crop={CropW}:{CropH}:{CropX}:{CropY}", cropW, cropH, cropX, cropY);

        return $"crop={cropW}:{cropH}:{cropX}:{cropY}";
    }

    public static string AspectLabel(int w, int h)
    {
        int g = Gcd(w, h);
        return $"{w / g}:{h / g}";
    }

    private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
}
