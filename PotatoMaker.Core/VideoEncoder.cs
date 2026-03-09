using FFMpegCore;
using FFMpegCore.Enums;
using Microsoft.Extensions.Logging;

namespace PotatoMaker.Core;

public static class VideoEncoder
{
    public static async Task EncodeAsync(
        EncodeJob                  job,
        EncoderChoice              encoder,
        ILogger                    logger,
        IProgress<EncodeProgress>? progress = null,
        string                     label    = "",
        CancellationToken          ct       = default)
    {
        if (encoder == EncoderChoice.SvtAv1)
        {
            logger.LogInformation("  Encoder: libsvtav1 (CPU two-pass)");
            await EncodeSvtAv1TwoPassAsync(job, logger, progress, label, ct);
            logger.LogInformation(PipelineEvents.Success, "  ✓ libsvtav1 encode complete.");
            return;
        }

        logger.LogInformation("  Trying av1_nvenc...");

        try
        {
            await EncodeNvencAsync(job, logger, progress, label, ct);
            logger.LogInformation(PipelineEvents.Success, "  ✓ NVENC AV1 encode complete.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("  av1_nvenc not available.");
            logger.LogWarning("    Reason: {Reason}", ex.Message.Split('\n')[0].Trim());
            logger.LogInformation("  Falling back to libsvtav1 two-pass (CPU)...");
            await EncodeSvtAv1TwoPassAsync(job, logger, progress, label, ct);
            logger.LogInformation(PipelineEvents.Success, "  ✓ libsvtav1 encode complete.");
        }
    }

    private static async Task EncodeNvencAsync(
        EncodeJob job, ILogger logger, IProgress<EncodeProgress>? progress, string label, CancellationToken ct)
    {
        var progressDuration = job.SegmentSecs.HasValue
            ? TimeSpan.FromSeconds(job.SegmentSecs.Value)
            : job.TotalDuration;

        try
        {
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
                    progress?.Report(new EncodeProgress($"  {label}[NVENC]", (int)pct));
                }, progressDuration)
                .ProcessAsynchronously();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            TryDeleteFile(job.OutputPath);
            throw;
        }
    }

    private static async Task EncodeSvtAv1TwoPassAsync(
        EncodeJob job, ILogger logger, IProgress<EncodeProgress>? progress, string label, CancellationToken ct)
    {
        string statsBase = Path.Combine(Path.GetTempPath(), $"pm_{Guid.NewGuid():N}");
        string statsArg  = statsBase.Replace("\\", "/");
        string statsDir  = Path.GetDirectoryName(statsBase) ?? Path.GetTempPath();
        string statsName = Path.GetFileName(statsBase);

        var progressDuration = job.SegmentSecs.HasValue
            ? TimeSpan.FromSeconds(job.SegmentSecs.Value)
            : job.TotalDuration;

        try
        {
            // Pass 1
            logger.LogInformation("  {Label}[Pass 1/2] analyzing...", label);

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
                    progress?.Report(new EncodeProgress($"  {label}[Pass 1/2] Analyzing", (int)pct));
                }, progressDuration)
                .ProcessAsynchronously();

            logger.LogInformation(PipelineEvents.Success, "  {Label}[Pass 1/2] done.", label);

            // Pass 2
            logger.LogInformation("  {Label}[Pass 2/2] encoding...", label);

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
                    progress?.Report(new EncodeProgress($"  {label}[Pass 2/2] Encoding ", (int)pct));
                }, progressDuration)
                .ProcessAsynchronously();

            logger.LogInformation(PipelineEvents.Success, "  {Label}[Pass 2/2] done.", label);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            TryDeleteFile(job.OutputPath);
            throw;
        }
        finally
        {
            foreach (string f in Directory.EnumerateFiles(statsDir, $"{statsName}*"))
                try { File.Delete(f); } catch {  }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup for canceled runs.
        }
    }
}
