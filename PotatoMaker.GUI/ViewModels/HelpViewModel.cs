namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Supplies static help content for the shell help screen.
/// </summary>
public sealed class HelpViewModel : ViewModelBase
{
    public IReadOnlyList<string> QuickStartSteps { get; } =
    [
        "Drop in a video or click Browse to choose one from disk.",
        "Preview the clip and use Space to play or pause while you inspect it.",
        "Press A to mark the trim start and D to mark the trim end.",
        "Review the summary, choose where to save, and verify the expected output.",
        "Start compression and follow the live progress log until the encode finishes."
    ];

    public IReadOnlyList<ShortcutHint> ShortcutHints { get; } =
    [
        new("Space", "Play or pause the current preview."),
        new("A", "Set the trim start to the current playback position."),
        new("D", "Set the trim end to the current playback position.")
    ];

    public string EncoderSummary =>
        "PotatoMaker prefers NVIDIA AV1 encoding when it is enabled and available. If NVENC AV1 is unavailable, the app falls back to CPU encoding with the preset you choose in Settings.";
}

/// <summary>
/// Describes a single keyboard shortcut shown in the help screen.
/// </summary>
public sealed record ShortcutHint(string Keys, string Description);
