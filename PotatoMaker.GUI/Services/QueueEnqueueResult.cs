namespace PotatoMaker.GUI.Services;

/// <summary>
/// Describes the outcome of adding an item to the compression queue.
/// </summary>
public sealed record QueueEnqueueResult(bool Succeeded, string Message);
