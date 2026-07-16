using PotatoMaker.GUI.Services;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Supplies support, documentation, and project metadata for the shell help screen.
/// </summary>
public sealed class HelpViewModel : ViewModelBase
{
    public HelpViewModel()
        : this(new AssemblyAppVersionService())
    {
    }

    public HelpViewModel(IAppVersionService versionService)
    {
        ArgumentNullException.ThrowIfNull(versionService);

        VersionText = versionService.DisplayVersion;
        CopyrightText = $"Copyright (c) {DateTime.UtcNow.Year} SpontaneousAct";
    }

    public string Title => "Help";

    public string IntroText =>
        "The full guide now lives on the PotatoMaker website.";

    public string VersionText { get; }

    public string WebsiteDisplayText => "spontaneousact.github.io/PotatoMaker";

    public string CopyrightText { get; }

    public IReadOnlyList<HelpActionLink> Links { get; } =
    [
        new(
            "Guide",
            "Read the full manual on the website.",
            AppLinkCatalog.HelpUrl,
            "Open guide"),
        new(
            "Issues",
            "Report a bug or request a feature on GitHub.",
            AppLinkCatalog.GitHubIssuesUrl,
            "Open issues"),
        new(
            "License",
            "MIT License.",
            AppLinkCatalog.LicenseUrl,
            "Open license"),
        new(
            "Third-party software",
            "FFmpeg, VLC / LibVLC, and managed-library license, source, and attribution information.",
            AppLinkCatalog.ThirdPartyNoticesUrl,
            "Open notices")
    ];
}

/// <summary>
/// Describes a single external resource shown on the help screen.
/// </summary>
public sealed record HelpActionLink(
    string Title,
    string Description,
    string Url,
    string ButtonText);
