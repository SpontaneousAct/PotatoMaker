using System.Globalization;
using System.Reflection;
using PotatoMaker.Core;
using Xunit;

namespace PotatoMaker.Tests.Core;

public sealed class CropDetectorTests
{
    [Fact]
    public void FormatSeconds_UsesInvariantCulture()
    {
        MethodInfo? formatMethod = typeof(CropDetector).GetMethod("FormatSeconds", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(formatMethod);

        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
            string formatted = Assert.IsType<string>(formatMethod!.Invoke(null, [12.5d]));

            Assert.Equal("12.5", formatted);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
