using FFMpegCore;
using FFMpegCore.Enums;

namespace PotatoMaker;

enum EncoderChoice { Nvenc, SvtAv1 }

static class VideoEncoder
{
    public static async Task EncodeAsync(EncodeJob job, EncoderChoice encoder, string label = "", CancellationToken ct = default)
    {
        if (encoder == EncoderChoice.SvtAv1)
        {
            Console.WriteLine($"  Encoder: libsvtav1 (CPU two-pass)");
            Console.WriteLine();
            await EncodeSvtAv1TwoPassAsync(job, label, ct);
            ConsoleHelper.WriteColored("  ✓ libsvtav1 encode complete.", ConsoleColor.Green);
            return;
        }

        Console.Write("  Trying av1_nvenc... ");

        try
        {
            await EncodeNvencAsync(job, label, ct);
            ConsoleHelper.WriteColored("  ✓ NVENC AV1 encode complete.", ConsoleColor.Green);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            ConsoleHelper.WriteColored("not available.", ConsoleColor.Yellow);
            ConsoleHelper.WriteColored($"    Reason: {ex.Message.Split('\n')[0].Trim()}", ConsoleColor.Yellow);
            Console.WriteLine();
            Console.WriteLine("  Falling back to libsvtav1 two-pass (CPU)...");
            Console.WriteLine();
            await EncodeSvtAv1TwoPassAsync(job, label, ct);
            ConsoleHelper.WriteColored("  ✓ libsvtav1 encode complete.", ConsoleColor.Green);
        }
    }

    private static async Task EncodeNvencAsync(EncodeJob job, string label, CancellationToken ct)
    {
        bool started = false;
        var  progressDuration = job.SegmentSecs.HasValue
            ? TimeSpan.FromSeconds(job.SegmentSecs.Value)
            : job.TotalDuration;

        await FFMpegArguments
            .FromFileInput(job.InputPath, false, o =>
            {
                if (job.StartOffsetSecs > 0) o.Seek(TimeSpan.FromSeconds(job.StartOffsetSecs));
            })
            .OutputToFile(job.OutputPath, overwrite: true, o =>
            {
                o.WithVideoCodec("av1_nvenc")
                 .WithVideoBitrate(job.VideoBitrateKbps)
                 .WithAudioCodec("aac")
                 .WithAudioBitrate(job.AudioBitrateKbps)
                 .WithCustomArgument("-rc vbr")
                 .WithCustomArgument($"-maxrate {job.VideoBitrateKbps}k")
                 .WithCustomArgument($"-bufsize {job.VideoBitrateKbps * 2}k")
                 .WithCustomArgument("-preset p5")
                 .WithFastStart();

                if (job.VideoFilter != null)
                    o.WithCustomArgument($"-vf {job.VideoFilter}");
                if (job.SegmentSecs.HasValue)
                    o.WithDuration(TimeSpan.FromSeconds(job.SegmentSecs.Value));
            })
            .CancellableThrough(ct)
            .NotifyOnProgress(pct =>
            {
                if (!started) { Console.WriteLine(); started = true; }
                RenderProgressBar($"  {label}[NVENC]", (int)pct);
            }, progressDuration)
            .ProcessAsynchronously();

        if (started) Console.WriteLine();
    }

    private static async Task EncodeSvtAv1TwoPassAsync(EncodeJob job, string label, CancellationToken ct)
    {
        string statsBase = Path.Combine(Path.GetTempPath(), $"pm_{Guid.NewGuid():N}");
        string statsArg  = statsBase.Replace("\\", "/");

        var progressDuration = job.SegmentSecs.HasValue
            ? TimeSpan.FromSeconds(job.SegmentSecs.Value)
            : job.TotalDuration;

        try
        {
            // Pass 1
            bool p1 = false;
            await FFMpegArguments
                .FromFileInput(job.InputPath, false, o =>
                {
                    if (job.StartOffsetSecs > 0) o.Seek(TimeSpan.FromSeconds(job.StartOffsetSecs));
                })
                .OutputToFile("NUL", overwrite: true, o =>
                {
                    o.WithVideoCodec("libsvtav1")
                     .WithVideoBitrate(job.VideoBitrateKbps)
                     .WithCustomArgument("-preset 6")
                     .WithCustomArgument("-pass 1")
                     .WithCustomArgument($"-passlogfile {statsArg}")
                     .DisableChannel(Channel.Audio)
                     .ForceFormat("null");

                    if (job.VideoFilter != null)
                        o.WithCustomArgument($"-vf {job.VideoFilter}");
                    if (job.SegmentSecs.HasValue)
                        o.WithDuration(TimeSpan.FromSeconds(job.SegmentSecs.Value));
                })
                .CancellableThrough(ct)
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
                .OutputToFile(job.OutputPath, overwrite: true, o =>
                {
                    o.WithVideoCodec("libsvtav1")
                     .WithVideoBitrate(job.VideoBitrateKbps)
                     .WithCustomArgument("-preset 6")
                     .WithCustomArgument("-pass 2")
                     .WithCustomArgument($"-passlogfile {statsArg}")
                     .WithCustomArgument($"-maxrate {job.VideoBitrateKbps}k")
                     .WithCustomArgument($"-bufsize {job.VideoBitrateKbps * 2}k")
                     .WithAudioCodec("aac")
                     .WithAudioBitrate(job.AudioBitrateKbps)
                     .WithFastStart();

                    if (job.VideoFilter != null)
                        o.WithCustomArgument($"-vf {job.VideoFilter}");
                    if (job.SegmentSecs.HasValue)
                        o.WithDuration(TimeSpan.FromSeconds(job.SegmentSecs.Value));
                })
                .CancellableThrough(ct)
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
