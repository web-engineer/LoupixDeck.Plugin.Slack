using System.Text.RegularExpressions;

namespace LoupixDeck.Plugin.Slack.Platform;

/// <summary>
/// Builds <c>slack://</c> links (docs.slack.dev/interactivity/deep-linking) from what a user is
/// likely to paste: a bare id, <c>T…/C…</c>, or a copied link such as
/// <c>https://acme.slack.com/archives/C0123ABCD</c> or <c>https://app.slack.com/client/T…/C…</c>.
/// </summary>
internal static partial class DeepLink
{
    [GeneratedRegex(@"\b(?<team>T[A-Z0-9]{6,})\b")]
    private static partial Regex TeamPattern();

    [GeneratedRegex(@"(?:^|[/\s=])(?<id>[CGDU][A-Z0-9]{6,})\b")]
    private static partial Regex ConversationPattern();

    [GeneratedRegex(@"^https?://(?<domain>[a-z0-9-]+)\.slack\.com/", RegexOptions.IgnoreCase)]
    private static partial Regex DomainPattern();

    /// <summary>
    /// Null when no conversation id can be found or the workspace cannot be worked out. The team
    /// comes from, in order: a T… id in the input, the subdomain of a copied link (looked up with
    /// <paramref name="teamForDomain"/>), then <paramref name="defaultTeam"/>.
    /// </summary>
    public static string? ForConversation(string? input, string? defaultTeam, Func<string, string?>? teamForDomain = null)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var text = input.Trim();

        var team = TeamPattern().Match(text) is { Success: true } t ? t.Groups["team"].Value
            : DomainPattern().Match(text) is { Success: true } d && d.Groups["domain"].Value != "app" && teamForDomain?.Invoke(d.Groups["domain"].Value) is { } byDomain ? byDomain
            : defaultTeam;
        var id = ConversationPattern().Match(text) is { Success: true } c ? c.Groups["id"].Value : null;
        if (id is null || string.IsNullOrWhiteSpace(team)) return null;

        // Users open as a DM through slack://user; channels, private channels and DMs through slack://channel.
        var kind = id[0] == 'U' ? "user" : "channel";
        return $"slack://{kind}?team={Uri.EscapeDataString(team)}&id={Uri.EscapeDataString(id)}";
    }

    /// <summary>Opens the desktop app on one workspace.</summary>
    public static string ForWorkspace(string teamId) => $"slack://open?team={Uri.EscapeDataString(teamId)}";

    /// <summary>True for something shaped like a team id (T…).</summary>
    public static bool IsTeamId(string text) => TeamPattern().Match(text) is { Success: true } m && m.Value == text.Trim();
}
