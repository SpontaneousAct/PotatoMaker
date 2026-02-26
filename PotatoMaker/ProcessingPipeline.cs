using FFMpegCore;

namespace PotatoMaker;

/// <summary>
/// Orchestrates the full processing pipeline: probe -> crop detect -> plan -> encode.
/// All bitrate/scale/split planning is delegated to <see cref="EncodePlanner"/>.
/// </summary>
class ProcessingPipeline
{
    private readonly string _inputPath;
    private readonly string _outputDir;
    private readonly string _outputBase;
    private readonly EncoderChoice _encoder;
    private readonly IMediaAnalysis _probe;

    private ProcessingPipeline(string inputPath, EncoderChoice encoder, IMediaAnalysis probe)
    {
        _inputPath  = inputPath;
        _outputDir  = Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? ".";
        _outputBase = Path.GetFileNameWithoutExtension(inputPath);
        _encoder    = encoder;
        _probe      = probe;
    }

    public static async Task<ProcessingPipeline> CreateAsync(string inputPath, EncoderChoice encoder, CancellationToken ct = default)
    {
        Console.Write("Probing file... ");
        ct.ThrowIfCancellationRequested();
        var probe = await FFProbe.AnalyseAsync(inputPath);
        ConsoleHelper.WriteColored("done.", ConsoleColor.Green);
        Console.WriteLine();
        return new ProcessingPipeline(inputPath, encoder, probe);
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        double durationSecs   = _probe.Duration.TotalSeconds;
        long   inputSizeBytes = new FileInfo(_inputPath).Length;
        int    origWidth      = _probe.PrimaryVideoStream?.Width  ?? 0;
        int    origHeight     = _probe.PrimaryVideoStream?.Height ?? 0;

        string durationFmt = _probe.Duration.TotalHours >= 1
            ? _probe.Duration.ToString(@"h\:mm\:ss\.f")
            : _probe.Duration.ToString(@"m\:ss\.f");

        Console.WriteLine($"  Input     : {Path.GetFileName(_inputPath)}");
        Console.WriteLine($"  Duration  : {durationFmt}  ({durationSecs:F1}s)");
        Console.WriteLine($"  Size      : {inputSizeBytes / 1_048_576.0:F1} MB");
        Console.WriteLine($"  Resolution: {origWidth}x{origHeight}  ({CropDetector.AspectLabel(origWidth, origHeight)})");
        Console.WriteLine();

        Console.WriteLine("--- Crop Detection ----------------------------------");
        string? cropFilter = await CropDetector.DetectAsync(_inputPath, _probe.Duration, origWidth, origHeight, ct);
        Console.WriteLine();

        Console.WriteLine("--- Determining Encoding Strategy -------------------------------");
        var encodePlan = EncodePlanner.PlanStrategy(durationSecs, origHeight);

        Console.WriteLine($"  Target size   : {EncodePlanner.EffectiveTargetMb} MB  (hard limit: {EncodePlanner.TargetSizeMb} MB)");
        Console.WriteLine($"  Audio reserve : {EncodePlanner.AudioBitrateKbps} kbps");
        Console.WriteLine($"  Resolution    : {encodePlan.ResolutionLabel}");
        Console.WriteLine($"  Bitrate : {encodePlan.VideoBitrateKbps} kbps");
        Console.WriteLine($"  Files : {encodePlan.Parts}");

        if (encodePlan.VideoBitrateKbps <= EncodePlanner.MinVideoBitrateKbps)
        {
            ConsoleHelper.WriteColored($"  Video bitrate : {encodePlan.VideoBitrateKbps} kbps — clamped to {EncodePlanner.MinVideoBitrateKbps} kbps", ConsoleColor.Red);
            ConsoleHelper.WriteColored("  Warning: clip is very long - output quality will be poor.", ConsoleColor.Red);
        }
        else
        {
            ConsoleHelper.WriteColored($"  Video bitrate : {encodePlan.VideoBitrateKbps} kbps" + (encodePlan.Parts > 1 ? "  (per part)" : ""), ConsoleColor.Cyan);
        }

        ConsoleHelper.WriteColored($"  Resolution    : {encodePlan.ResolutionLabel}", ConsoleColor.Yellow);
        Console.WriteLine();

        string? videoFilter = EncodePlanner.BuildVideoFilter(cropFilter, encodePlan.ScaleFilter);

        if (encodePlan.Parts == 1)
        {
            await RunSingleAsync(encodePlan.VideoBitrateKbps, videoFilter, ct);
        }
        else
        {
            await RunSplitAsync(encodePlan.VideoBitrateKbps, videoFilter, encodePlan.Parts, durationSecs, ct);
        }
    }

    private async Task RunSingleAsync(int videoBitrateKbps, string? videoFilter, CancellationToken ct)
    {
        Console.WriteLine("--- Encoding ----------------------------------------");

        var job = new EncodeJob(
            InputPath:        _inputPath,
            OutputPath:       Path.Combine(_outputDir, $"{_outputBase}_discord.mp4"),
            TotalDuration:    _probe.Duration,
            VideoBitrateKbps: videoBitrateKbps,
            AudioBitrateKbps: EncodePlanner.AudioBitrateKbps,
            VideoFilter:      videoFilter
        );

        await VideoEncoder.EncodeAsync(job, _encoder, ct: ct);
        PrintSummary([job.OutputPath]);
    }

    private async Task RunSplitAsync(int videoBitrateKbps, string? videoFilter, int parts, double totalSecs, CancellationToken ct)
    {
        double segSecs = totalSecs / parts;

        Console.WriteLine("--- Split Plan --------------------------------------");
        ConsoleHelper.WriteColored("  Bitrate too low for single file at full duration.", ConsoleColor.Yellow);
        ConsoleHelper.WriteColored($"  Splitting into {parts} parts — {segSecs:F1}s each  ({videoBitrateKbps} kbps per part)", ConsoleColor.Yellow);
        Console.WriteLine();

        var outputPaths = new List<string>();

        for (int i = 0; i < parts; i++)
        {
            string outputPath = Path.Combine(_outputDir, $"{_outputBase}_discord_part{i + 1}.mp4");
            outputPaths.Add(outputPath);

            Console.WriteLine($"--- Part {i + 1}/{parts} ------------------------------------------");

            var job = new EncodeJob(
                InputPath:        _inputPath,
                OutputPath:       outputPath,
                TotalDuration:    _probe.Duration,
                VideoBitrateKbps: videoBitrateKbps,
                AudioBitrateKbps: EncodePlanner.AudioBitrateKbps,
                VideoFilter:      videoFilter,
                StartOffsetSecs:  i * segSecs,
                SegmentSecs:      segSecs
            );

            await VideoEncoder.EncodeAsync(job, _encoder, label: $"[{i + 1}/{parts}] ", ct: ct);
            Console.WriteLine();
        }

        PrintSummary(outputPaths);
    }

    private static void PrintSummary(IEnumerable<string> outputPaths)
    {
        Console.WriteLine();
        Console.WriteLine("--- Output ------------------------------------------");
        foreach (string path in outputPaths)
        {
            if (!File.Exists(path)) continue;
            double outMb = new FileInfo(path).Length / 1_048_576.0;
            bool   fits  = outMb <= EncodePlanner.TargetSizeMb;
            ConsoleHelper.WriteColored($"  {Path.GetFileName(path)}  -  {outMb:F2} MB  {(fits ? "✓" : "⚠  over target")}", fits ? ConsoleColor.Green : ConsoleColor.Yellow);
        }
        Console.WriteLine();
    }
}
