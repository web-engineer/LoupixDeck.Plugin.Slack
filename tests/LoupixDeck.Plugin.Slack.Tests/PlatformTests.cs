using LoupixDeck.Plugin.Slack.Platform;
using LoupixDeck.Plugin.Slack.Shortcuts;
using Xunit;

namespace LoupixDeck.Plugin.Slack.Tests;

public sealed class DockBadgeTests
{
    [Theory]
    [InlineData("\"StatusLabel\"={ \"label\"=\"6\" }", "Mentions", 6, "6")]
    [InlineData("\"StatusLabel\"={ \"label\"=\"99+\" }", "Mentions", 99, "99+")]
    [InlineData("\"StatusLabel\"={ \"label\"=\"•\" }", "Unread", 0, "•")]
    [InlineData("\"StatusLabel\"={ \"label\"=\"\" }", "Clear", 0, "")]
    [InlineData("\"StatusLabel\"=[ NULL ]", "Clear", 0, "")]
    public void Parses_lsappinfo_output(string output, string kind, int count, string label)
    {
        var badge = DockBadge.Parse(output);
        Assert.Equal(Enum.Parse<BadgeKind>(kind), badge.Kind);
        Assert.Equal(count, badge.Count);
        Assert.Equal(label, badge.Label);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\"bundleid\"=\"com.example\"")]
    public void Slack_not_running_is_unknown(string? output) =>
        Assert.Equal(BadgeKind.Unknown, DockBadge.Parse(output).Kind);

    [Fact]
    public void Monitor_raises_changed_only_on_change()
    {
        var next = Badge.Clear;
        using var monitor = new BadgeMonitor(() => next);
        var changes = 0;
        monitor.Changed += () => changes++;

        monitor.Poll();
        monitor.Poll();
        next = new Badge(BadgeKind.Mentions, 2, "2");
        monitor.Poll();
        monitor.Poll();

        Assert.Equal(2, changes);
        Assert.Equal(BadgeKind.Mentions, monitor.Current.Kind);
    }
}

public sealed class DeepLinkTests
{
    [Theory]
    [InlineData("C0123ABCD", "T0001AAAA", "slack://channel?team=T0001AAAA&id=C0123ABCD")]
    [InlineData("T0002BBBB/C0123ABCD", null, "slack://channel?team=T0002BBBB&id=C0123ABCD")]
    [InlineData("https://app.slack.com/client/T0002BBBB/C0123ABCD", null, "slack://channel?team=T0002BBBB&id=C0123ABCD")]
    [InlineData("https://acme.slack.com/archives/C0123ABCD", "T0001AAAA", "slack://channel?team=T0001AAAA&id=C0123ABCD")]
    [InlineData("https://acme.slack.com/archives/C0123ABCD/p1700000000123456", "T0001AAAA", "slack://channel?team=T0001AAAA&id=C0123ABCD")]
    [InlineData("D0123ABCD", "T0001AAAA", "slack://channel?team=T0001AAAA&id=D0123ABCD")]
    [InlineData("U0123ABCD", "T0001AAAA", "slack://user?team=T0001AAAA&id=U0123ABCD")]
    public void Builds_links(string input, string? team, string expected) =>
        Assert.Equal(expected, DeepLink.ForConversation(input, team));

    [Theory]
    [InlineData("C0123ABCD", null)]   // no team known
    [InlineData("#general", "T0001AAAA")] // names are not supported by Slack's deep links
    [InlineData("", "T0001AAAA")]
    public void Refuses_what_slack_cannot_open(string input, string? team) =>
        Assert.Null(DeepLink.ForConversation(input, team));
}

public sealed class ShortcutCatalogTests
{
    /// <summary>
    /// Key names the host's macro engine knows on both macOS and Windows (LoupixDeck
    /// Utils/KeyNames.cs). A typo here would press nothing, silently.
    /// </summary>
    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cmd", "Ctrl", "Alt", "Shift",
        "A", "B", "C", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "S", "T", "U", "X", "Y",
        "0", "1", "2", "7", "8", "9",
        "Up", "Down", "Left", "Right", "Esc", "Enter", "Space", "F11",
        "LeftBracket", "RightBracket", "Period", "Comma", "Equals", "Minus"
    };

    [Fact]
    public void Ids_are_unique() =>
        Assert.Equal(ShortcutCatalog.All.Count, ShortcutCatalog.All.Select(s => s.Id).Distinct().Count());

    [Fact]
    public void Every_key_name_is_one_the_host_knows()
    {
        foreach (var s in ShortcutCatalog.All)
        foreach (var combo in new[] { s.Mac, s.Other })
        foreach (var key in combo.Split('+'))
            Assert.True(KnownKeys.Contains(key), $"{s.Id}: unknown key '{key}' in '{combo}'");
    }

    [Fact]
    public void No_combination_contains_a_comma()
    {
        // The host splits command parameters on commas, so "Cmd+," would arrive as "Cmd+".
        Assert.All(ShortcutCatalog.All, s => Assert.DoesNotContain(',', s.Mac + s.Other));
    }

    [Fact]
    public void Windows_combinations_never_use_cmd()
    {
        // Cmd maps to the Windows key there, which would open the Start menu.
        Assert.All(ShortcutCatalog.All, s => Assert.DoesNotContain("Cmd", s.Other));
    }

    [Theory]
    [InlineData("JumpTo", "Cmd+K", "Ctrl+K")]
    [InlineData("HuddleMute", "Cmd+Shift+Space", "Ctrl+Shift+Space")]
    [InlineData("Back", "Cmd+LeftBracket", "Alt+Left")]
    [InlineData("DMs", "Cmd+Shift+K", "Ctrl+Shift+2")]
    public void Spot_checks_against_slacks_help_page(string id, string mac, string other)
    {
        var s = ShortcutCatalog.All.Single(x => x.Id == id);
        Assert.Equal(mac, s.Mac);
        Assert.Equal(other, s.Other);
    }
}
