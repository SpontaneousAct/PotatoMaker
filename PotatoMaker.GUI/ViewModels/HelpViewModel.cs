namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Supplies static help content for the shell help screen.
/// </summary>
public sealed class HelpViewModel : ViewModelBase
{
    public string IntroText =>
        "PotatoMaker keeps things simple: load a video, choose the part you want, then start compression. The app handles the technical stuff for you.";

    public IReadOnlyList<string> QuickGuideSteps { get; } =
    [
        "Load a video with Browse, or drag and drop one into the app.",
        "Preview the video and choose the clip you want with Set Start and Set End.",
        "Check the summary and choose where the output should be saved if needed.",
        "Click Start Compression and watch the console until the job finishes."
    ];

    public IReadOnlyList<ShortcutHint> ShortcutHints { get; } =
    [
        new("Space", "Play or pause the current preview."),
        new("Q", "Jump backward by 10 seconds."),
        new("E", "Jump forward by 10 seconds."),
        new("A", "Set the trim start to the current playback position."),
        new("D", "Set the trim end to the current playback position."),
    ];
}

/// <summary>
/// Describes a single keyboard shortcut shown in the help screen.
/// </summary>
public sealed record ShortcutHint(string Keys, string Description);
