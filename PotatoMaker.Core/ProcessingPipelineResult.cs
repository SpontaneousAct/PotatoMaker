namespace PotatoMaker.Core;

/// <summary>
/// Describes the files produced by one pipeline run.
/// </summary>
public sealed record ProcessingPipelineResult(
    IReadOnlyList<string> OutputPaths,
    long TotalOutputBytes);
