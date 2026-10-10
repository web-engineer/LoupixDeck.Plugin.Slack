using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Slack.Api;

/// <summary>
/// One Slack workspace the user connected: its token slot, its API client and what the plugin last
/// learnt about it (who the token belongs to, and the user's status there).
/// </summary>
internal sealed class Workspace : IDisposable
{
    public Workspace(int slot, string token, SlackApi api)
    {
        Slot = slot;
        Token = token;
        Api = api;
    }

    /// <summary>The token itself, only to carry identities over when the settings change.</summary>
    internal string Token { get; }

    /// <summary>The settings slot (1-based) the token came from.</summary>
    public int Slot { get; }

    public SlackApi Api { get; }

    /// <summary>Who the token belongs to; null until <c>auth.test</c> answered.</summary>
    public Identity? Identity { get; internal set; }

    public SlackSelf? Self { get; internal set; }

    /// <summary>Slack's error code from the last failed call, or null.</summary>
    public string? Error { get; internal set; }

    public string Name => Identity?.Team is { Length: > 0 } team ? team : $"Workspace {Slot}";

    /// <summary>
    /// True when <paramref name="selector"/> names this workspace: its slot number, team id (T…),
    /// subdomain (acme or acme.slack.com) or name, ignoring case.
    /// </summary>
    public bool Matches(string selector)
    {
        var s = selector.Trim();
        if (int.TryParse(s, out var slot)) return slot == Slot;
        if (Identity is not { } id) return false;
        if (s.EndsWith(".slack.com", StringComparison.OrdinalIgnoreCase)) s = s[..^".slack.com".Length];
        return string.Equals(s, id.TeamId, StringComparison.OrdinalIgnoreCase)
               || string.Equals(s, id.Domain, StringComparison.OrdinalIgnoreCase)
               || string.Equals(s, id.Team, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => Api.Dispose();
}

/// <summary>
/// Every workspace the user connected (one token per workspace), plus the poller that keeps each
/// one's status, Do Not Disturb and presence cached for the account keys. Polls once a minute while
/// an account key is on screen (the plugin's demand gate starts and stops it), and right after the
/// deck changed something. Keys read the cache; they never wait on Slack.
/// </summary>
internal sealed class WorkspaceSet : IDisposable
{
    /// <summary>The most workspaces the settings page lets you add.</summary>
    public const int Slots = 10;

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IPluginLogger _log;
    private readonly object _lock = new();
    private readonly HashSet<string> _logged = new();
    private CancellationTokenSource? _cts;

    /// <param name="tokens">Token per slot, index 0 = slot 1; blank slots are skipped.</param>
    /// <param name="handler">Test hook: the HTTP handler for a token.</param>
    /// <param name="previous">The set this one replaces: identities already learnt for the same tokens are kept.</param>
    public WorkspaceSet(IReadOnlyList<string?> tokens, IPluginLogger log, Func<string, HttpMessageHandler?>? handler = null,
        WorkspaceSet? previous = null)
    {
        _log = log;
        var all = new List<Workspace>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i]?.Trim();
            if (string.IsNullOrEmpty(token)) continue;
            all.Add(new Workspace(i + 1, token, new SlackApi(() => token, handler?.Invoke(token)))
            {
                Identity = previous?.All.FirstOrDefault(w => w.Token == token)?.Identity
            });
        }

        All = all;
    }

    public event Action? Changed;

    public IReadOnlyList<Workspace> All { get; }

    public bool Any => All.Count > 0;

    /// <summary>
    /// The workspaces a key's <c>Workspace</c> parameter names. Blank or <c>all</c> means every
    /// workspace; otherwise a comma-free list separated by <c>;</c> or <c>|</c>, e.g. <c>acme;2</c>.
    /// </summary>
    public IReadOnlyList<Workspace> Select(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector) || selector.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)) return All;
        var parts = selector.Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return All.Where(w => parts.Any(w.Matches)).ToList();
    }

    /// <summary>True while some workspace has not told us who it is yet, so a name might still match.</summary>
    public bool Resolving => All.Any(w => w.Identity is null && w.Error is null);

    public Workspace? ByTeamId(string teamId) =>
        All.FirstOrDefault(w => string.Equals(w.Identity?.TeamId, teamId, StringComparison.OrdinalIgnoreCase));

    public Workspace? ByDomain(string domain) =>
        All.FirstOrDefault(w => string.Equals(w.Identity?.Domain, domain, StringComparison.OrdinalIgnoreCase));

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

    /// <summary>Learns who each token belongs to, without reading statuses.</summary>
    public async Task IdentifyAsync()
    {
        var changed = await Task.WhenAll(All.Select(IdentifyOneAsync)).ConfigureAwait(false);
        if (changed.Any(c => c)) Changed?.Invoke();
    }

    /// <summary>Re-reads the given workspaces (all of them by default). Safe to call while stopped.</summary>
    public async Task RefreshAsync(IEnumerable<Workspace>? only = null)
    {
        var started = DateTime.UtcNow;
        var changed = await Task.WhenAll((only ?? All).Select(RefreshOneAsync)).ConfigureAwait(false);
        if (!_firstReadLogged && All.Any(w => w.Self is not null))
        {
            _firstReadLogged = true;
            _log.Info($"Slack status read for {All.Count(w => w.Self is not null)}/{All.Count} workspace(s) in {(DateTime.UtcNow - started).TotalMilliseconds:0} ms");
        }

        if (changed.Any(c => c)) Changed?.Invoke();
    }

    private bool _firstReadLogged;

    private async Task<bool> IdentifyOneAsync(Workspace w)
    {
        if (w.Identity is not null) return false;
        try
        {
            w.Identity = await w.Api.AuthTestAsync().ConfigureAwait(false);
            w.Error = null;
            _log.Info($"Slack workspace {w.Slot}: {w.Identity.Team} ({w.Identity.TeamId}) as {w.Identity.User}");
            return true;
        }
        catch (Exception ex) when (IsSlackFailure(ex))
        {
            return Fail(w, ex, "sign-in check");
        }
    }

    private async Task<bool> RefreshOneAsync(Workspace w)
    {
        var changed = await IdentifyOneAsync(w).ConfigureAwait(false);
        if (w.Identity is null) return changed;
        try
        {
            var self = await w.Api.GetSelfAsync().ConfigureAwait(false);
            changed |= self != w.Self || w.Error is not null;
            w.Self = self;
            w.Error = null;
            return changed;
        }
        catch (Exception ex) when (IsSlackFailure(ex))
        {
            return Fail(w, ex, "status poll") | changed;
        }
    }

    private bool Fail(Workspace w, Exception ex, string what)
    {
        var error = ex is SlackApiException s ? s.Error : ex is TaskCanceledException ? "timeout" : "network";
        if (_logged.Add($"{w.Slot}:{error}")) _log.Warn($"Slack {what} for workspace {w.Slot} ({w.Name}) failed: {ex.Message}");
        var changed = w.Error != error;
        w.Error = error;
        return changed;
    }

    private static bool IsSlackFailure(Exception ex) => ex is SlackApiException or HttpRequestException or TaskCanceledException;

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await RefreshAsync().ConfigureAwait(false);
            try
            {
                await Task.Delay(Interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        Stop();
        foreach (var w in All) w.Dispose();
    }
}
