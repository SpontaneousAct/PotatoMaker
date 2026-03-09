using Microsoft.Extensions.Logging;

namespace PotatoMaker.Core;

/// <summary>
/// Orchestrates encoding using precomputed strategy analysis.
/// Probing and strategy analysis are caller responsibilities.
/// </summary>
public class ProcessingPipeline
{
    private readonly string _inputPath;
    private readonly string _outputDir;
    private readonly string _outputBase;
    private readonly EncodeSettings _settings;
    private readonly VideoInfo _info;
    private readonly ILogger<ProcessingPipeline> _logger;
    private readonly IProgress<EncodeProgress>? _progress;

    public ProcessingPipeline(
        string inputPath, VideoInfo info, EncodeSettings settings,
        ILogger<ProcessingPipeline> logger, IProgress<EncodeProgress>? progress = null, string? outputDirectory = null)
    {
        string fullInputPath = Path.GetFullPath(inputPath);
        InputMediaSupport.ThrowIfInvalidPath(fullInputPath);

        _inputPath = fullInputPath;
        _outputDir = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.GetDirectoryName(fullInputPath) ?? "."
            : Path.GetFullPath(outputDirectory);
        _outputBase = Path.GetFileNameWithoutExtension(fullInputPath);
        _info = info;
        _settings = settings;
        _logger = logger;
        _progress = progress;
    }

    public async Task RunAsync(StrategyAnalysis analysis, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_outputDir);

        double durationSecs = _info.Duration.TotalSeconds;
        long inputSizeBytes = new FileInfo(_inputPath).Length;
        int origWidth = _info.Width;
        int origHeight = _info.Height;

        string durationFmt = _info.Duration.TotalHours >= 1
            ? _info.Duration.ToString(@"h\:mm\:ss\.f")
            : _info.Duration.ToString(@"m\:ss\.f");

        _logger.LogInformation("  Input     : {FileName}", Path.GetFileName(_inputPath));
        _logger.LogInformation("  Duration  : {Duration}  ({Seconds:F1}s)", durationFmt, durationSecs);
        _logger.LogInformation("  Size      : {Size:F1} MB", inputSizeBytes / 1_048_576.0);
        _logger.LogInformation("  Resolution: {Width}x{Height}  ({Aspect})", origWidth, origHeight, CropDetector.AspectLabel(origWidth, origHeight));
        _logger.LogInformation("");

        string pipelineInputPath = Path.GetFullPath(_inputPath);
        if (!string.Equals(analysis.InputPath, pipelineInputPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Strategy analysis input path does not match pipeline input path.");

        _logger.LogInformation("--- Crop Detection ----------------------------------");
        string? cropFilter = analysis.CropFilter;
        _logger.LogInformation("  Crop filter : {CropFilter}", cropFilter ?? "none");
        _logger.LogInformation("");

        _logger.LogInformation("--- Determining Encoding Strategy -------------------------------");
        var encodePlan = analysis.Plan;

        _logger.LogInformation("  Target size   : {Target} MB  (hard limit: {Limit} MB)", _settings.EffectiveTargetMb, _settings.TargetSizeMb);
        _logger.LogInformation("  Audio reserve : {Audio} kbps", _settings.AudioBitrateKbps);
        _logger.LogInformation("  Resolution    : {Resolution}", encodePlan.ResolutionLabel);
        _logger.LogInformation("  Bitrate       : {Bitrate} kbps", encodePlan.VideoBitrateKbps);
        _logger.LogInformation("  Files         : {Parts}", encodePlan.Parts);

        if (encodePlan.VideoBitrateKbps < _settings.MinVideoBitrateKbps)
        {
            _logger.LogWarning("  Video bitrate : {Bitrate} kbps - below preferred minimum {Min} kbps to stay within size target", encodePlan.VideoBitrateKbps, _settings.MinVideoBitrateKbps);
            _logger.LogWarning("  Warning: clip is very long - output quality will be poor.");
        }
        else
        {
            string suffix = encodePlan.Parts > 1 ? "  (per part)" : "";
            _logger.LogInformation(PipelineEvents.Emphasis, "  Video bitrate : {Bitrate} kbps{Suffix}", encodePlan.VideoBitrateKbps, suffix);
        }

        _logger.LogWarning("  Resolution    : {Resolution}", encodePlan.ResolutionLabel);
        _logger.LogInformation("");

        string? videoFilter = analysis.VideoFilter;

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
            InputPath: _inputPath,
            OutputPath: Path.Combine(_outputDir, $"{_outputBase}_discord.mp4"),
            TotalDuration: _info.Duration,
            VideoBitrateKbps: videoBitrateKbps,
            AudioBitrateKbps: _settings.AudioBitrateKbps,
            VideoFilter: videoFilter
        );

        await VideoEncoder.EncodeAsync(job, _settings.Encoder, _logger, _progress, ct: ct);
        PrintSummary([job.OutputPath]);
    }

    private async Task RunSplitAsync(int videoBitrateKbps, string? videoFilter, int parts, double totalSecs, CancellationToken ct)
    {
        double segSecs = totalSecs / parts;

        _logger.LogInformation("--- Split Plan --------------------------------------");
        _logger.LogWarning("  Bitrate too low for single file at full duration.");
        _logger.LogWarning("  Splitting into {Parts} parts - {SegSecs:F1}s each  ({Bitrate} kbps per part)", parts, segSecs, videoBitrateKbps);
        _logger.LogInformation("");

        var outputPaths = new List<string>();

        for (int i = 0; i < parts; i++)
        {
            string outputPath = Path.Combine(_outputDir, $"{_outputBase}_discord_part{i + 1}.mp4");
            outputPaths.Add(outputPath);

            _logger.LogInformation("--- Part {Part}/{Total} ------------------------------------------", i + 1, parts);

            var job = new EncodeJob(
                InputPath: _inputPath,
                OutputPath: outputPath,
                TotalDuration: _info.Duration,
                VideoBitrateKbps: videoBitrateKbps,
                AudioBitrateKbps: _settings.AudioBitrateKbps,
                VideoFilter: videoFilter,
                StartOffsetSecs: i * segSecs,
                SegmentSecs: segSecs
            );

            await VideoEncoder.EncodeAsync(job, _settings.Encoder, _logger, _progress, label: $"[{i + 1}/{parts}] ", ct: ct);
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
            bool fits = outMb <= _settings.TargetSizeMb;
            if (fits)
                _logger.LogInformation(PipelineEvents.Success, "  {File}  -  {Size:F2} MB  OK", Path.GetFileName(path), outMb);
            else
                _logger.LogWarning("  {File}  -  {Size:F2} MB  over target", Path.GetFileName(path), outMb);
        }

        _logger.LogInformation("");
    }
}
