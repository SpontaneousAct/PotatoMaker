using Microsoft.Extensions.Logging;
using PotatoMaker.Cli;
using PotatoMaker.Core;

class Program
{
    static async Task<int> Main(string[] args)
    {
        string? ffmpegFolder = FFmpegBinaries.EnsureConfigured();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new PipelineConsoleLoggerProvider());
        });

        var logger   = loggerFactory.CreateLogger<ProcessingPipeline>();
        var progress = new ConsoleProgressHandler();

        if (!string.IsNullOrWhiteSpace(ffmpegFolder))
            logger.LogInformation("Using bundled FFmpeg binaries from: {Path}", ffmpegFolder);
        else
            logger.LogInformation("Using FFmpeg from PATH.");

        string ffmpegVersionSummary = await FFmpegBinaries.GetVersionSummaryAsync();
        logger.LogInformation("FFmpeg runtime: {Version}", ffmpegVersionSummary);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        Console.WriteLine("+------------------------------------------+");
        Console.WriteLine("|          PotatoMaker  v0.1               |");
        Console.WriteLine("+------------------------------------------+");
        Console.WriteLine();

        var flags = args.Where(a => a.StartsWith('-')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var positional = args.Where(a => !a.StartsWith('-')).ToArray();

        bool useCpu = flags.Contains("--cpu");
        var settings = new EncodeSettings
        {
            Encoder = useCpu ? EncoderChoice.SvtAv1 : EncoderChoice.Nvenc
        };

        if (positional.Length == 0)
        {
            logger.LogError("Error: No input file specified.");
            Console.WriteLine("Usage:  potatomaker [--cpu] <video_file>");
            Console.WriteLine("        potatomaker \"C:\\clips\\gameplay.mp4\"");
            Console.WriteLine("        potatomaker --cpu \"C:\\clips\\gameplay.mp4\"");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --cpu    Use libsvtav1 CPU two-pass encoder (default: av1_nvenc GPU)");
            return 1;
        }

        string inputPath = Path.GetFullPath(positional[0].Trim('"'));
        if (!InputMediaSupport.TryValidatePath(inputPath, out string validationError))
        {
            logger.LogError("Error: {Message}", validationError);
            return 1;
        }

        try
        {
            logger.LogInformation("Probing file...");
            var info = await VideoInfo.ProbeAsync(inputPath, cts.Token);
            logger.LogInformation(PipelineEvents.Success, "Probe complete.");

            logger.LogInformation("Analyzing crop + strategy...");
            var analysis = await StrategyAnalyzer.AnalyzeAsync(inputPath, info, settings, logger, ct: cts.Token);
            logger.LogInformation(PipelineEvents.Success, "Strategy ready.");

            var pipeline = new ProcessingPipeline(inputPath, info, settings, logger, progress);
            await pipeline.RunAsync(analysis, cts.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            logger.LogWarning("Cancelled by user.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            logger.LogError("Fatal error: {Message}", ex.Message);
            return 1;
        }
    }
}
