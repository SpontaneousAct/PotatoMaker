using FFMpegCore;
using Microsoft.Extensions.Logging;

namespace PotatoMaker.Core;

/// <summary>
/// Orchestrates the full processing pipeline: probe -> crop detect -> plan -> encode.
/// All bitrate/scale/split planning is delegated to <see cref="EncodePlanner"/>.
/// </summary>
public class ProcessingPipeline
{
    private readonly string _inputPath;
    private readonly string _outputDir;
    private readonly string _outputBase;
    private readonly EncoderChoice _encoder;
    private readonly IMediaAnalysis _probe;
    private readonly ILogger<ProcessingPipeline> _logger;
    private readonly IProgress<EncodeProgress>? _progress;

    private ProcessingPipeline(
        string inputPath, EncoderChoice encoder, IMediaAnalysis probe,
        ILogger<ProcessingPipeline> logger, IProgress<EncodeProgress>? progress)
    {
        _inputPath  = inputPath;
        _outputDir  = Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? ".";
        _outputBase = Path.GetFileNameWithoutExtension(inputPath);
        _encoder    = encoder;
        _probe      = probe;
        _logger     = logger;
        _progress   = progress;
    }

    public static async Task<ProcessingPipeline> CreateAsync(
        string inputPath, EncoderChoice encoder,
        ILogger<ProcessingPipeline> logger, IProgress<EncodeProgress>? progress = null,
        CancellationToken ct = default)
    {
        logger.LogInformation("Probing file...");
        ct.ThrowIfCancellationRequested();
        var probe = await FFProbe.AnalyseAsync(inputPath);
        logger.LogInformation(PipelineEvents.Success, "Probe complete.");
        return new ProcessingPipeline(inputPath, encoder, probe, logger, progress);
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

        _logger.LogInformation("  Input     : {FileName}", Path.GetFileName(_inputPath));
        _logger.LogInformation("  Duration  : {Duration}  ({Seconds:F1}s)", durationFmt, durationSecs);
        _logger.LogInformation("  Size      : {Size:F1} MB", inputSizeBytes / 1_048_576.0);
        _logger.LogInformation("  Resolution: {Width}x{Height}  ({Aspect})", origWidth, origHeight, CropDetector.AspectLabel(origWidth, origHeight));
        _logger.LogInformation("");

        _logger.LogInformation("--- Crop Detection ----------------------------------");
        string? cropFilter = await CropDetector.DetectAsync(_inputPath, _probe.Duration, origWidth, origHeight, _logger, ct);
        _logger.LogInformation("");

        _logger.LogInformation("--- Determining Encoding Strategy -------------------------------");
        var encodePlan = EncodePlanner.PlanStrategy(durationSecs, origHeight);

        _logger.LogInformation("  Target size   : {Target} MB  (hard limit: {Limit} MB)", EncodePlanner.EffectiveTargetMb, EncodePlanner.TargetSizeMb);
        _logger.LogInformation("  Audio reserve : {Audio} kbps", EncodePlanner.AudioBitrateKbps);
        _logger.LogInformation("  Resolution    : {Resolution}", encodePlan.ResolutionLabel);
        _logger.LogInformation("  Bitrate       : {Bitrate} kbps", encodePlan.VideoBitrateKbps);
        _logger.LogInformation("  Files         : {Parts}", encodePlan.Parts);

        if (encodePlan.VideoBitrateKbps <= EncodePlanner.MinVideoBitrateKbps)
        {
            _logger.LogError("  Video bitrate : {Bitrate} kbps — clamped to {Min} kbps", encodePlan.VideoBitrateKbps, EncodePlanner.MinVideoBitrateKbps);
            _logger.LogError("  Warning: clip is very long - output quality will be poor.");
        }
        else
        {
            string suffix = encodePlan.Parts > 1 ? "  (per part)" : "";
            _logger.LogInformation(PipelineEvents.Emphasis, "  Video bitrate : {Bitrate} kbps{Suffix}", encodePlan.VideoBitrateKbps, suffix);
        }

        _logger.LogWarning("  Resolution    : {Resolution}", encodePlan.ResolutionLabel);
        _logger.LogInformation("");

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
        _logger.LogInformation("--- Encoding ----------------------------------------");

        var job = new EncodeJob(
            InputPath:        _inputPath,
            OutputPath:       Path.Combine(_outputDir, $"{_outputBase}_discord.mp4"),
            TotalDuration:    _probe.Duration,
            VideoBitrateKbps: videoBitrateKbps,
            AudioBitrateKbps: EncodePlanner.AudioBitrateKbps,
            VideoFilter:      videoFilter
        );

        await VideoEncoder.EncodeAsync(job, _encoder, _logger, _progress, ct: ct);
        PrintSummary([job.OutputPath]);
    }

    private async Task RunSplitAsync(int videoBitrateKbps, string? videoFilter, int parts, double totalSecs, CancellationToken ct)
    {
        double segSecs = totalSecs / parts;

        _logger.LogInformation("--- Split Plan --------------------------------------");
        _logger.LogWarning("  Bitrate too low for single file at full duration.");
        _logger.LogWarning("  Splitting into {Parts} parts — {SegSecs:F1}s each  ({Bitrate} kbps per part)", parts, segSecs, videoBitrateKbps);
        _logger.LogInformation("");

        var outputPaths = new List<string>();

        for (int i = 0; i < parts; i++)
        {
            string outputPath = Path.Combine(_outputDir, $"{_outputBase}_discord_part{i + 1}.mp4");
            outputPaths.Add(outputPath);

            _logger.LogInformation("--- Part {Part}/{Total} ------------------------------------------", i + 1, parts);

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

            await VideoEncoder.EncodeAsync(job, _encoder, _logger, _progress, label: $"[{i + 1}/{parts}] ", ct: ct);
        }

        PrintSummary(outputPaths);
    }

    private void PrintSummary(IEnumerable<string> outputPaths)
    {
        _logger.LogInformation("");
        _logger.LogInformation("--- Output ------------------------------------------");
        foreach (string path in outputPaths)
        {
            if (!File.Exists(path)) continue;
            double outMb = new FileInfo(path).Length / 1_048_576.0;
            bool   fits  = outMb <= EncodePlanner.TargetSizeMb;
            if (fits)
                _logger.LogInformation(PipelineEvents.Success, "  {File}  -  {Size:F2} MB  ✓", Path.GetFileName(path), outMb);
            else
                _logger.LogWarning("  {File}  -  {Size:F2} MB  ⚠  over target", Path.GetFileName(path), outMb);
        }
        _logger.LogInformation("");
    }
}
