using System.Text.Json.Nodes;
using System.Web;
using LoupixDeck.Plugin.Slack.Api;
using LoupixDeck.PluginSdk;
using Xunit;

namespace LoupixDeck.Plugin.Slack.Tests;

/// <summary>A pretend Slack workspace that remembers what the plugin set.</summary>
internal sealed class FakeWorkspace : FakeSlack
{
    public string Text = "", Emoji = "";
    public bool Snoozed, Away;
    public string? SnoozeError;

    public FakeWorkspace(string team, string teamId, string domain)
    {
        Routes["auth.test"] = () =>
        {
            var r = Json($"{{\"ok\":true,\"user\":\"craig\",\"user_id\":\"U1\",\"team\":\"{team}\",\"team_id\":\"{teamId}\",\"url\":\"https://{domain}.slack.com/\"}}");
            r.Headers.Add("x-oauth-scopes", string.Join(",", SlackApi.Scopes));
            return r;
        };
        Routes["users.profile.get"] = () => Json(new JsonObject
        {
            ["ok"] = true, ["profile"] = new JsonObject { ["status_text"] = Text, ["status_emoji"] = Emoji, ["status_expiration"] = 0 }
        }.ToJsonString());
        Routes["dnd.info"] = () => Json($"{{\"ok\":true,\"dnd_enabled\":false,\"snooze_enabled\":{(Snoozed ? "true" : "false")},\"snooze_endtime\":{DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds()}}}");
        Routes["users.getPresence"] = () => Json($"{{\"ok\":true,\"presence\":\"{(Away ? "away" : "active")}\",\"manual_away\":{(Away ? "true" : "false")}}}");
        Routes["users.profile.set"] = () =>
        {
            var profile = JsonNode.Parse(HttpUtility.ParseQueryString(Calls[^1].Body)["profile"]!)!;
            Text = (string)profile["status_text"]!;
            Emoji = (string)profile["status_emoji"]!;
            return Json("{\"ok\":true}");
        };
        Routes["dnd.setSnooze"] = () =>
        {
            if (SnoozeError is not null) return Json($"{{\"ok\":false,\"error\":\"{SnoozeError}\"}}");
            Snoozed = true;
            return Json("{\"ok\":true}");
        };
        Routes["dnd.endSnooze"] = () =>
        {
            if (!Snoozed) return Json("{\"ok\":false,\"error\":\"snooze_not_active\"}");
            Snoozed = false;
            return Json("{\"ok\":true}");
        };
        Routes["users.setPresence"] = () =>
        {
            Away = HttpUtility.ParseQueryString(Calls[^1].Body)["presence"] == "away";
            return Json("{\"ok\":true}");
        };
    }

    public int Count(string method) => Calls.Count(c => c.Method == method);
}

public sealed class WorkspaceTests
{
    private readonly FakeWorkspace _acme = new("Acme", "T0001AAAA", "acme");
    private readonly FakeWorkspace _beta = new("Beta Corp", "T0002BBBB", "beta");

    private async Task<(SlackPlugin Plugin, FakeHost Host)> LoadAsync()
    {
        var host = new FakeHost();
        host.Values["bring_to_front"] = false;
        host.Values["token_1"] = "xoxp-acme";
        host.Values["token_3"] = "xoxp-beta"; // gaps between slots are fine
        var plugin = new SlackPlugin { HandlerFactory = t => t == "xoxp-acme" ? _acme : _beta };
        plugin.Initialize(host);
        await plugin.Workspaces.RefreshAsync();
        return (plugin, host);
    }

    private static IPluginCommand Command(SlackPlugin p, string name) => p.GetCommands().Single(c => c.Descriptor.CommandName == name);

    private static CommandContext Ctx(FakeHost host, params string[] p) =>
        new() { Parameters = p, Target = ButtonTargets.TouchButton, SourceIndex = 0, Host = host };

    [Fact]
    public async Task Workspaces_are_found_by_name_domain_id_or_slot()
    {
        var (plugin, _) = await LoadAsync();
        var set = plugin.Workspaces;

        Assert.Equal(2, set.Select(null).Count);
        Assert.Equal(2, set.Select("all").Count);
        Assert.Equal("Acme", set.Select("acme").Single().Name);
        Assert.Equal("Beta Corp", set.Select("beta corp").Single().Name);
        Assert.Equal("Beta Corp", set.Select("beta.slack.com").Single().Name);
        Assert.Equal("Beta Corp", set.Select("T0002BBBB").Single().Name);
        Assert.Equal("Beta Corp", set.Select("3").Single().Name);
        Assert.Equal(2, set.Select("acme;3").Count);
        Assert.Empty(set.Select("nowhere"));
        plugin.Shutdown();
    }

    [Fact]
    public async Task Status_preset_spans_every_workspace_and_shows_the_share()
    {
        var (plugin, host) = await LoadAsync();
        var status = (IValueDisplayCommand)Command(plugin, "Slack.Status");
        string[] lunch = ["Lunch", ":taco:", "0"];
        _acme.Text = "Lunch"; _acme.Emoji = ":taco:";
        await plugin.Workspaces.RefreshAsync();

        var half = status.GetValue(Ctx(host, lunch))!.Value;
        Assert.Equal(0.5, half.Normalized);
        Assert.Equal("1/2", half.Detail);

        await status.Execute(Ctx(host, lunch)); // not everywhere yet: set it everywhere
        Assert.Equal("Lunch", _beta.Text);
        Assert.Equal(1.0, status.GetValue(Ctx(host, lunch))!.Value.Normalized);

        await status.Execute(Ctx(host, lunch)); // everywhere: clear everywhere
        Assert.Equal("", _acme.Text);
        Assert.Equal("", _beta.Text);
        plugin.Shutdown();
    }

    [Fact]
    public async Task Workspace_parameter_limits_a_key_to_one_workspace()
    {
        var (plugin, host) = await LoadAsync();
        var status = Command(plugin, "Slack.Status");

        await status.Execute(Ctx(host, "Focus time", ":headphones:", "30", "beta"));

        Assert.Equal("Focus time", _beta.Text);
        Assert.Equal(0, _acme.Count("users.profile.set"));
        plugin.Shutdown();
    }

    [Fact]
    public async Task Partial_failure_is_reported_and_the_rest_still_change()
    {
        var (plugin, host) = await LoadAsync();
        _beta.SnoozeError = "missing_scope";

        await Command(plugin, "Slack.Focus").Execute(Ctx(host, "45"));

        Assert.True(_acme.Snoozed);
        Assert.Equal(["1/2 failed"], host.Overlays);
        var value = ((IValueDisplayCommand)Command(plugin, "Slack.Focus")).GetValue(Ctx(host, "45"))!.Value;
        Assert.Equal("1/2", value.Detail);

        // One tap resumes notifications wherever they are paused.
        await Command(plugin, "Slack.Focus").Execute(Ctx(host, "45"));
        Assert.False(_acme.Snoozed);
        plugin.Shutdown();
    }

    [Fact]
    public async Task Away_goes_everywhere_then_back()
    {
        var (plugin, host) = await LoadAsync();
        var away = Command(plugin, "Slack.Away");

        await away.Execute(Ctx(host));
        Assert.True(_acme.Away && _beta.Away);
        await away.Execute(Ctx(host));
        Assert.False(_acme.Away || _beta.Away);
        plugin.Shutdown();
    }

    [Fact]
    public async Task Conversation_links_find_their_workspace_by_domain()
    {
        var (plugin, host) = await LoadAsync();
        var open = Command(plugin, "Slack.Conversation");

        await open.Execute(Ctx(host, "https://beta.slack.com/archives/C0123ABCD", "Ops"));
        await open.Execute(Ctx(host, "C0999ZZZZ", "General", "acme"));
        await open.Execute(Ctx(host, "https://other.slack.com/archives/C0123ABCD"));

        Assert.Equal(
        [
            "slack://channel?team=T0002BBBB&id=C0123ABCD",
            "slack://channel?team=T0001AAAA&id=C0999ZZZZ",
            "https://other.slack.com/archives/C0123ABCD" // unknown workspace: Slack's own redirect
        ], host.Opened);
        plugin.Shutdown();
    }

    [Fact]
    public async Task Workspace_key_switches_slack_and_shows_the_name()
    {
        var (plugin, host) = await LoadAsync();
        var go = Command(plugin, "Slack.Workspace");

        Assert.Equal("Beta Corp", ((IValueDisplayCommand)go).GetValue(Ctx(host, "beta"))!.Value.Text);
        await go.Execute(Ctx(host, "beta"));
        await go.Execute(Ctx(host, "T0009ZZZZ")); // a team id works without a token

        Assert.Equal(["slack://open?team=T0002BBBB", "slack://open?team=T0009ZZZZ"], host.Opened);
        plugin.Shutdown();
    }
}
