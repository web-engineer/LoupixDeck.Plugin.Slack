using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Slack.Api;

/// <summary>
/// Keeps a cached <see cref="SlackSelf"/> for the status, focus and away keys. Polls once a minute
/// while one of those keys is on screen (the plugin's demand gate starts and stops it), and again
/// right after the deck changed something. Keys read <see cref="Current"/>; they never wait on Slack.
/// </summary>
internal sealed class SelfMonitor : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(10);

    private readonly SlackApi _api;
    private readonly IPluginLogger _log;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private TimeSpan _delay = Interval;
    private string? _lastError;

    public SelfMonitor(SlackApi api, IPluginLogger log)
    {
        _api = api;
        _log = log;
    }

    public event Action? Changed;

    public SlackSelf? Current { get; private set; }

    /// <summary>Slack's error code from the last failed poll, or null.</summary>
    public string? Error { get; private set; }

    public void Start()
    {
        lock (_lock)
        {
            if (_cts is not null) return;
            _cts = new CancellationTokenSource();
            _ = LoopAsync(_cts.Token);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>Polls now, e.g. after a key changed the status. Safe to call while stopped.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            var self = await _api.GetSelfAsync().ConfigureAwait(false);
            var changed = self != Current || Error is not null;
            Current = self;
            Error = null;
            _lastError = null;
            _delay = Interval;
            if (changed) Changed?.Invoke();
        }
        catch (Exception ex) when (ex is SlackApiException or HttpRequestException or TaskCanceledException)
        {
            var error = ex is SlackApiException s ? s.Error : ex is TaskCanceledException ? "timeout" : "network";
            if (error != _lastError) _log.Warn($"Slack status poll failed: {ex.Message}");
            _lastError = error;
            var changed = Error != error;
            Error = error;
            _delay = TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, _delay.Ticks * 2));
            if (changed) Changed?.Invoke();
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_api.HasToken) await RefreshAsync().ConfigureAwait(false);
            try
            {
                await Task.Delay(_delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public void Dispose() => Stop();
}
