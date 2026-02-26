namespace PotatoMaker;

class Program
{
    static async Task<int> Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;          // prevent immediate process kill so we can clean up
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
            ConsoleHelper.WriteColored("Error: No input file specified.", ConsoleColor.Red);
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
            ConsoleHelper.WriteColored($"Error: File not found: {inputPath}", ConsoleColor.Red);
            return 1;
        }

        try
        {
            var crusher = await ProcessingPipeline.CreateAsync(inputPath, encoder, cts.Token);
            await crusher.RunAsync(cts.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            ConsoleHelper.WriteColored("Cancelled by user.", ConsoleColor.Yellow);
            return 130;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            ConsoleHelper.WriteColored($"Fatal error: {ex.Message}", ConsoleColor.Red);
            return 1;
        }
    }
}
