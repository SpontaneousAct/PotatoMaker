namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Represents the user-facing state of the current analysis or encode flow.
/// </summary>
public enum ConversionStatus
{
    Idle,
    Analysing,
    Encoding,
    Cancelled,
    Error,
    Done
}
