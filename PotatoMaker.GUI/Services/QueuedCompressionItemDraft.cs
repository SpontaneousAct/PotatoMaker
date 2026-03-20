using PotatoMaker.Core;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Captures the full encode snapshot needed to add a video to the queue.
/// </summary>
public sealed record QueuedCompressionItemDraft(
    string InputPath,
    string OutputDirectory,
    VideoInfo Info,
    StrategyAnalysis Strategy,
    EncodeSettings Settings,
    VideoClipRange ClipRange,
    long SelectedSizeBytes);
