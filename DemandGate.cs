namespace LoupixDeck.Plugin.Slack;

/// <summary>
/// Starts a background worker the first time a key needs its data and stops it again once no key
/// has asked for a while. The host only polls display commands for keys on the current page (and,
/// with a recent host, not at all while the device is off), so this is what keeps the plugin
/// idle when nobody is looking. A disabled worker never starts.
/// </summary>
internal sealed class DemandGate : IDisposable
{
    private readonly object _lock = new();
    private readonly Func<bool> _enabled;
    private readonly Action _start;
    private readonly Action _stop;
    private readonly TimeSpan _idleTimeout;
    private readonly Timer _timer;
    private DateTime _lastTouch;
    private bool _running;
    private bool _disposed;

    public DemandGate(Func<bool> enabled, Action start, Action stop, TimeSpan idleTimeout)
    {
        _enabled = enabled;
        _start = start;
        _stop = stop;
        _idleTimeout = idleTimeout;
        _timer = new Timer(_ => OnTimer(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsRunning { get { lock (_lock) return _running; } }

    /// <summary>A key asked for data: (re)start the worker if allowed and push the idle deadline out.</summary>
    public void Touch()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _lastTouch = DateTime.UtcNow;
            if (!_running && _enabled())
            {
                _running = true;
                _start();
            }

            if (_running) _timer.Change(_idleTimeout, _idleTimeout);
        }
    }

    /// <summary>Settings changed: stop now; the next <see cref="Touch"/> restarts the worker if it is still enabled.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            StopCore();
        }
    }

    private void OnTimer()
    {
        lock (_lock)
        {
            if (!_running || _disposed) return;
            if (DateTime.UtcNow - _lastTouch >= _idleTimeout) StopCore();
        }
    }

    private void StopCore()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        if (!_running) return;
        _running = false;
        _stop();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            StopCore();
            _timer.Dispose();
        }
    }
}
