using FFMpegCore;
using FFMpegCore.Enums;
using Microsoft.Extensions.Logging;

namespace PotatoMaker.Core;

/// <summary>
/// Encodes video jobs with the configured FFmpeg encoder.
/// </summary>
public static class VideoEncoder
{
    public static async Task EncodeAsync(
        EncodeJob job,
        EncoderChoice encoder,
        int svtAv1Preset,
        ILogger logger,
        IProgress<EncodeProgress>? progress = null,
        string label = "",
        CancellationToken ct = default)
    {
        FFmpegBinaries.EnsureConfigured();
        int normalizedSvtAv1Preset = EncodeSettings.NormalizeSvtAv1Preset(svtAv1Preset);

        switch (encoder)
        {
            case EncoderChoice.SvtAv1:
                logger.LogInformation("  Encoder: libsvtav1 (CPU two-pass, preset {Preset})", normalizedSvtAv1Preset);
                await EncodeSvtAv1TwoPassAsync(job, normalizedSvtAv1Preset, logger, progress, label, ct);
                logger.LogInformation(PipelineEvents.Success, "  [ok] libsvtav1 encode complete.");
                return;
            case EncoderChoice.Nvenc:
                logger.LogInformation("  Encoder: av1_nvenc (GPU)");
                await EncodeNvencAsync(job, logger, progress, label, ct);
                logger.LogInformation(PipelineEvents.Success, "  [ok] NVENC AV1 encode complete.");
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(encoder), encoder, "Unknown encoder choice.");
        }
    }

    private static async Task EncodeNvencAsync(
        EncodeJob job,
        ILogger logger,
        IProgress<EncodeProgress>? progress,
        string label,
        CancellationToken ct)
    {
        TimeSpan progressDuration = job.SegmentSecs.HasValue
            ? TimeSpan.FromSeconds(job.SegmentSecs.Value)
            : job.TotalDuration;

        try
        {
            await FFMpegArguments
                .FromFileInput(job.InputPath, false, o =>
                {
                    if (job.StartOffsetSecs > 0)
                        o.Seek(TimeSpan.FromSeconds(job.StartOffsetSecs));
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

                    if (job.VideoFilter is not null)
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
        EncodeJob job,
        int svtAv1Preset,
        ILogger logger,
        IProgress<EncodeProgress>? progress,
        string label,
        CancellationToken ct)
    {
        string statsBase = Path.Combine(Path.GetTempPath(), $"pm_{Guid.NewGuid():N}");
        string statsArg = statsBase.Replace("\\", "/", StringComparison.Ordinal);
        string statsDir = Path.GetDirectoryName(statsBase) ?? Path.GetTempPath();
        string statsName = Path.GetFileName(statsBase);

        TimeSpan progressDuration = job.SegmentSecs.HasValue
            ? TimeSpan.FromSeconds(job.SegmentSecs.Value)
            : job.TotalDuration;

        try
        {
            logger.LogInformation("  {Label}[Pass 1/2] analyzing...", label);

            await FFMpegArguments
                .FromFileInput(job.InputPath, false, o =>
                {
                    if (job.StartOffsetSecs > 0)
                        o.Seek(TimeSpan.FromSeconds(job.StartOffsetSecs));
                })
                .OutputToFile("NUL", overwrite: true, o =>
                {
                    o.WithVideoCodec("libsvtav1")
                     .WithVideoBitrate(job.VideoBitrateKbps)
                     .WithCustomArgument($"-preset {svtAv1Preset}")
                     .WithCustomArgument("-pass 1")
                     .WithCustomArgument($"-passlogfile {statsArg}")
                     .DisableChannel(Channel.Audio)
                     .ForceFormat("null");

                    if (job.VideoFilter is not null)
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
            logger.LogInformation("  {Label}[Pass 2/2] encoding...", label);

            await FFMpegArguments
                .FromFileInput(job.InputPath, false, o =>
                {
                    if (job.StartOffsetSecs > 0)
                        o.Seek(TimeSpan.FromSeconds(job.StartOffsetSecs));
                })
                .OutputToFile(job.OutputPath, overwrite: true, o =>
                {
                    o.WithVideoCodec("libsvtav1")
                     .WithVideoBitrate(job.VideoBitrateKbps)
                     .WithCustomArgument($"-preset {svtAv1Preset}")
                     .WithCustomArgument("-pass 2")
                     .WithCustomArgument($"-passlogfile {statsArg}")
                     .WithAudioCodec("aac")
                     .WithAudioBitrate(job.AudioBitrateKbps)
                     .WithFastStart();

                    if (job.VideoFilter is not null)
                        o.WithCustomArgument($"-vf {job.VideoFilter}");
                    if (job.SegmentSecs.HasValue)
                        o.WithDuration(TimeSpan.FromSeconds(job.SegmentSecs.Value));
                })
                .CancellableThrough(ct)
                .NotifyOnProgress(pct =>
                {
                    progress?.Report(new EncodeProgress($"  {label}[Pass 2/2] Encoding", (int)pct));
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
            foreach (string file in Directory.EnumerateFiles(statsDir, $"{statsName}*"))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                }
            }
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
        }
    }
}
