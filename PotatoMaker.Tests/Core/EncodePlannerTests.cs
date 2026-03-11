using PotatoMaker.Core;
using Xunit;

namespace PotatoMaker.Tests.Core;

public sealed class EncodePlannerTests
{
    [Fact]
    public void PlanStrategy_ZeroDuration_ThrowsFriendlyError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            EncodePlanner.PlanStrategy(0, 1080, new EncodeSettings()));

        Assert.Equal("The selected clip has no duration. Choose a valid source video or a longer clip.", ex.Message);
    }
}
