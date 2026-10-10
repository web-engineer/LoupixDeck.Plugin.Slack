using LoupixDeck.Plugin.Slack.Platform;
using LoupixDeck.Plugin.Slack.Rendering;
using LoupixDeck.Plugin.Slack.Shortcuts;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Slack.Commands;

/// <summary>Brings Slack to the front, starting it when it is not running.</summary>
internal sealed class OpenCommand(SlackPlugin plugin) : SlackCommandBase(plugin)
{
    public const string Name = "Slack.Open";

    public override CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = Name,
        DisplayName = "Open Slack",
        Group = ShortcutCatalog.Navigate,
        Icon = "\U000F0424", // mdi-pound-box
        Description = "Bring Slack to the front, starting it if needed.",
        ButtonLayout = SlackKey.IconAndCaption("\U000F0424", "Slack")
    };

    public override Task Execute(CommandContext ctx) => GuardAsync(ctx, "open", async () =>
    {
        if (!await SlackApp.ActivateAsync(ctx.Host).ConfigureAwait(false)) Hint(ctx, "Slack not found");
    });
}

/// <summary>
/// Opens one conversation through Slack's deep link. Slack links take ids, not names: the team id
/// (T…) and the channel (C…, G…), DM (D…) or user (U…) id. Copy a link to the conversation in
/// Slack and the ids are in it; the settings page also reports your team id.
/// </summary>
internal sealed class ConversationCommand(SlackPlugin plugin) : SlackCommandBase(plugin)
{
    public const string Name = "Slack.Conversation";
    private const string ConversationParameter = "Conversation";
    private const string CaptionParameter = "Caption";

    public override CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = Name,
        DisplayName = "Open conversation",
        Group = ShortcutCatalog.Navigate,
        Icon = "\U000F0423", // mdi-pound
        Description = "Open a channel, DM or person by id, or paste a Slack link to it.",
        ParameterTemplate = $"({{{ConversationParameter}}},{{{CaptionParameter}}})",
        Parameters =
        [
            new CommandParameter(ConversationParameter, typeof(string)),
            new CommandParameter(CaptionParameter, typeof(string))
        ],
        ButtonLayout = SlackKey.IconAndCaption("\U000F0423", "Channel")
    };

    public override Task Execute(CommandContext ctx) => GuardAsync(ctx, "open conversation", () =>
    {
        var link = DeepLink.ForConversation(Parameter(ctx, 0), Plugin.TeamId);
        if (link is null)
        {
            Hint(ctx, "Set channel");
            ctx.Host.Logger.Warn($"Slack.Conversation: cannot build a link from '{Parameter(ctx, 0)}'. Use a channel id with a known team, T…/C…, or a Slack link.");
            return Task.CompletedTask;
        }

        if (!SlackApp.OpenLink(ctx.Host, link)) Hint(ctx, "Slack not found");
        return Task.CompletedTask;
    });
}
