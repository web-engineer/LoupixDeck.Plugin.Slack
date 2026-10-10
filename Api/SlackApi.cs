using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LoupixDeck.Plugin.Slack.Api;

/// <summary>Thrown when Slack answers <c>ok: false</c>; <see cref="Error"/> is Slack's error code.</summary>
internal sealed class SlackApiException(string method, string error)
    : Exception($"{method}: {error}")
{
    public string Error { get; } = error;
}

/// <summary>The user Slack says a token belongs to, and the scopes the token carries.</summary>
internal sealed record Identity(string User, string UserId, string Team, string TeamId, IReadOnlySet<string> Scopes);

/// <summary>Your own status, Do Not Disturb and presence, as one snapshot.</summary>
internal sealed record SlackSelf
{
    public string StatusText { get; init; } = string.Empty;
    public string StatusEmoji { get; init; } = string.Empty;
    public DateTimeOffset? StatusExpires { get; init; }

    /// <summary>A Do Not Disturb snooze is running (set from Slack, the deck or a schedule's manual override).</summary>
    public bool Snoozed { get; init; }
    public DateTimeOffset? SnoozeEnds { get; init; }

    /// <summary>Inside the user's Do Not Disturb schedule (notifications paused without a snooze).</summary>
    public bool ScheduledDnd { get; init; }

    /// <summary>Set to away by hand (Slack's "Set yourself as away").</summary>
    public bool ManualAway { get; init; }
    public string Presence { get; init; } = string.Empty;
}

/// <summary>
/// The handful of Slack Web API methods the plugin uses, called with a user token (<c>xoxp-</c>)
/// from the user's own Slack app. Every call is a form POST, which every method accepts.
/// </summary>
internal sealed class SlackApi : IDisposable
{
    public const string BaseUrl = "https://slack.com/api/";

    /// <summary>User scopes the plugin's Slack app asks for. Keep in step with <see cref="Manifest"/>.</summary>
    public static readonly string[] Scopes =
        ["users.profile:read", "users.profile:write", "dnd:read", "dnd:write", "users:read", "users:write"];

    private readonly HttpClient _http;
    private readonly Func<string?> _token;

    public SlackApi(Func<string?> token, HttpMessageHandler? handler = null)
    {
        _token = token;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(BaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(10);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LoupixDeck.Plugin.Slack");
    }

    public bool HasToken => !string.IsNullOrWhiteSpace(_token());

    public async Task<Identity> AuthTestAsync(CancellationToken ct = default)
    {
        var (json, response) = await CallAsync("auth.test", [], ct).ConfigureAwait(false);
        var scopes = response.Headers.TryGetValues("x-oauth-scopes", out var values)
            ? values.SelectMany(v => v.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).ToHashSet()
            : new HashSet<string>();
        return new Identity(Str(json, "user"), Str(json, "user_id"), Str(json, "team"), Str(json, "team_id"), scopes);
    }

    /// <summary>Reads status, Do Not Disturb and presence in three calls (Tier 3–4: far below the limits at one poll a minute).</summary>
    public async Task<SlackSelf> GetSelfAsync(CancellationToken ct = default)
    {
        var (profileJson, _) = await CallAsync("users.profile.get", [], ct).ConfigureAwait(false);
        var (dndJson, _) = await CallAsync("dnd.info", [], ct).ConfigureAwait(false);
        var (presenceJson, _) = await CallAsync("users.getPresence", [], ct).ConfigureAwait(false);
        return ParseSelf(profileJson, dndJson, presenceJson, DateTimeOffset.UtcNow);
    }

    internal static SlackSelf ParseSelf(JsonNode profileJson, JsonNode dndJson, JsonNode presenceJson, DateTimeOffset now)
    {
        var profile = profileJson["profile"];
        var expiration = Long(profile, "status_expiration");

        var snoozed = Bool(dndJson, "snooze_enabled");
        var snoozeEnd = Long(dndJson, "snooze_endtime");
        var dndEnabled = Bool(dndJson, "dnd_enabled");
        var nextStart = Long(dndJson, "next_dnd_start_ts");
        var nextEnd = Long(dndJson, "next_dnd_end_ts");
        var nowTs = now.ToUnixTimeSeconds();

        return new SlackSelf
        {
            StatusText = Str(profile, "status_text"),
            StatusEmoji = Str(profile, "status_emoji"),
            StatusExpires = expiration > 0 ? DateTimeOffset.FromUnixTimeSeconds(expiration) : null,
            Snoozed = snoozed,
            SnoozeEnds = snoozed && snoozeEnd > 0 ? DateTimeOffset.FromUnixTimeSeconds(snoozeEnd) : null,
            ScheduledDnd = dndEnabled && nextStart > 0 && nextStart <= nowTs && nowTs < nextEnd,
            ManualAway = Bool(presenceJson, "manual_away"),
            Presence = Str(presenceJson, "presence")
        };
    }

    /// <summary>Sets your status. <paramref name="minutes"/> 0 means it stays until cleared.</summary>
    public Task SetStatusAsync(string text, string emoji, int minutes, CancellationToken ct = default)
    {
        var profile = new JsonObject
        {
            ["status_text"] = text,
            ["status_emoji"] = emoji,
            ["status_expiration"] = minutes > 0 ? DateTimeOffset.UtcNow.AddMinutes(minutes).ToUnixTimeSeconds() : 0
        };
        return CallAsync("users.profile.set", [new("profile", profile.ToJsonString())], ct);
    }

    public Task ClearStatusAsync(CancellationToken ct = default) => SetStatusAsync(string.Empty, string.Empty, 0, ct);

    public Task SnoozeAsync(int minutes, CancellationToken ct = default) =>
        CallAsync("dnd.setSnooze", [new("num_minutes", Math.Max(1, minutes).ToString())], ct);

    public Task EndSnoozeAsync(CancellationToken ct = default) => CallAsync("dnd.endSnooze", [], ct);

    /// <summary><c>away</c> marks you away by hand; <c>auto</c> lets Slack decide from your activity.</summary>
    public Task SetPresenceAsync(bool away, CancellationToken ct = default) =>
        CallAsync("users.setPresence", [new("presence", away ? "away" : "auto")], ct);

    private async Task<(JsonNode Json, HttpResponseMessage Response)> CallAsync(string method,
        KeyValuePair<string, string>[] form, CancellationToken ct)
    {
        var token = _token();
        if (string.IsNullOrWhiteSpace(token)) throw new SlackApiException(method, "no_token");

        using var request = new HttpRequestMessage(HttpMethod.Post, method) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

        var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
            throw new SlackApiException(method, $"ratelimited (retry after {wait.TotalSeconds:0}s)");
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var json = JsonNode.Parse(body) ?? throw new SlackApiException(method, "empty_response");
        if (!Bool(json, "ok")) throw new SlackApiException(method, Str(json, "error") is { Length: > 0 } e ? e : "unknown_error");
        return (json, response);
    }

    private static string Str(JsonNode? node, string key) =>
        node?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : string.Empty;

    private static bool Bool(JsonNode? node, string key) =>
        node?[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static long Long(JsonNode? node, string key)
    {
        if (node?[key] is not JsonValue v) return 0;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<double>(out var d)) return (long)d;
        return v.TryGetValue<string>(out var s) && long.TryParse(s, out var p) ? p : 0;
    }

    /// <summary>
    /// The app manifest for the user's own single-workspace Slack app: user scopes only, no bot,
    /// no events, nothing distributed. Slack's "create from manifest" link pre-fills it.
    /// </summary>
    public static string Manifest() => new JsonObject
    {
        ["display_information"] = new JsonObject
        {
            ["name"] = "LoupixDeck",
            ["description"] = "Status, Do Not Disturb and away keys for my LoupixDeck control surface.",
            ["background_color"] = "#4a154b"
        },
        ["oauth_config"] = new JsonObject
        {
            ["scopes"] = new JsonObject { ["user"] = new JsonArray(Scopes.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()) }
        },
        ["settings"] = new JsonObject
        {
            ["org_deploy_enabled"] = false,
            ["socket_mode_enabled"] = false,
            ["token_rotation_enabled"] = false
        }
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

    public static string CreateAppUrl() =>
        "https://api.slack.com/apps?new_app=1&manifest_json=" + Uri.EscapeDataString(Manifest());

    public void Dispose() => _http.Dispose();
}
