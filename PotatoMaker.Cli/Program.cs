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
        var encoder = useCpu ? EncoderChoice.SvtAv1 : EncoderChoice.Nvenc;

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

        string inputPath = positional[0].Trim('"');

        if (!File.Exists(inputPath))
        {
            logger.LogError("Error: File not found: {Path}", inputPath);
            return 1;
        }

        try
        {
            var processingPipeline = await ProcessingPipeline.CreateAsync(inputPath, encoder, logger, progress, cts.Token);
            await processingPipeline.RunAsync(cts.Token);
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
