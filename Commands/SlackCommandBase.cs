using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Slack.Commands;

/// <summary>Shared plumbing: plugin access, parameter helpers, error guarding and on-key hints.</summary>
internal abstract class SlackCommandBase(SlackPlugin plugin) : IPluginCommand
{
    protected SlackPlugin Plugin { get; } = plugin;

    public abstract CommandDescriptor Descriptor { get; }

    public virtual ButtonTargets SupportedTargets => ButtonTargets.All;

    public abstract Task Execute(CommandContext ctx);

    protected static string? Parameter(CommandContext ctx, int index) =>
        ctx.Parameters.Length > index && !string.IsNullOrWhiteSpace(ctx.Parameters[index]) ? ctx.Parameters[index].Trim() : null;

    protected static int IntParameter(CommandContext ctx, int index, int fallback) =>
        int.TryParse(Parameter(ctx, index), out var value) ? value : fallback;

    /// <summary>Runs <paramref name="action"/> and routes any exception to the host logger instead of the caller.</summary>
    protected static async Task GuardAsync(CommandContext ctx, string what, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ctx.Host.Logger.Error($"Slack {what} failed", ex);
        }
    }

    /// <summary>Short on-key feedback for a touch button; silent for other targets.</summary>
    protected static void Hint(CommandContext ctx, string text)
    {
        if (ctx.Target == ButtonTargets.TouchButton && ctx.SourceIndex is { } slot)
            ctx.Host.OverlayTouchText(slot, ctx.Host.Tr(text), TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Switches every key bound to <paramref name="command"/> to <paramref name="state"/> when the
    /// key being rendered shows another one, so a key that was reset is put right on its next poll.
    /// </summary>
    protected static void Show(CommandContext ctx, string command, string state)
    {
        if (!string.Equals(ctx.StateName, state, StringComparison.OrdinalIgnoreCase))
            ctx.Host.SetActiveButtonState(command, state);
    }
}
