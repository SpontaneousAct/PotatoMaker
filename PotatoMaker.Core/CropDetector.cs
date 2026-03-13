using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace PotatoMaker.Core;

/// <summary>
/// Detects crop filters that remove black bars.
/// </summary>
public static partial class CropDetector
{
    private const int CropLimit = 64;
    private const int CropRound = 2;
    private const int CropReset = 0;
    private const int SampleFrames = 72;
    private const double MeaningfulBarPercent = 0.05;
    private const double MinorAxisRemovalPercent = 0.005;
    private const int CenterTolerancePixels = 8;
    private static readonly double[] SampleWindowPercents = [0.10, 0.30, 0.50, 0.70, 0.90];

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
        IReadOnlyList<double> sampleTimes = BuildSampleTimes(startOffset, totalDuration);
        var sampleCrops = new List<CropRect?>(sampleTimes.Count);

        logger.LogInformation(
            "  Sampling crop detection at {Count} point(s): {Times}",
            sampleTimes.Count,
            string.Join(", ", sampleTimes.Select(FormatSeconds)));

        for (int index = 0; index < sampleTimes.Count; index++)
        {
            double seekSeconds = sampleTimes[index];
            string stderr = await RunCropDetectSampleAsync(ffmpegPath, inputPath, seekSeconds, logger, index + 1, sampleTimes.Count, ct);
            IReadOnlyList<CropRect> observedCrops = ParseCropMatches(stderr);

            if (observedCrops.Count == 0)
            {
                logger.LogInformation("  Sample {Index}/{Count}: cropdetect produced no values.", index + 1, sampleTimes.Count);
                foreach (string line in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(3))
                    logger.LogInformation("  ffmpeg: {Line}", line);

                sampleCrops.Add(null);
                continue;
            }

            CropRect? windowCrop = SelectWindowCrop(observedCrops, srcWidth, srcHeight);
            sampleCrops.Add(windowCrop);

            if (windowCrop is { } crop)
            {
                logger.LogInformation(
                    "  Sample {Index}/{Count}: stable candidate {Filter}",
                    index + 1,
                    sampleTimes.Count,
                    crop.ToFilterString());
            }
            else
            {
                logger.LogInformation("  Sample {Index}/{Count}: no stable centered crop.", index + 1, sampleTimes.Count);
            }
        }

        CropRect? finalCrop = SelectFinalCrop(sampleCrops);
        if (finalCrop is not { } cropRect)
        {
            logger.LogInformation("  No consistent black bars detected - full {Width}x{Height} frame is picture.", srcWidth, srcHeight);
            return null;
        }

        CropEvaluation evaluation = EvaluateCrop(cropRect, srcWidth, srcHeight);
        if (!evaluation.IsValid || !evaluation.HasCrop)
        {
            logger.LogInformation("  No consistent black bars detected - full {Width}x{Height} frame is picture.", srcWidth, srcHeight);
            return null;
        }

        if (evaluation.HasPillarbox)
        {
            logger.LogWarning(
                "  Pillarbox: {SrcW}x{SrcH} ({SrcAspect}) -> picture {CropW}x{CropH} ({CropAspect})",
                srcWidth,
                srcHeight,
                AspectLabel(srcWidth, srcHeight),
                evaluation.NormalizedCrop.Width,
                evaluation.NormalizedCrop.Height,
                AspectLabel(evaluation.NormalizedCrop.Width, evaluation.NormalizedCrop.Height));
        }

        if (evaluation.HasLetterbox)
        {
            logger.LogWarning(
                "  Letterbox: {SrcW}x{SrcH} ({SrcAspect}) -> picture {CropW}x{CropH} ({CropAspect})",
                srcWidth,
                srcHeight,
                AspectLabel(srcWidth, srcHeight),
                evaluation.NormalizedCrop.Width,
                evaluation.NormalizedCrop.Height,
                AspectLabel(evaluation.NormalizedCrop.Width, evaluation.NormalizedCrop.Height));
        }

        logger.LogInformation("  Filter: {Filter}", evaluation.NormalizedCrop.ToFilterString());
        return evaluation.NormalizedCrop.ToFilterString();
    }

    public static string AspectLabel(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return "unknown";

        int gcd = Gcd(width, height);
        return $"{width / gcd}:{height / gcd}";
    }

    internal static IReadOnlyList<double> BuildSampleTimes(TimeSpan startOffset, TimeSpan totalDuration)
    {
        if (totalDuration <= TimeSpan.Zero)
            return [Math.Max(0, startOffset.TotalSeconds)];

        return SampleWindowPercents
            .Select(percent => startOffset.TotalSeconds + (totalDuration.TotalSeconds * percent))
            .Select(seconds => Math.Max(0, Math.Round(seconds, 1, MidpointRounding.AwayFromZero)))
            .Distinct()
            .ToArray();
    }

    internal static IReadOnlyList<CropRect> ParseCropMatches(string stderr) =>
        CropRegex()
            .Matches(stderr)
            .Select(match => new CropRect(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture)))
            .ToArray();

    internal static CropRect? SelectWindowCrop(IReadOnlyList<CropRect> observedCrops, int srcWidth, int srcHeight)
    {
        if (observedCrops.Count == 0)
            return null;

        int noCropCount = 0;
        int invalidCount = 0;
        var cropCounts = new Dictionary<CropRect, int>();

        foreach (CropRect observedCrop in observedCrops)
        {
            CropEvaluation evaluation = EvaluateCrop(observedCrop, srcWidth, srcHeight);
            if (!evaluation.IsValid)
            {
                invalidCount++;
                continue;
            }

            if (!evaluation.HasCrop)
            {
                noCropCount++;
                continue;
            }

            cropCounts[evaluation.NormalizedCrop] = cropCounts.GetValueOrDefault(evaluation.NormalizedCrop) + 1;
        }

        if (cropCounts.Count == 0)
            return null;

        KeyValuePair<CropRect, int>[] rankedCrops = cropCounts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key.Width)
            .ThenBy(pair => pair.Key.Height)
            .ToArray();

        KeyValuePair<CropRect, int> bestCrop = rankedCrops[0];
        int totalObservations = noCropCount + invalidCount + cropCounts.Values.Sum();
        int requiredDominance = (totalObservations / 2) + 1;

        if (bestCrop.Value < requiredDominance || bestCrop.Value <= noCropCount)
            return null;

        if (rankedCrops.Length > 1 && rankedCrops[1].Value == bestCrop.Value)
            return null;

        return bestCrop.Key;
    }

    internal static CropRect? SelectFinalCrop(IReadOnlyList<CropRect?> sampleCrops)
    {
        CropRect[] stableCrops = sampleCrops
            .Where(crop => crop is not null)
            .Select(crop => crop!.Value)
            .ToArray();

        if (stableCrops.Length == 0)
            return null;

        KeyValuePair<CropRect, int>[] rankedCrops = stableCrops
            .GroupBy(crop => crop)
            .Select(group => new KeyValuePair<CropRect, int>(group.Key, group.Count()))
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key.Width)
            .ThenBy(pair => pair.Key.Height)
            .ToArray();

        KeyValuePair<CropRect, int> bestCrop = rankedCrops[0];
        int requiredSupport = Math.Max(2, (sampleCrops.Count / 2) + 1);

        if (bestCrop.Value < requiredSupport)
            return null;

        if (rankedCrops.Length > 1 && rankedCrops[1].Value == bestCrop.Value)
            return null;

        return bestCrop.Key;
    }

    internal static CropEvaluation EvaluateCrop(CropRect crop, int srcWidth, int srcHeight)
    {
        if (srcWidth <= 0 || srcHeight <= 0)
            return CropEvaluation.Invalid;

        if (crop.Width <= 0 || crop.Height <= 0 || crop.X < 0 || crop.Y < 0)
            return CropEvaluation.Invalid;

        if (crop.Width > srcWidth || crop.Height > srcHeight)
            return CropEvaluation.Invalid;

        if (crop.X + crop.Width > srcWidth || crop.Y + crop.Height > srcHeight)
            return CropEvaluation.Invalid;

        int removedWidth = srcWidth - crop.Width;
        int removedHeight = srcHeight - crop.Height;

        bool widthCentered = Math.Abs(crop.X - (removedWidth / 2.0)) <= CenterTolerancePixels;
        bool heightCentered = Math.Abs(crop.Y - (removedHeight / 2.0)) <= CenterTolerancePixels;
        bool widthRemovalIsMinor = removedWidth <= MinorAxisRemovalTolerance(srcWidth);
        bool heightRemovalIsMinor = removedHeight <= MinorAxisRemovalTolerance(srcHeight);

        if (!widthCentered && !widthRemovalIsMinor)
            return CropEvaluation.Invalid;

        if (!heightCentered && !heightRemovalIsMinor)
            return CropEvaluation.Invalid;

        bool hasPillarbox = removedWidth >= MeaningfulBarThreshold(srcWidth) && widthCentered;
        bool hasLetterbox = removedHeight >= MeaningfulBarThreshold(srcHeight) && heightCentered;

        if (!hasPillarbox && !hasLetterbox)
            return CropEvaluation.NoCrop;

        int normalizedWidth = hasPillarbox ? crop.Width : srcWidth;
        int normalizedHeight = hasLetterbox ? crop.Height : srcHeight;
        int normalizedX = hasPillarbox ? (srcWidth - normalizedWidth) / 2 : 0;
        int normalizedY = hasLetterbox ? (srcHeight - normalizedHeight) / 2 : 0;

        return new CropEvaluation(
            CropEvaluationKindFrom(hasPillarbox, hasLetterbox),
            new CropRect(normalizedWidth, normalizedHeight, normalizedX, normalizedY));
    }

    private static async Task<string> RunCropDetectSampleAsync(
        string ffmpegPath,
        string inputPath,
        double seekSeconds,
        ILogger logger,
        int sampleIndex,
        int sampleCount,
        CancellationToken ct)
    {
        string seekArg = seekSeconds > 1.0 ? $"-ss {FormatSeconds(seekSeconds)} " : string.Empty;
        string arguments = $"{seekArg}-i \"{inputPath}\" -frames:v {SampleFrames} -vf cropdetect={CropLimit}:{CropRound}:{CropReset} -an -f null NUL";
        logger.LogInformation("  Sample {Index}/{Count}: {Ffmpeg} {Arguments}", sampleIndex, sampleCount, Path.GetFileName(ffmpegPath), arguments);

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = arguments,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi)!;
            using CancellationTokenRegistration registration = ct.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
            });

            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(ct);
            return stderr;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError("  cropdetect failed to launch ffmpeg: {Message}", ex.Message);
            return string.Empty;
        }
    }

    private static string FormatSeconds(double seconds) =>
        seconds.ToString("F1", CultureInfo.InvariantCulture);

    private static int MinorAxisRemovalTolerance(int dimension) =>
        Math.Max(CropRound * 2, (int)Math.Ceiling(dimension * MinorAxisRemovalPercent));

    private static int MeaningfulBarThreshold(int dimension) =>
        (int)Math.Ceiling(dimension * MeaningfulBarPercent);

    private static CropEvaluationKind CropEvaluationKindFrom(bool hasPillarbox, bool hasLetterbox) => (hasPillarbox, hasLetterbox) switch
    {
        (true, true) => CropEvaluationKind.Windowbox,
        (true, false) => CropEvaluationKind.Pillarbox,
        (false, true) => CropEvaluationKind.Letterbox,
        _ => CropEvaluationKind.NoCrop
    };

    [GeneratedRegex(@"crop=(\d+):(\d+):(\d+):(\d+)")]
    private static partial Regex CropRegex();

    private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
}

internal readonly record struct CropRect(int Width, int Height, int X, int Y)
{
    public string ToFilterString() => $"crop={Width}:{Height}:{X}:{Y}";
}

internal readonly record struct CropEvaluation(CropEvaluationKind Kind, CropRect NormalizedCrop)
{
    public static CropEvaluation Invalid => new(CropEvaluationKind.Invalid, default);
    public static CropEvaluation NoCrop => new(CropEvaluationKind.NoCrop, default);

    public bool IsValid => Kind != CropEvaluationKind.Invalid;
    public bool HasCrop => Kind is CropEvaluationKind.Pillarbox or CropEvaluationKind.Letterbox or CropEvaluationKind.Windowbox;
    public bool HasPillarbox => Kind is CropEvaluationKind.Pillarbox or CropEvaluationKind.Windowbox;
    public bool HasLetterbox => Kind is CropEvaluationKind.Letterbox or CropEvaluationKind.Windowbox;
}

internal enum CropEvaluationKind
{
    Invalid,
    NoCrop,
    Pillarbox,
    Letterbox,
    Windowbox
}
