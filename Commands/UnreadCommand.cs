using LoupixDeck.Plugin.Slack.Platform;
using LoupixDeck.Plugin.Slack.Rendering;
using LoupixDeck.Plugin.Slack.Shortcuts;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Slack.Commands;

/// <summary>
/// Mirrors Slack's own app badge on a key: quiet, unread (a dot) or a count of mentions and DMs.
/// On macOS the badge is read from the Dock, so it needs no Slack token. Tap to jump to Activity
/// when something is for you, All unreads otherwise.
/// </summary>
internal sealed class UnreadCommand(SlackPlugin plugin) : SlackCommandBase(plugin), IValueDisplayCommand
{
    public const string Name = "Slack.Unread";
    public const string ClearState = "clear";
    public const string UnreadState = "unread";
    public const string MentionsState = "mentions";

    public override CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = Name,
        DisplayName = "Unread badge",
        Group = ShortcutCatalog.Navigate,
        Icon = "\U000F009A", // mdi-bell
        Description = "Shows Slack's badge: nothing new, unread messages, or how many mentions and DMs wait for you (macOS). Tap to go to them.",
        States =
        [
            new ButtonStateDescriptor
            {
                Name = MentionsState, Description = "Mentions or DMs are waiting",
                Layout = SlackKey.State(SlackKey.RedHex,
                    SlackKey.Icon("Bell", "\U000F009E", 0.36, -16), // mdi-bell-ring
                    SlackKey.ValueText("Count", ButtonTextSource.Value, 22, 16))
            },
            new ButtonStateDescriptor
            {
                Name = UnreadState, Description = "Unread messages, none for you",
                Layout = SlackKey.State(SlackKey.AubergineHex,
                    SlackKey.Icon("Dot", "\U000F0765", 0.4, -8), // mdi-circle-medium
                    SlackKey.Caption("Unread"))
            },
            new ButtonStateDescriptor
            {
                Name = ClearState, Description = "Nothing new, or the badge cannot be read",
                Layout = SlackKey.State(SlackKey.AubergineHex,
                    SlackKey.Icon("Check", "\U000F012C", 0.4, -9), // mdi-check
                    SlackKey.ValueText("Status", ButtonTextSource.Value, 13, 26))
            }
        ],
        ButtonLayout = new ButtonLayoutDescriptor { Mode = ButtonLayoutMode.None }
    };

    public override ButtonTargets SupportedTargets => ButtonTargets.TouchButton;

    /// <summary>Keeps the badge watcher alive; changes arrive by push from the watcher.</summary>
    public TimeSpan UpdateInterval => TimeSpan.FromSeconds(5);

    public AdjustmentValue? GetValue(CommandContext ctx)
    {
        if (!OperatingSystem.IsMacOS())
        {
            Show(ctx, Name, ClearState);
            return new AdjustmentValue(double.NaN, ctx.Host.Tr("macOS only"));
        }

        Plugin.BadgeGate.Touch();
        var badge = Plugin.Badges.Current;
        switch (badge.Kind)
        {
            case BadgeKind.Mentions:
                Show(ctx, Name, MentionsState);
                return new AdjustmentValue(double.NaN, badge.Label);
            case BadgeKind.Unread:
                Show(ctx, Name, UnreadState);
                return null;
            case BadgeKind.Clear:
                Show(ctx, Name, ClearState);
                return new AdjustmentValue(double.NaN, ctx.Host.Tr("All read"));
            default:
                Show(ctx, Name, ClearState);
                return new AdjustmentValue(double.NaN, ctx.Host.Tr(SlackApp.IsInstalled ? "Not running" : "No Slack"));
        }
    }

    public override Task Execute(CommandContext ctx) => GuardAsync(ctx, "unread", async () =>
    {
        var target = Plugin.Badges.Current.Kind switch
        {
            BadgeKind.Mentions => "Activity",
            BadgeKind.Unread => "Unreads",
            _ => null
        };

        if (target is null)
        {
            if (!await SlackApp.ActivateAsync(ctx.Host).ConfigureAwait(false)) Hint(ctx, "Slack not found");
            return;
        }

        var shortcut = ShortcutCatalog.All.Single(s => s.Id == target);
        await Plugin.Keys.SendAsync(ctx.Host, shortcut.ForThisOs).ConfigureAwait(false);
        Plugin.Badges.Poll();
    });
}
