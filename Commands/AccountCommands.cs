using LoupixDeck.Plugin.Slack.Api;
using LoupixDeck.Plugin.Slack.Rendering;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Slack.Commands;

/// <summary>
/// Keys that read and change your own Slack account through the Web API: status, Do Not Disturb and
/// away. Each takes an optional <c>Workspace</c> parameter: blank for every connected workspace, or
/// one (or several, separated by <c>;</c>) by name, subdomain, team id or settings slot. Without any
/// token they show "Set token".
/// </summary>
internal abstract class AccountCommandBase(SlackPlugin plugin) : SlackCommandBase(plugin), IValueDisplayCommand
{
    public const string Group = "Slack Status";
    public const string WorkspaceParameter = "Workspace";

    public override ButtonTargets SupportedTargets => ButtonTargets.TouchButton;

    /// <summary>Keeps the poller alive; updates arrive by push from it.</summary>
    public TimeSpan UpdateInterval => TimeSpan.FromSeconds(5);

    /// <summary>Position of the <c>Workspace</c> parameter in this command's parameter list.</summary>
    protected abstract int WorkspaceIndex { get; }

    public abstract AdjustmentValue? GetValue(CommandContext ctx);

    protected static CommandParameter WorkspaceParam() => new(WorkspaceParameter, typeof(string)) { DefaultValue = "all" };

    protected IReadOnlyList<Workspace> Targets(CommandContext ctx) => Plugin.Workspaces.Select(Parameter(ctx, WorkspaceIndex));

    /// <summary>
    /// The selected workspaces whose state is known, or null with the value text explaining why there
    /// is nothing to show yet.
    /// </summary>
    protected IReadOnlyList<(Workspace W, SlackSelf S)>? Known(CommandContext ctx, out AdjustmentValue? problem)
    {
        problem = null;
        var set = Plugin.Workspaces;
        if (!set.Any)
        {
            problem = new AdjustmentValue(double.NaN, ctx.Host.Tr("Set token"));
            return null;
        }

        Plugin.WorkspaceGate.Touch();
        var targets = Targets(ctx);
        if (targets.Count == 0)
        {
            problem = new AdjustmentValue(double.NaN, set.Resolving ? "…" : ctx.Host.Tr("No workspace"));
            return null;
        }

        var known = targets.Where(w => w.Self is not null).Select(w => (w, w.Self!)).ToList();
        if (known.Count > 0) return known;

        problem = new AdjustmentValue(double.NaN, targets.Select(w => w.Error).FirstOrDefault(e => e is not null) is { } e ? Short(e) : "…");
        return null;
    }

    /// <summary>"3/6" when only some of the workspaces are in the state, else null.</summary>
    protected static string? Share(int count, int total) => total > 1 && count > 0 && count < total ? $"{count}/{total}" : null;

    /// <summary>
    /// Runs a change in every selected workspace at once, then re-reads them so every key catches up.
    /// A touch button briefly shows what failed: Slack's reason when all failed, "2/6 failed" otherwise.
    /// </summary>
    protected async Task ChangeAsync(CommandContext ctx, string what, Func<SlackApi, Task> change)
    {
        if (!Plugin.Workspaces.Any)
        {
            Hint(ctx, "Set token");
            return;
        }

        var targets = Targets(ctx);
        if (targets.Count == 0)
        {
            Hint(ctx, "No workspace");
            return;
        }

        var failures = await Task.WhenAll(targets.Select(async w =>
        {
            try
            {
                await change(w.Api).ConfigureAwait(false);
                return null;
            }
            catch (SlackApiException ex)
            {
                ctx.Host.Logger.Warn($"Slack {what} in {w.Name} failed: {ex.Message}");
                return ex.Error;
            }
            catch (HttpRequestException ex)
            {
                ctx.Host.Logger.Warn($"Slack {what} in {w.Name} failed: {ex.Message}");
                return "network";
            }
        })).ConfigureAwait(false);

        var failed = failures.Where(f => f is not null).ToList();
        if (failed.Count == targets.Count) Hint(ctx, Short(failed[0]!));
        else if (failed.Count > 0) Hint(ctx, $"{failed.Count}/{targets.Count} failed");

        await Plugin.Workspaces.RefreshAsync(targets).ConfigureAwait(false);
    }

    /// <summary>Slack error codes worth naming on a key; anything else is just "Failed".</summary>
    internal static string Short(string error) => error switch
    {
        "missing_scope" => "Missing scope",
        "invalid_auth" or "not_authed" or "token_revoked" or "account_inactive" => "Bad token",
        "network" or "timeout" => "Offline",
        _ when error.StartsWith("ratelimited", StringComparison.Ordinal) => "Rate limited",
        "no_token" => "Set token",
        _ => "Failed"
    };

    internal static string Remaining(DateTimeOffset? until)
    {
        if (until is not { } end) return string.Empty;
        var left = end - DateTimeOffset.UtcNow;
        if (left <= TimeSpan.Zero) return string.Empty;
        if (left.TotalHours >= 24) return $"{(int)left.TotalDays}d";
        if (left.TotalMinutes >= 60) return $"{(int)left.TotalHours}h{left.Minutes:00}";
        return $"{Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))}m";
    }

    /// <summary>The soonest of several end times, ignoring missing ones.</summary>
    protected static DateTimeOffset? Soonest(IEnumerable<DateTimeOffset?> times) =>
        times.Where(t => t is not null).Select(t => t!.Value).DefaultIfEmpty().Min() is var min && min != default ? min : null;
}

/// <summary>
/// A status preset: tap to set the status text and emoji (optionally for a number of minutes) in the
/// selected workspaces, tap again while every one of them has it to clear it. A ring round the key
/// fills with the share of workspaces that have this status. (A ring rather than button states:
/// states are shared by every key with the same command, and each preset key must light on its own.)
/// </summary>
internal sealed class StatusCommand(SlackPlugin plugin) : AccountCommandBase(plugin)
{
    public const string Name = "Slack.Status";

    protected override int WorkspaceIndex => 3;

    public override CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = Name,
        DisplayName = "Status preset",
        Group = Group,
        Icon = "\U000F0FFB", // mdi-account-edit-outline
        Description = "Set your status in all workspaces or chosen ones, e.g. In a meeting, :calendar:, 60, all. Tap again to clear it. Minutes 0 keeps it until cleared.",
        ParameterTemplate = "({Text},{Emoji},{Minutes},{Workspace})",
        Parameters =
        [
            new CommandParameter("Text", typeof(string)) { DefaultValue = "In a meeting" },
            new CommandParameter("Emoji", typeof(string)) { DefaultValue = ":calendar:" },
            new CommandParameter("Minutes", typeof(int)) { DefaultValue = "60" },
            WorkspaceParam()
        ],
        ButtonLayout = SlackKey.State(SlackKey.AubergineHex,
            new ButtonLayerDescriptor
            {
                Kind = ButtonLayerKind.Indicator, Name = "Active ring", IconScale = 0.9,
                Color = SlackKey.GreenHex, TrackColor = "#00000000", Thickness = 0.07,
                StartAngle = -90, SweepAngle = 360
            },
            SlackKey.Icon("Icon", "\U000F0FFB", 0.3, -15), // mdi-account-edit-outline
            SlackKey.ValueText("Status", ButtonTextSource.Value, 12, 10),
            SlackKey.ValueText("Time left", ButtonTextSource.Detail, 10, 25, SlackKey.FadedHex))
    };

    public override AdjustmentValue? GetValue(CommandContext ctx)
    {
        var text = Parameter(ctx, 0) ?? string.Empty;
        var known = Known(ctx, out var problem);
        if (known is null) return problem;

        var withIt = known.Where(k => IsThisStatus(ctx, k.S)).ToList();
        return new AdjustmentValue((double)withIt.Count / known.Count, text.Length > 0 ? text : ctx.Host.Tr("Status"))
        {
            Detail = Share(withIt.Count, known.Count) ?? (withIt.Count > 0 ? Remaining(Soonest(withIt.Select(k => k.S.StatusExpires))) : null)
        };
    }

    private static bool IsThisStatus(CommandContext ctx, SlackSelf self) =>
        string.Equals(self.StatusText, Parameter(ctx, 0) ?? string.Empty, StringComparison.Ordinal) &&
        string.Equals(self.StatusEmoji, Parameter(ctx, 1) ?? string.Empty, StringComparison.Ordinal);

    public override Task Execute(CommandContext ctx) => GuardAsync(ctx, "status", () =>
    {
        var text = Parameter(ctx, 0) ?? string.Empty;
        var emoji = Parameter(ctx, 1) ?? string.Empty;
        var minutes = IntParameter(ctx, 2, 0);
        if (text.Length == 0 && emoji.Length == 0)
        {
            Hint(ctx, "Set status");
            return Task.CompletedTask;
        }

        // Clear only when every selected workspace already has it; otherwise bring them all in line.
        var targets = Targets(ctx);
        var everywhere = targets.Count > 0 && targets.All(w => w.Self is { } s && IsThisStatus(ctx, s));
        return everywhere
            ? ChangeAsync(ctx, "clear status", api => api.ClearStatusAsync())
            : ChangeAsync(ctx, "set status", api => api.SetStatusAsync(text, emoji, minutes));
    });
}

/// <summary>Clears your status in the selected workspaces. Shows the status you have now.</summary>
internal sealed class ClearStatusCommand(SlackPlugin plugin) : AccountCommandBase(plugin)
{
    public const string Name = "Slack.ClearStatus";

    protected override int WorkspaceIndex => 0;

    public override CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = Name,
        DisplayName = "Clear status",
        Group = Group,
        Icon = "\U000F0156", // mdi-close
        Description = "Clear your status in all workspaces or chosen ones. The key shows the status you have now.",
        ParameterTemplate = "({Workspace})",
        Parameters = [WorkspaceParam()],
        ButtonLayout = SlackKey.State(SlackKey.AubergineHex,
            SlackKey.Icon("Icon", "\U000F0156", 0.36, -14), // mdi-close
            SlackKey.ValueText("Status", ButtonTextSource.Value, 12, 14),
            SlackKey.ValueText("Time left", ButtonTextSource.Detail, 10, 31))
    };

    public override AdjustmentValue? GetValue(CommandContext ctx)
    {
        var known = Known(ctx, out var problem);
        if (known is null) return problem;

        var withStatus = known.Where(k => k.S.StatusText.Length > 0 || k.S.StatusEmoji.Length > 0).ToList();
        if (withStatus.Count == 0) return new AdjustmentValue(double.NaN, ctx.Host.Tr("No status"));

        var first = withStatus[0].S;
        var same = withStatus.All(k => k.S.StatusText == first.StatusText && k.S.StatusEmoji == first.StatusEmoji);
        var label = !same ? ctx.Host.Tr("Mixed") : first.StatusText.Length > 0 ? first.StatusText : first.StatusEmoji;
        return new AdjustmentValue(double.NaN, label)
        {
            Detail = Share(withStatus.Count, known.Count) ?? Remaining(Soonest(withStatus.Select(k => k.S.StatusExpires)))
        };
    }

    public override Task Execute(CommandContext ctx) =>
        GuardAsync(ctx, "clear status", () => ChangeAsync(ctx, "clear status", api => api.ClearStatusAsync()));
}

/// <summary>
/// Do Not Disturb: tap to pause notifications for a set time in the selected workspaces, tap again
/// to resume them. Shows the time left, and also lights during your Do Not Disturb schedule.
/// </summary>
internal sealed class FocusCommand(SlackPlugin plugin) : AccountCommandBase(plugin)
{
    public const string Name = "Slack.Focus";
    public const string OnState = "paused";
    public const string ScheduledState = "scheduled";
    public const string OffState = "notifying";

    protected override int WorkspaceIndex => 1;

    public override CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = Name,
        DisplayName = "Pause notifications",
        Group = Group,
        Icon = "\U000F009B", // mdi-bell-off
        Description = "Turn on Do Not Disturb for the given minutes in all workspaces or chosen ones; tap again to resume notifications. Shows the time left.",
        ParameterTemplate = "({Minutes},{Workspace})",
        Parameters = [new CommandParameter("Minutes", typeof(int)) { DefaultValue = "60" }, WorkspaceParam()],
        States =
        [
            new ButtonStateDescriptor
            {
                Name = OffState, Description = "Notifications are on",
                Layout = SlackKey.State(SlackKey.AubergineHex,
                    SlackKey.Icon("Icon", "\U000F009A", 0.4, -10), // mdi-bell
                    SlackKey.ValueText("Status", ButtonTextSource.Value, 13, 26))
            },
            new ButtonStateDescriptor
            {
                Name = OnState, Description = "Notifications are paused",
                Layout = SlackKey.State(SlackKey.YellowHex,
                    SlackKey.Icon("Icon", "\U000F009B", 0.4, -10, SlackKey.DarkHex), // mdi-bell-off
                    SlackKey.ValueText("Time left", ButtonTextSource.Value, 13, 22, SlackKey.DarkHex),
                    SlackKey.ValueText("Workspaces", ButtonTextSource.Detail, 9, 36, SlackKey.DarkHex))
            },
            new ButtonStateDescriptor
            {
                Name = ScheduledState, Description = "Paused by your Do Not Disturb schedule",
                Layout = SlackKey.State(SlackKey.AubergineHex,
                    SlackKey.Icon("Icon", "\U000F009B", 0.4, -10, SlackKey.YellowHex), // mdi-bell-off
                    SlackKey.Caption("Scheduled"))
            }
        ],
        ButtonLayout = new ButtonLayoutDescriptor { Mode = ButtonLayoutMode.None }
    };

    public override AdjustmentValue? GetValue(CommandContext ctx)
    {
        var known = Known(ctx, out var problem);
        if (known is null)
        {
            Show(ctx, Name, OffState);
            return problem;
        }

        var snoozed = known.Where(k => k.S.Snoozed).ToList();
        if (snoozed.Count > 0)
        {
            Show(ctx, Name, OnState);
            return new AdjustmentValue(double.NaN, Remaining(Soonest(snoozed.Select(k => k.S.SnoozeEnds))))
            {
                Detail = Share(snoozed.Count, known.Count)
            };
        }

        if (known.All(k => k.S.ScheduledDnd))
        {
            Show(ctx, Name, ScheduledState);
            return null;
        }

        Show(ctx, Name, OffState);
        return new AdjustmentValue(double.NaN, $"{IntParameter(ctx, 0, 60)} min");
    }

    public override Task Execute(CommandContext ctx) => GuardAsync(ctx, "do not disturb", () =>
    {
        // Resume when anything selected is paused, so one tap always ends a pause the key shows.
        var anySnoozed = Targets(ctx).Any(w => w.Self?.Snoozed == true);
        return anySnoozed
            ? ChangeAsync(ctx, "end snooze", EndSnoozeIfRunning)
            : ChangeAsync(ctx, "snooze", api => api.SnoozeAsync(IntParameter(ctx, 0, 60)));
    });

    /// <summary>Slack answers <c>snooze_not_active</c> for a workspace that was not paused; that is not a failure here.</summary>
    private static async Task EndSnoozeIfRunning(SlackApi api)
    {
        try
        {
            await api.EndSnoozeAsync().ConfigureAwait(false);
        }
        catch (SlackApiException ex) when (ex.Error == "snooze_not_active")
        {
        }
    }
}

/// <summary>Away: tap to set yourself away in the selected workspaces, tap again to go back to automatic.</summary>
internal sealed class AwayCommand(SlackPlugin plugin) : AccountCommandBase(plugin)
{
    public const string Name = "Slack.Away";
    public const string AwayState = "away";
    public const string ActiveState = "active";

    protected override int WorkspaceIndex => 0;

    public override CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = Name,
        DisplayName = "Away",
        Group = Group,
        Icon = "\U000F0004", // mdi-account
        Description = "Set yourself away in all workspaces or chosen ones; tap again to be active.",
        ParameterTemplate = "({Workspace})",
        Parameters = [WorkspaceParam()],
        States =
        [
            new ButtonStateDescriptor
            {
                Name = ActiveState, Description = "Slack shows you active when you are",
                Layout = SlackKey.State(SlackKey.AubergineHex,
                    SlackKey.Icon("Icon", "\U000F09DE", 0.4, -10, SlackKey.GreenHex), // mdi-circle-medium
                    SlackKey.ValueText("Status", ButtonTextSource.Value, 13, 26))
            },
            new ButtonStateDescriptor
            {
                Name = AwayState, Description = "You are set to away",
                Layout = SlackKey.State(SlackKey.DarkHex,
                    SlackKey.Icon("Icon", "\U000F0766", 0.3, -10, SlackKey.FadedHex), // mdi-circle-outline
                    SlackKey.Caption("Away"),
                    SlackKey.ValueText("Workspaces", ButtonTextSource.Detail, 9, 38, SlackKey.FadedHex))
            }
        ],
        ButtonLayout = new ButtonLayoutDescriptor { Mode = ButtonLayoutMode.None }
    };

    public override AdjustmentValue? GetValue(CommandContext ctx)
    {
        var known = Known(ctx, out var problem);
        if (known is null)
        {
            Show(ctx, Name, ActiveState);
            return problem;
        }

        var away = known.Count(k => k.S.ManualAway);
        if (away > 0)
        {
            Show(ctx, Name, AwayState);
            return new AdjustmentValue(double.NaN, null) { Detail = Share(away, known.Count) };
        }

        Show(ctx, Name, ActiveState);
        return new AdjustmentValue(double.NaN, ctx.Host.Tr("Active"));
    }

    public override Task Execute(CommandContext ctx) => GuardAsync(ctx, "presence", () =>
    {
        // Back to active when anything selected is away; away everywhere otherwise.
        var anyAway = Targets(ctx).Any(w => w.Self?.ManualAway == true);
        return ChangeAsync(ctx, "set presence", api => api.SetPresenceAsync(away: !anyAway));
    });
}
