using FFMpegCore;
using FFMpegCore.Enums;

namespace PotatoMaker;

enum EncoderChoice { Nvenc, Libx265 }

static class VideoEncoder
{
    public static async Task EncodeAsync(EncodeJob job, EncoderChoice encoder, string label = "")
    {
        if (encoder == EncoderChoice.Libx265)
        {
            Console.WriteLine($"  Encoder: libx265 (CPU two-pass)");
            Console.WriteLine();
            await EncodeSoftwareTwoPassAsync(job, label);
            ConsoleHelper.WriteColored("  ✓ libx265 encode complete.", ConsoleColor.Green);
            return;
        }

        Console.Write("  Trying hevc_nvenc... ");

        try
        {
            await EncodeNvencAsync(job, label);
            ConsoleHelper.WriteColored("  ✓ NVENC encode complete.", ConsoleColor.Green);
        }
        catch (Exception ex)
        {
            ConsoleHelper.WriteColored("not available.", ConsoleColor.Yellow);
            ConsoleHelper.WriteColored($"    Reason: {ex.Message.Split('\n')[0].Trim()}", ConsoleColor.Yellow);
            Console.WriteLine();
            Console.WriteLine("  Falling back to libx265 two-pass (CPU)...");
            Console.WriteLine();
            await EncodeSoftwareTwoPassAsync(job, label);
            ConsoleHelper.WriteColored("  ✓ libx265 encode complete.", ConsoleColor.Green);
        }
    }

    private static async Task EncodeNvencAsync(EncodeJob job, string label)
    {
        int bufsize = job.VideoBitrateKbps * 2;

        string extraArgs = $"-rc vbr -maxrate {job.VideoBitrateKbps}k -bufsize {bufsize}k -preset p5 -tag:v hvc1 -movflags +faststart";
        if (job.VideoFilter != null) extraArgs += $" -vf {job.VideoFilter}";
        if (job.SegmentSecs.HasValue) extraArgs += $" -t {job.SegmentSecs.Value:F3}";

        bool started = false;
        var  progressDuration = job.SegmentSecs.HasValue
            ? TimeSpan.FromSeconds(job.SegmentSecs.Value)
            : job.TotalDuration;

        await FFMpegArguments
            .FromFileInput(job.InputPath, false, o =>
            {
                if (job.StartOffsetSecs > 0) o.Seek(TimeSpan.FromSeconds(job.StartOffsetSecs));
            })
            .OutputToFile(job.OutputPath, overwrite: true, o => o
                .WithVideoCodec("hevc_nvenc")
                .WithVideoBitrate(job.VideoBitrateKbps)
                .WithAudioCodec("aac")
                .WithAudioBitrate(job.AudioBitrateKbps)
                .WithCustomArgument(extraArgs)
            )
            .NotifyOnProgress(pct =>
            {
                if (!started) { Console.WriteLine(); started = true; }
                RenderProgressBar($"  {label}[NVENC]", (int)pct);
            }, progressDuration)
            .ProcessAsynchronously();

        if (started) Console.WriteLine();
    }

    private static async Task EncodeSoftwareTwoPassAsync(EncodeJob job, string label)
    {
        string statsBase = Path.Combine(Path.GetTempPath(), $"pm_{Guid.NewGuid():N}");
        string statsArg  = statsBase.Replace("\\", "/");

        var progressDuration = job.SegmentSecs.HasValue
            ? TimeSpan.FromSeconds(job.SegmentSecs.Value)
            : job.TotalDuration;

        string durationArg = job.SegmentSecs.HasValue ? $"-t {job.SegmentSecs.Value:F3} " : "";
        string filterArg   = job.VideoFilter != null ? $"-vf {job.VideoFilter}" : "";

        try
        {
            // Pass 1
            bool p1 = false;
            await FFMpegArguments
                .FromFileInput(job.InputPath, false, o =>
                {
                    if (job.StartOffsetSecs > 0) o.Seek(TimeSpan.FromSeconds(job.StartOffsetSecs));
                })
                .OutputToFile("NUL", overwrite: true, o => o
                    .WithVideoCodec("libx265")
                    .WithVideoBitrate(job.VideoBitrateKbps)
                    .WithCustomArgument($"-x265-params pass=1:stats='{statsArg}.log'")
                    .WithCustomArgument(durationArg + filterArg)
                    .DisableChannel(Channel.Audio)
                    .ForceFormat("null")
                )
                .NotifyOnProgress(pct =>
                {
                    if (!p1) p1 = true;
                    RenderProgressBar($"  {label}[Pass 1/2] Analyzing", (int)pct);
                }, progressDuration)
                .ProcessAsynchronously();

            Console.WriteLine();
            ConsoleHelper.WriteColored($"  {label}[Pass 1/2] done.", ConsoleColor.Green);
            Console.WriteLine();

            // Pass 2
            bool p2 = false;
            await FFMpegArguments
                .FromFileInput(job.InputPath, false, o =>
                {
                    if (job.StartOffsetSecs > 0) o.Seek(TimeSpan.FromSeconds(job.StartOffsetSecs));
                })
                .OutputToFile(job.OutputPath, overwrite: true, o => o
                    .WithVideoCodec("libx265")
                    .WithVideoBitrate(job.VideoBitrateKbps)
                    .WithCustomArgument($"-x265-params pass=2:stats='{statsArg}.log'")
                    .WithAudioCodec("aac")
                    .WithAudioBitrate(job.AudioBitrateKbps)
                    .WithCustomArgument(durationArg + filterArg)
                    .WithCustomArgument("-tag:v hvc1 -movflags +faststart")
                )
                .NotifyOnProgress(pct =>
                {
                    if (!p2) p2 = true;
                    RenderProgressBar($"  {label}[Pass 2/2] Encoding ", (int)pct);
                }, progressDuration)
                .ProcessAsynchronously();

            Console.WriteLine();
        }
        finally
        {
            foreach (string f in Directory.GetFiles(Path.GetTempPath(), $"pm_*.log*"))
                try { File.Delete(f); } catch {  }
        }
    }

    public static void RenderProgressBar(string label, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        int    filled = percent / 5;
        string bar    = new string('█', filled) + new string('░', 20 - filled);
        Console.Write($"\r{label}  [{bar}] {percent,3}%   ");
    }
}
