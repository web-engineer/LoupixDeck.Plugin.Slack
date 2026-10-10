namespace LoupixDeck.Plugin.Slack.Platform;

/// <summary>
/// Polls Slack's Dock badge every couple of seconds while the unread key is on screen, and raises
/// <see cref="Changed"/> only when the badge actually changes.
/// </summary>
internal sealed class BadgeMonitor : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly object _lock = new();
    private readonly Func<Badge> _read;
    private Timer? _timer;
    private int _busy;

    public BadgeMonitor(Func<Badge>? read = null) => _read = read ?? DockBadge.Read;

    public event Action? Changed;

    public Badge Current { get; private set; } = Badge.Unknown;

    public void Start()
    {
        lock (_lock)
        {
            _timer ??= new Timer(_ => Poll(), null, TimeSpan.Zero, Interval);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    public void Poll()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var badge = _read();
            if (badge == Current) return;
            Current = badge;
            Changed?.Invoke();
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    public void Dispose() => Stop();
}
