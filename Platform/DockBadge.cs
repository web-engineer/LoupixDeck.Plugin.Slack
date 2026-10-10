using System.Text.RegularExpressions;

namespace LoupixDeck.Plugin.Slack.Platform;

/// <summary>
/// What Slack's own app badge says: nothing, a dot (unread channels, no mentions) or a count of
/// mentions and DMs. On macOS the Dock label is public through <c>lsappinfo</c>, so this needs no
/// Slack token and no permission. Other platforms report <see cref="BadgeKind.Unknown"/>.
/// </summary>
internal enum BadgeKind
{
    /// <summary>The badge could not be read (Slack not running, or not macOS).</summary>
    Unknown,
    /// <summary>No badge: nothing unread.</summary>
    Clear,
    /// <summary>A dot: unread messages, none of them for you.</summary>
    Unread,
    /// <summary>A number: mentions and direct messages waiting.</summary>
    Mentions
}

internal readonly record struct Badge(BadgeKind Kind, int Count, string Label)
{
    public static readonly Badge Unknown = new(BadgeKind.Unknown, 0, string.Empty);
    public static readonly Badge Clear = new(BadgeKind.Clear, 0, string.Empty);
}

internal static partial class DockBadge
{
    [GeneratedRegex("\"label\"\\s*=\\s*\"(?<label>[^\"]*)\"")]
    private static partial Regex LabelPattern();

    /// <summary>Reads Slack's Dock badge now. Slow-ish (a child process): call it from a worker, not a render.</summary>
    public static Badge Read()
    {
        if (!OperatingSystem.IsMacOS()) return Badge.Unknown;
        var output = SlackApp.Capture("lsappinfo", "info", "-only", "StatusLabel", "-app", SlackApp.MacBundleId);
        return Parse(output);
    }

    /// <summary>
    /// Parses <c>lsappinfo</c> output. A running Slack without a badge prints
    /// <c>"StatusLabel"=[ NULL ]</c> or nothing after the key; Slack not running prints nothing.
    /// </summary>
    internal static Badge Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return Badge.Unknown;
        if (!output.Contains("StatusLabel", StringComparison.Ordinal)) return Badge.Unknown;

        var match = LabelPattern().Match(output);
        if (!match.Success) return Badge.Clear;

        var label = match.Groups["label"].Value.Trim();
        if (label.Length == 0) return Badge.Clear;
        if (int.TryParse(label, out var count)) return new Badge(BadgeKind.Mentions, count, label);

        // "99+" and similar keep the label as Slack drew it.
        if (label.EndsWith('+') && int.TryParse(label.TrimEnd('+'), out var floor))
            return new Badge(BadgeKind.Mentions, floor, label);

        // Slack draws a bullet for "unread, nothing for you".
        return new Badge(BadgeKind.Unread, 0, label);
    }
}
