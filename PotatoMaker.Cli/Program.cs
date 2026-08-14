using System.Globalization;
using Microsoft.Extensions.Logging;
using PotatoMaker.Cli;
using PotatoMaker.Core;

class Program
{
    static async Task<int> Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new PipelineConsoleLoggerProvider());
        });

        var logger   = loggerFactory.CreateLogger<ProcessingPipeline>();
        var progress = new ConsoleProgressHandler();

        if (!TryParseArguments(args, out CliOptions options, out string? argumentError))
        {
            logger.LogError("Error: {Message}", argumentError);
            PrintUsage();
            return 1;
        }

        if (options.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        FfmpegRuntimeValidationResult runtime = await FfmpegRuntimeLocator.FindAndConfigureAsync();
        if (!runtime.IsValid)
        {
            logger.LogError("A compatible FFmpeg installation is required: {Message}", runtime.Message);
            logger.LogError("Install a full GPL FFmpeg build on PATH, set POTATOMAKER_FFMPEG_DIR, or run the PotatoMaker desktop app to download one.");
            return 2;
        }

        logger.LogInformation("{Message}", runtime.Message);

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

        EncodeSettings settings = new EncodeSettings
        {
            Encoder = options.UseCpu ? EncoderChoice.SvtAv1 : EncoderChoice.Nvenc
        }.WithOutputSizeLimit(options.OutputSizeLimitMb);

        string inputPath = Path.GetFullPath(options.InputPath!);
        if (!InputMediaSupport.TryValidatePath(inputPath, out string validationError))
        {
            logger.LogError("Error: {Message}", validationError);
            return 1;
        }

        if (!options.UseCpu)
        {
            logger.LogInformation("Checking AV1 NVENC support...");
            bool nvencSupported = await Av1NvencSupportProbe.IsSupportedAsync(cts.Token);
            if (!nvencSupported)
            {
                logger.LogError("AV1 NVENC is not available on this machine. Re-run with --cpu to use the libsvtav1 CPU encoder.");
                return 1;
            }

            logger.LogInformation(PipelineEvents.Success, "AV1 NVENC is available.");
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
            _ = await pipeline.RunAsync(analysis, cts.Token);
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

    private static bool TryParseArguments(string[] args, out CliOptions options, out string? error)
    {
        bool useCpu = false;
        bool showHelp = false;
        bool parseOptions = true;
        double outputSizeLimitMb = EncodeSettings.DefaultOutputSizeLimitMb;
        string? inputPath = null;
        error = null;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (parseOptions && argument == "--")
            {
                parseOptions = false;
                continue;
            }

            if (parseOptions &&
                (string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(argument, "-h", StringComparison.OrdinalIgnoreCase)))
            {
                showHelp = true;
                continue;
            }

            if (parseOptions && string.Equals(argument, "--cpu", StringComparison.OrdinalIgnoreCase))
            {
                useCpu = true;
                continue;
            }

            if (parseOptions && string.Equals(argument, "--target-size-mb", StringComparison.OrdinalIgnoreCase))
            {
                if (++index >= args.Length ||
                    !double.TryParse(args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out outputSizeLimitMb) ||
                    !outputSizeLimitMb.Equals(EncodeSettings.NormalizeOutputSizeLimitMb(outputSizeLimitMb)))
                {
                    error = $"--target-size-mb must be between {EncodeSettings.MinOutputSizeLimitMb:0.##} and {EncodeSettings.MaxOutputSizeLimitMb:0.##}.";
                    options = new CliOptions();
                    return false;
                }

                continue;
            }

            if (parseOptions && argument.StartsWith('-'))
            {
                error = $"Unknown option: {argument}";
                options = new CliOptions();
                return false;
            }

            if (inputPath is not null)
            {
                error = "Only one input file can be specified.";
                options = new CliOptions();
                return false;
            }

            inputPath = argument.Trim('"');
        }

        if (!showHelp && string.IsNullOrWhiteSpace(inputPath))
        {
            error = "No input file specified.";
            options = new CliOptions();
            return false;
        }

        options = new CliOptions(useCpu, showHelp, outputSizeLimitMb, inputPath);
        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:  potatomaker [--cpu] [--target-size-mb <MB>] <video_file>");
        Console.WriteLine("        potatomaker \"C:\\clips\\gameplay.mp4\"");
        Console.WriteLine("        potatomaker --cpu --target-size-mb 20 \"C:\\clips\\gameplay.mp4\"");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --cpu                  Use libsvtav1 CPU two-pass encoding instead of av1_nvenc GPU");
        Console.WriteLine(
            $"  --target-size-mb <MB>  Set the per-file upload limit (default: {EncodeSettings.DefaultOutputSizeLimitMb:0.##} MB)");
        Console.WriteLine("  -h, --help             Show this help");
    }

    private sealed record CliOptions(
        bool UseCpu = false,
        bool ShowHelp = false,
        double OutputSizeLimitMb = EncodeSettings.DefaultOutputSizeLimitMb,
        string? InputPath = null);
}
