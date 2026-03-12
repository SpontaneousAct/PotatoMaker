using PotatoMaker.GUI.Services;
using Xunit;

namespace PotatoMaker.Tests.Services;

public sealed class AppVersionServiceTests
{
    [Fact]
    public void UsesInformationalVersionForSemanticDisplay()
    {
        var service = new AssemblyAppVersionService("1.2.3-beta.4+abc123", new Version(1, 0, 0, 0));

        Assert.Equal("1.2.3-beta.4", service.SemanticVersion);
        Assert.Equal("v1.2.3-beta.4", service.DisplayVersion);
        Assert.Equal("1.2.3-beta.4+abc123", service.InformationalVersion);
    }

    [Fact]
    public void FallsBackToAssemblyVersionWhenInformationalVersionIsMissing()
    {
        var service = new AssemblyAppVersionService(null, new Version(2, 5, 0, 0));

        Assert.Equal("2.5.0", service.SemanticVersion);
        Assert.Equal("v2.5.0", service.DisplayVersion);
        Assert.Equal("2.5.0", service.InformationalVersion);
    }
}
