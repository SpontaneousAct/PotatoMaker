namespace PotatoMaker.GUI.Services;

/// <summary>
/// Describes the lifecycle of a queued compression item.
/// </summary>
public enum CompressionQueueItemStatus
{
    Queued,
    Encoding,
    Completed,
    Cancelled,
    Failed
}
