using LoupixDeck.Plugin.Slack.Shortcuts;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Slack.Commands;

/// <summary>
/// A dial that steps through something in Slack: turn left for one shortcut, right for the other,
/// press for a third. Each tick is one key press, capped so a fast spin cannot flood Slack.
/// </summary>
internal sealed class DialCommand : SlackCommandBase, IAdjustmentCommand
{
    private const int MaxTicksPerTurn = 5;

    private readonly Shortcut _left;
    private readonly Shortcut _right;
    private readonly Shortcut _press;

    private DialCommand(SlackPlugin plugin, string id, string displayName, string glyph, string description,
        string left, string right, string press) : base(plugin)
    {
        _left = Find(left);
        _right = Find(right);
        _press = Find(press);
        Descriptor = new CommandDescriptor
        {
            CommandName = "Slack." + id,
            DisplayName = displayName,
            Group = ShortcutCatalog.Navigate,
            Icon = glyph,
            Description = description
        };
    }

    public override CommandDescriptor Descriptor { get; }

    public override ButtonTargets SupportedTargets => ButtonTargets.RotaryEncoder;

    public static IEnumerable<DialCommand> All(SlackPlugin plugin) =>
    [
        new(plugin, "ConversationDial", "Conversation dial", "\U000F0369", // mdi-message-text
            "Turn to move up and down the sidebar's conversations; press to jump to a conversation.",
            "PreviousConversation", "NextConversation", "JumpTo"),
        new(plugin, "UnreadDial", "Unread dial", "\U000F0B92", // mdi-email-mark-as-unread
            "Turn to step through conversations with unread messages; press to mark the open one read.",
            "PreviousUnread", "NextUnread", "MarkRead"),
        new(plugin, "HistoryDial", "History dial", "\U000F02DA", // mdi-history
            "Turn to go back and forward in Slack's history; press for All unreads.",
            "Back", "Forward", "Unreads"),
        new(plugin, "ZoomDial", "Zoom dial", "\U000F0349", // mdi-magnify
            "Turn to zoom Slack out and in; press for actual size.",
            "ZoomOut", "ZoomIn", "ActualSize")
    ];

    private static Shortcut Find(string id) => ShortcutCatalog.All.Single(s => s.Id == id);

    public Task ApplyAdjustment(CommandContext ctx, int ticks) => GuardAsync(ctx, Descriptor.CommandName, async () =>
    {
        if (ticks == 0) return;
        var shortcut = ticks < 0 ? _left : _right;
        await Plugin.Keys.SendAsync(ctx.Host, shortcut.ForThisOs, Math.Min(Math.Abs(ticks), MaxTicksPerTurn)).ConfigureAwait(false);
    });

    public Task ApplyReset(CommandContext ctx) => Press(ctx);

    public override Task Execute(CommandContext ctx) => Press(ctx);

    private Task Press(CommandContext ctx) => GuardAsync(ctx, Descriptor.CommandName, () =>
        Plugin.Keys.SendAsync(ctx.Host, _press.ForThisOs));
}
