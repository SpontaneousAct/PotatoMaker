namespace PotatoMaker.GUI.ViewModels;

internal sealed class SeekRequestThrottler
{
    private readonly TimeSpan _minimumInterval;
    private DateTimeOffset? _lastDispatchAt;
    private TimeSpan? _pendingSeek;

    public SeekRequestThrottler(TimeSpan minimumInterval)
    {
        if (minimumInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(minimumInterval));

        _minimumInterval = minimumInterval;
    }

    public TimeSpan? PendingSeek => _pendingSeek;

    public bool TryQueue(TimeSpan position, DateTimeOffset now, out TimeSpan immediateSeek)
    {
        _pendingSeek = position;
        if (_lastDispatchAt is null || now - _lastDispatchAt.Value >= _minimumInterval)
        {
            immediateSeek = DispatchPending(now)!.Value;
            return true;
        }

        immediateSeek = default;
        return false;
    }

    public TimeSpan? Flush(DateTimeOffset now) => DispatchPending(now);

    public TimeSpan GetRemainingDelay(DateTimeOffset now)
    {
        if (_pendingSeek is null || _lastDispatchAt is null)
            return TimeSpan.Zero;

        TimeSpan remaining = _minimumInterval - (now - _lastDispatchAt.Value);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public void Reset()
    {
        _lastDispatchAt = null;
        _pendingSeek = null;
    }

    private TimeSpan? DispatchPending(DateTimeOffset now)
    {
        if (_pendingSeek is not { } pendingSeek)
            return null;

        _pendingSeek = null;
        _lastDispatchAt = now;
        return pendingSeek;
    }
}
