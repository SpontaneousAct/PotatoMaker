using PotatoMaker.GUI.Services;
using Xunit;

namespace PotatoMaker.Tests.Services;

public sealed class WindowsSingleInstanceManagerTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("yes")]
    public void DebugOverride_DisablesSingleInstanceHandling(string value)
    {
        Assert.True(WindowsSingleInstanceManager.IsDisabled(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("false")]
    public void DebugOverride_LeavesSingleInstanceHandlingEnabled(string? value)
    {
        Assert.False(WindowsSingleInstanceManager.IsDisabled(value));
    }
}
