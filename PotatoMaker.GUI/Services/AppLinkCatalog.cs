namespace PotatoMaker.GUI.Services;

/// <summary>
/// Centralizes public project links shown by the desktop app.
/// </summary>
public static class AppLinkCatalog
{
    public const string WebsiteUrl = "https://spontaneousact.github.io/PotatoMaker/";

    public const string HelpUrl = $"{WebsiteUrl}help/";

    public const string PrivacyPolicyUrl = $"{WebsiteUrl}privacy/";

    public const string GitHubRepositoryUrl = "https://github.com/SpontaneousAct/PotatoMaker";

    public const string GitHubIssuesUrl = $"{GitHubRepositoryUrl}/issues/new";

    public const string ReleasesUrl = $"{GitHubRepositoryUrl}/releases";

    public const string LicenseUrl = $"{GitHubRepositoryUrl}/blob/main/LICENSE.txt";

    public const string ThirdPartyNoticesUrl =
        $"{GitHubRepositoryUrl}/blob/main/third_party/notices/THIRD-PARTY-NOTICES.txt";
}
