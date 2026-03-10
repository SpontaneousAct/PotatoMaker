namespace PotatoMaker.Core;

/// <summary>
/// Reports encode progress to front ends.
/// </summary>
public record EncodeProgress(string Label, int Percent);
