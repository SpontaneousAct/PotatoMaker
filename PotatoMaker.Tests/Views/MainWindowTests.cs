using Avalonia.Input;
using PotatoMaker.GUI.Views;
using Xunit;

namespace PotatoMaker.Tests.Views;

public sealed class MainWindowTests
{
    [Theory]
    [InlineData(Key.D0)]
    [InlineData(Key.NumPad0)]
    public void IsTimelineResetShortcut_AcceptsControlZero(Key key)
    {
        Assert.True(MainWindow.IsTimelineResetShortcut(key, KeyModifiers.Control));
    }

    [Fact]
    public void IsTimelineResetShortcut_RejectsOtherModifiers()
    {
        Assert.False(MainWindow.IsTimelineResetShortcut(Key.D0, KeyModifiers.None));
        Assert.False(MainWindow.IsTimelineResetShortcut(Key.D0, KeyModifiers.Control | KeyModifiers.Shift));
    }
}
