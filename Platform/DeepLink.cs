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

    /// <summary>Null when no conversation id can be found or no team id is known.</summary>
    public static string? ForConversation(string? input, string? defaultTeam)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var text = input.Trim();

        var team = TeamPattern().Match(text) is { Success: true } t ? t.Groups["team"].Value : defaultTeam;
        var id = ConversationPattern().Match(text) is { Success: true } c ? c.Groups["id"].Value : null;
        if (id is null || string.IsNullOrWhiteSpace(team)) return null;

        // Users open as a DM through slack://user; channels, private channels and DMs through slack://channel.
        var kind = id[0] == 'U' ? "user" : "channel";
        return $"slack://{kind}?team={Uri.EscapeDataString(team)}&id={Uri.EscapeDataString(id)}";
    }
}
