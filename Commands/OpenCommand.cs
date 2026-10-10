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
/// Opens one conversation through Slack's deep link. Slack links take ids, not names: paste a copied
/// Slack link (its workspace is worked out from the link), T…/C…, or a bare channel (C…, G…), DM (D…)
/// or user (U…) id together with the <c>Workspace</c> parameter.
/// </summary>
internal sealed class ConversationCommand(SlackPlugin plugin) : SlackCommandBase(plugin)
{
    public const string Name = "Slack.Conversation";

    public override CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = Name,
        DisplayName = "Open conversation",
        Group = ShortcutCatalog.Navigate,
        Icon = "\U000F0423", // mdi-pound
        Description = "Open a channel, DM or person: paste a Slack link to it, or give its id and the workspace.",
        ParameterTemplate = "({Conversation},{Caption},{Workspace})",
        Parameters =
        [
            new CommandParameter("Conversation", typeof(string)),
            new CommandParameter("Caption", typeof(string)),
            new CommandParameter("Workspace", typeof(string))
        ],
        ButtonLayout = SlackKey.IconAndCaption("\U000F0423", "Channel")
    };

    public override Task Execute(CommandContext ctx) => GuardAsync(ctx, "open conversation", () =>
    {
        var set = Plugin.Workspaces;
        var workspace = Parameter(ctx, 2);
        var defaultTeam = workspace is not null
            ? DeepLink.IsTeamId(workspace) ? workspace : set.Select(workspace).FirstOrDefault()?.Identity?.TeamId
            : set.All.Count == 1 ? set.All[0].Identity?.TeamId : null;

        var link = DeepLink.ForConversation(Parameter(ctx, 0), defaultTeam, d => set.ByDomain(d)?.Identity?.TeamId);
        // A copied link from a workspace with no token: let Slack's own redirect open it.
        if (link is null && Parameter(ctx, 0) is { } raw && raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            raw.Contains(".slack.com/", StringComparison.OrdinalIgnoreCase))
            link = raw;

        if (link is null)
        {
            Hint(ctx, "Set channel");
            ctx.Host.Logger.Warn($"Slack.Conversation: cannot build a link from '{Parameter(ctx, 0)}' (workspace '{workspace}'). " +
                                 "Paste a Slack link, T…/C…, or an id plus a connected workspace.");
            return Task.CompletedTask;
        }

        if (!SlackApp.OpenLink(ctx.Host, link)) Hint(ctx, "Slack not found");
        return Task.CompletedTask;
    });
}

/// <summary>
/// Switches the Slack desktop app to one workspace. Give the workspace by name, subdomain or slot
/// (needs its token), or by team id (T…, works without a token). The key shows the workspace name.
/// </summary>
internal sealed class WorkspaceCommand(SlackPlugin plugin) : SlackCommandBase(plugin), IValueDisplayCommand
{
    public const string Name = "Slack.Workspace";

    public override CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = Name,
        DisplayName = "Go to workspace",
        Group = ShortcutCatalog.Navigate,
        Icon = "\U000F0BCD", // mdi-swap-horizontal-bold
        Description = "Switch Slack to one workspace, by name, subdomain, slot number or team id.",
        ParameterTemplate = "({Workspace})",
        Parameters = [new CommandParameter("Workspace", typeof(string)) { DefaultValue = "1" }],
        ButtonLayout = SlackKey.State(SlackKey.AubergineHex,
            SlackKey.Icon("Icon", "\U000F0BCD", 0.4, -10), // mdi-swap-horizontal-bold
            SlackKey.ValueText("Workspace", ButtonTextSource.Value, 12, 26))
    };

    public override ButtonTargets SupportedTargets => ButtonTargets.All;

    public TimeSpan UpdateInterval => TimeSpan.FromSeconds(30);

    public AdjustmentValue? GetValue(CommandContext ctx)
    {
        var selector = Parameter(ctx, 0);
        if (selector is null) return new AdjustmentValue(double.NaN, ctx.Host.Tr("Set workspace"));
        var match = Plugin.Workspaces.Select(selector).FirstOrDefault();
        if (match?.Identity is null && Plugin.Workspaces.Any) _ = Plugin.Workspaces.IdentifyAsync();
        return new AdjustmentValue(double.NaN, match?.Name ?? selector);
    }

    public override Task Execute(CommandContext ctx) => GuardAsync(ctx, "go to workspace", async () =>
    {
        var selector = Parameter(ctx, 0);
        if (selector is null)
        {
            Hint(ctx, "Set workspace");
            return;
        }

        var teamId = DeepLink.IsTeamId(selector) ? selector : Plugin.Workspaces.Select(selector).FirstOrDefault()?.Identity?.TeamId;
        if (teamId is null)
        {
            await Plugin.Workspaces.IdentifyAsync().ConfigureAwait(false);
            teamId = Plugin.Workspaces.Select(selector).FirstOrDefault()?.Identity?.TeamId;
        }

        if (teamId is null)
        {
            Hint(ctx, "No workspace");
            return;
        }

        if (!SlackApp.OpenLink(ctx.Host, DeepLink.ForWorkspace(teamId))) Hint(ctx, "Slack not found");
    });
}
