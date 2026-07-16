using PotatoMaker.GUI.Services;
using PotatoMaker.GUI.ViewModels;
using Xunit;

namespace PotatoMaker.Tests.ViewModels;

public sealed class HelpViewModelTests
{
    [Fact]
    public void Links_IncludeThirdPartyLicenseAndSourceNotices()
    {
        var viewModel = new HelpViewModel(new StubVersionService());

        HelpActionLink link = Assert.Single(
            viewModel.Links,
            item => item.Title == "Third-party software");
        Assert.Equal(AppLinkCatalog.ThirdPartyNoticesUrl, link.Url);
        Assert.Contains("/blob/master/", link.Url, StringComparison.Ordinal);
        Assert.Contains("FFmpeg", link.Description, StringComparison.Ordinal);
        Assert.Contains("LibVLC", link.Description, StringComparison.Ordinal);
    }

    private sealed class StubVersionService : IAppVersionService
    {
        public string SemanticVersion => "1.0.0";

        public string DisplayVersion => "v1.0.0";

        public string InformationalVersion => "1.0.0";
    }
}
