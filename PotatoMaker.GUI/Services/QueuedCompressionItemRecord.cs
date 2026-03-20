using PotatoMaker.Core;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Persists one queued compression item.
/// </summary>
public sealed record QueuedCompressionItemRecord(
    string Id,
    string InputPath,
    string OutputDirectory,
    VideoInfo Info,
    StrategyAnalysis Strategy,
    EncodeSettings Settings,
    long ClipStartTicks,
    long ClipEndTicks,
    long SelectedSizeBytes,
    CompressionQueueItemStatus Status,
    int ProgressPercent,
    string ProgressStateText,
    long? OutputSizeBytes,
    string? FailureMessage,
    DateTimeOffset AddedAtUtc);
