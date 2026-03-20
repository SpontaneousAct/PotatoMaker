using CommunityToolkit.Mvvm.ComponentModel;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Prevents the app from running more than one encode session at a time.
/// </summary>
public sealed partial class EncodeExecutionCoordinator : ObservableObject
{
    private readonly Lock _sync = new();
    private int _activeLeaseCount;

    [ObservableProperty]
    private bool _isBusy;

    public IDisposable? TryAcquire()
    {
        lock (_sync)
        {
            if (_activeLeaseCount > 0)
                return null;

            _activeLeaseCount = 1;
            IsBusy = true;
            return new Lease(this);
        }
    }

    private void Release()
    {
        lock (_sync)
        {
            if (_activeLeaseCount == 0)
                return;

            _activeLeaseCount = 0;
            IsBusy = false;
        }
    }

    private sealed class Lease(EncodeExecutionCoordinator owner) : IDisposable
    {
        private EncodeExecutionCoordinator? _owner = owner;

        public void Dispose()
        {
            EncodeExecutionCoordinator? owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release();
        }
    }
}
