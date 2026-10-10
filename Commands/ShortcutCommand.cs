using LoupixDeck.Plugin.Slack.Rendering;
using LoupixDeck.Plugin.Slack.Shortcuts;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Slack.Commands;

/// <summary>One key per Slack shortcut: bring Slack to the front, then press the shortcut.</summary>
internal sealed class ShortcutCommand(SlackPlugin plugin, Shortcut shortcut) : SlackCommandBase(plugin)
{
    public Shortcut Shortcut { get; } = shortcut;

    public override CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = shortcut.CommandName,
        DisplayName = shortcut.DisplayName,
        Group = shortcut.Group,
        Icon = shortcut.Glyph,
        Description = $"{shortcut.Description} ({(OperatingSystem.IsMacOS() ? shortcut.Mac : shortcut.Other)})",
        ButtonLayout = SlackKey.IconAndCaption(shortcut.Glyph, shortcut.Caption)
    };

    public static IEnumerable<ShortcutCommand> All(SlackPlugin plugin) =>
        ShortcutCatalog.All.Select(s => new ShortcutCommand(plugin, s));

    public override Task Execute(CommandContext ctx) => GuardAsync(ctx, Shortcut.Id, async () =>
    {
        if (!await Plugin.Keys.SendAsync(ctx.Host, Shortcut.ForThisOs).ConfigureAwait(false))
            Hint(ctx, "Slack not found");
    });
}
