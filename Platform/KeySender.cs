using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Slack.Platform;

/// <summary>
/// Sends a Slack shortcut through the host's <c>System.KeyCombination</c>, bringing Slack to the
/// front first so the keys land in Slack and not in whatever app had focus.
/// </summary>
internal sealed class KeySender(Func<bool> bringToFront)
{
    /// <summary>
    /// After Slack was seen in front, trust that for this long. A dial sends a burst of shortcuts
    /// and checking the front app costs two child processes each time.
    /// </summary>
    private static readonly TimeSpan FrontTrust = TimeSpan.FromSeconds(1.5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _frontUntil;

    /// <summary>Returns false when Slack could not be brought to the front; no keys are sent then.</summary>
    public async Task<bool> SendAsync(IPluginHost host, string combination, int repeat = 1)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (bringToFront() && DateTime.UtcNow >= _frontUntil)
            {
                if (!await SlackApp.ActivateAsync(host).ConfigureAwait(false)) return false;
            }

            _frontUntil = DateTime.UtcNow + FrontTrust;
            for (var i = 0; i < repeat; i++)
            {
                host.ExecuteCommand($"System.KeyCombination({combination})");
                if (i < repeat - 1) await Task.Delay(40).ConfigureAwait(false);
            }

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forget that Slack was in front, e.g. after the user switched apps on purpose.</summary>
    public void Invalidate() => _frontUntil = DateTime.MinValue;
}
