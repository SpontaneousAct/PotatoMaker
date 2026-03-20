namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Supplies static help content for the shell help screen.
/// </summary>
public sealed class HelpViewModel : ViewModelBase
{
    public string OverviewText =>
        "PotatoMaker makes videos easier to share. Open a file, trim the part you want, then click Compress.";

    public IReadOnlyList<HelpStep> GettingStartedSteps { get; } =
    [
        new("1", "Open a video", "Click Browse... or drag a video into the app."),
        new("2", "Trim the clip", "Use the timeline handles or Set Start and Set End to keep only the part you want."),
        new("3", "Check the plan", "Look at Strategy Preview to see the output resolution, bitrate, crop, and number of parts."),
        new("4", "Choose where to save", "Use Save Output > Choose if you want the export in a different folder."),
        new("5", "Compress", "Click Compress to start, or Add to queue if you want to line up more than one video.")
    ];

    public IReadOnlyList<HelpNote> AutomaticChoices { get; } =
    [
        new("Crop and size", "PotatoMaker can detect crop automatically and choose a smaller resolution when it helps."),
        new("Bitrate and parts", "It picks the bitrate for you and can split large exports into multiple MP4 files."),
        new("Encoder settings", "If you want to change encoder behavior, use Settings. The help screen does not assume a GPU path.")
    ];

    public IReadOnlyList<HelpNote> QuickTips { get; } =
    [
        new("Compress is unavailable", "Wait for the file analysis to finish. The status changes to Ready when the export plan is set."),
        new("Output location", "Reset under Save Output switches back to the source file folder."),
        new("Queue", "Add to queue keeps the current plan so you can set up the next video without starting right away."),
        new("Multiple parts", "If Strategy Preview shows more than one part, PotatoMaker will export several MP4 files.")
    ];

    public IReadOnlyList<ShortcutHint> ShortcutHints { get; } =
    [
        new("Space", "Play or pause the preview."),
        new("Q", "Jump backward by 10 seconds."),
        new("E", "Jump forward by 10 seconds."),
        new("A", "Set the trim start at the current position."),
        new("D", "Set the trim end at the current position.")
    ];
}

/// <summary>
/// Describes a single getting-started step.
/// </summary>
public sealed record HelpStep(string Number, string Title, string Description);

/// <summary>
/// Describes a short help note.
/// </summary>
public sealed record HelpNote(string Title, string Description);

/// <summary>
/// Describes a single keyboard shortcut shown in the help screen.
/// </summary>
public sealed record ShortcutHint(string Keys, string Description);
