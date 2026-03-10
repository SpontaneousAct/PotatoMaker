using PotatoMaker.GUI.ViewModels;
using Xunit;

namespace PotatoMaker.Tests.ViewModels;

public sealed class SeekRequestThrottlerTests
{
    [Fact]
    public void RapidRequests_CoalesceToLatestPendingSeek()
    {
        var throttler = new SeekRequestThrottler(TimeSpan.FromMilliseconds(75));
        DateTimeOffset start = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        bool dispatchedImmediately = throttler.TryQueue(TimeSpan.FromSeconds(4), start, out TimeSpan immediateSeek);
        bool dispatchedSecondSeek = throttler.TryQueue(TimeSpan.FromSeconds(9), start.AddMilliseconds(10), out _);
        bool dispatchedThirdSeek = throttler.TryQueue(TimeSpan.FromSeconds(12), start.AddMilliseconds(20), out _);

        Assert.True(dispatchedImmediately);
        Assert.Equal(TimeSpan.FromSeconds(4), immediateSeek);
        Assert.False(dispatchedSecondSeek);
        Assert.False(dispatchedThirdSeek);
        Assert.Equal(TimeSpan.FromSeconds(12), throttler.PendingSeek);
        Assert.Equal(TimeSpan.FromMilliseconds(55), throttler.GetRemainingDelay(start.AddMilliseconds(20)));
        Assert.Equal(TimeSpan.FromSeconds(12), throttler.Flush(start.AddMilliseconds(75)));
        Assert.Null(throttler.PendingSeek);
    }

    [Fact]
    public void Reset_ClearsPendingSeekAndAllowsImmediateDispatchAgain()
    {
        var throttler = new SeekRequestThrottler(TimeSpan.FromMilliseconds(75));
        DateTimeOffset start = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        throttler.TryQueue(TimeSpan.FromSeconds(2), start, out _);
        throttler.TryQueue(TimeSpan.FromSeconds(5), start.AddMilliseconds(10), out _);
        throttler.Reset();

        bool dispatchedImmediately = throttler.TryQueue(TimeSpan.FromSeconds(7), start.AddMilliseconds(11), out TimeSpan immediateSeek);

        Assert.True(dispatchedImmediately);
        Assert.Equal(TimeSpan.FromSeconds(7), immediateSeek);
        Assert.Null(throttler.PendingSeek);
        Assert.Equal(TimeSpan.Zero, throttler.GetRemainingDelay(start.AddMilliseconds(11)));
    }
}
