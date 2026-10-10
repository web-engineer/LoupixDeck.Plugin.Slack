using System.Net;
using System.Text.Json.Nodes;
using LoupixDeck.Plugin.Slack.Api;
using Xunit;

namespace LoupixDeck.Plugin.Slack.Tests;

/// <summary>Answers Slack API calls from a table and records what was sent.</summary>
internal sealed class FakeSlack : HttpMessageHandler
{
    public readonly Dictionary<string, Func<HttpResponseMessage>> Routes = new();
    public readonly List<(string Method, string Body, string? Auth)> Calls = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var method = request.RequestUri!.AbsolutePath.TrimStart('/').Replace("api/", "");
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        Calls.Add((method, body, request.Headers.Authorization?.ToString()));
        return Routes.TryGetValue(method, out var r) ? r() : Json("{\"ok\":false,\"error\":\"unknown_method\"}");
    }

    public static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}

public sealed class ApiTests
{
    private static (SlackApi Api, FakeSlack Fake) Make(string? token = "xoxp-test")
    {
        var fake = new FakeSlack();
        return (new SlackApi(() => token, fake), fake);
    }

    [Fact]
    public async Task Auth_test_reads_identity_and_scopes()
    {
        var (api, fake) = Make();
        fake.Routes["auth.test"] = () =>
        {
            var r = FakeSlack.Json("{\"ok\":true,\"user\":\"craig\",\"user_id\":\"U1\",\"team\":\"Acme\",\"team_id\":\"T1\"}");
            r.Headers.Add("x-oauth-scopes", "users.profile:read, dnd:read");
            return r;
        };

        var id = await api.AuthTestAsync();

        Assert.Equal("craig", id.User);
        Assert.Equal("T1", id.TeamId);
        Assert.Contains("dnd:read", id.Scopes);
        Assert.Equal("Bearer xoxp-test", fake.Calls.Single().Auth);
    }

    [Fact]
    public async Task Slack_errors_surface_their_code()
    {
        var (api, fake) = Make();
        fake.Routes["dnd.setSnooze"] = () => FakeSlack.Json("{\"ok\":false,\"error\":\"missing_scope\"}");

        var ex = await Assert.ThrowsAsync<SlackApiException>(() => api.SnoozeAsync(30));
        Assert.Equal("missing_scope", ex.Error);
        Assert.Contains("num_minutes=30", fake.Calls.Single().Body);
    }

    [Fact]
    public async Task Rate_limit_is_reported_not_thrown_as_http_error()
    {
        var (api, fake) = Make();
        fake.Routes["users.setPresence"] = () =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(12));
            return r;
        };

        var ex = await Assert.ThrowsAsync<SlackApiException>(() => api.SetPresenceAsync(away: true));
        Assert.StartsWith("ratelimited", ex.Error);
    }

    [Fact]
    public async Task No_token_never_calls_slack()
    {
        var (api, fake) = Make(token: " ");
        Assert.False(api.HasToken);
        await Assert.ThrowsAsync<SlackApiException>(() => api.ClearStatusAsync());
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task Set_status_sends_profile_json_with_expiry()
    {
        var (api, fake) = Make();
        fake.Routes["users.profile.set"] = () => FakeSlack.Json("{\"ok\":true}");

        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await api.SetStatusAsync("In a meeting", ":calendar:", 30);

        var form = System.Web.HttpUtility.ParseQueryString(fake.Calls.Single().Body);
        var profile = JsonNode.Parse(form["profile"]!)!;
        Assert.Equal("In a meeting", (string?)profile["status_text"]);
        Assert.Equal(":calendar:", (string?)profile["status_emoji"]);
        var exp = (long)profile["status_expiration"]!;
        Assert.InRange(exp, before + 30 * 60 - 5, before + 30 * 60 + 5);
    }

    [Fact]
    public void Parses_self_snapshot()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var self = SlackApi.ParseSelf(
            JsonNode.Parse("{\"ok\":true,\"profile\":{\"status_text\":\"Lunch\",\"status_emoji\":\":fork_and_knife:\",\"status_expiration\":1800001800}}")!,
            JsonNode.Parse("{\"ok\":true,\"dnd_enabled\":true,\"next_dnd_start_ts\":1800030000,\"next_dnd_end_ts\":1800060000,\"snooze_enabled\":true,\"snooze_endtime\":1800000600,\"snooze_remaining\":600}")!,
            JsonNode.Parse("{\"ok\":true,\"presence\":\"away\",\"manual_away\":true}")!,
            now);

        Assert.Equal("Lunch", self.StatusText);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_800_001_800), self.StatusExpires);
        Assert.True(self.Snoozed);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_800_000_600), self.SnoozeEnds);
        Assert.False(self.ScheduledDnd); // the schedule starts later
        Assert.True(self.ManualAway);
    }

    [Fact]
    public void Scheduled_dnd_counts_only_inside_the_window()
    {
        var profile = JsonNode.Parse("{\"profile\":{}}")!;
        var presence = JsonNode.Parse("{\"presence\":\"active\"}")!;
        var dnd = JsonNode.Parse("{\"dnd_enabled\":true,\"next_dnd_start_ts\":100,\"next_dnd_end_ts\":200,\"snooze_enabled\":false}")!;

        Assert.True(SlackApi.ParseSelf(profile, dnd, presence, DateTimeOffset.FromUnixTimeSeconds(150)).ScheduledDnd);
        Assert.False(SlackApi.ParseSelf(profile, dnd, presence, DateTimeOffset.FromUnixTimeSeconds(250)).ScheduledDnd);
    }

    [Fact]
    public void Manifest_asks_for_user_scopes_only()
    {
        var manifest = JsonNode.Parse(SlackApi.Manifest())!;
        var scopes = manifest["oauth_config"]!["scopes"]!;
        Assert.Null(scopes["bot"]);
        Assert.Equal(SlackApi.Scopes, scopes["user"]!.AsArray().Select(s => (string)s!).ToArray());
        Assert.StartsWith("https://api.slack.com/apps?new_app=1&manifest_json=", SlackApi.CreateAppUrl());
    }
}
