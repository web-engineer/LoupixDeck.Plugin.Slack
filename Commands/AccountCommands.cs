using LoupixDeck.Plugin.Slack.Api;
using LoupixDeck.Plugin.Slack.Rendering;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Slack.Commands;

/// <summary>
/// Keys that read and change your own Slack account through the Web API: status, Do Not Disturb and
/// away. They need a user token (see the settings page); without one they show "Set token".
/// </summary>
internal abstract class AccountCommandBase(SlackPlugin plugin) : SlackCommandBase(plugin), IValueDisplayCommand
{
    public const string Group = "Slack Status";

    public override ButtonTargets SupportedTargets => ButtonTargets.TouchButton;

    /// <summary>Keeps the poller alive; updates arrive by push from it.</summary>
    public TimeSpan UpdateInterval => TimeSpan.FromSeconds(5);

    public abstract AdjustmentValue? GetValue(CommandContext ctx);

    /// <summary>The cached account, or null with the value text explaining why there is none yet.</summary>
    protected SlackSelf? Self(CommandContext ctx, out AdjustmentValue? problem)
    {
        problem = null;
        if (!Plugin.Api.HasToken)
        {
            problem = new AdjustmentValue(double.NaN, ctx.Host.Tr("Set token"));
            return null;
        }

        Plugin.SelfGate.Touch();
        if (Plugin.Self.Current is { } self) return self;
        problem = new AdjustmentValue(double.NaN, Plugin.Self.Error is { } e ? Short(e) : "…");
        return null;
    }

    /// <summary>Runs a change against Slack, then re-reads the account so every key catches up.</summary>
    protected async Task ChangeAsync(CommandContext ctx, string what, Func<SlackApi, Task> change)
    {
        if (!Plugin.Api.HasToken)
        {
            Hint(ctx, "Set token");
            return;
        }

        try
        {
            await change(Plugin.Api).ConfigureAwait(false);
        }
        catch (SlackApiException ex)
        {
            ctx.Host.Logger.Warn($"Slack {what} failed: {ex.Message}");
            Hint(ctx, Short(ex.Error));
            return;
        }

        await Plugin.Self.RefreshAsync().ConfigureAwait(false);
    }

    /// <summary>Slack error codes worth naming on a key; anything else is just "Failed".</summary>
    protected static string Short(string error) => error switch
    {
        "missing_scope" => "Missing scope",
        "invalid_auth" or "not_authed" or "token_revoked" or "account_inactive" => "Bad token",
        "network" or "timeout" => "Offline",
        _ when error.StartsWith("ratelimited", StringComparison.Ordinal) => "Rate limited",
        "no_token" => "Set token",
        _ => "Failed"
    };

    protected static string Remaining(DateTimeOffset? until)
    {
        if (until is not { } end) return string.Empty;
        var left = end - DateTimeOffset.UtcNow;
        if (left <= TimeSpan.Zero) return string.Empty;
        if (left.TotalHours >= 24) return $"{(int)left.TotalDays}d";
        if (left.TotalMinutes >= 60) return $"{(int)left.TotalHours}h{left.Minutes:00}";
        return $"{Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))}m";
    }
}

/// <summary>
/// A status preset: tap to set the status text and emoji (optionally for a number of minutes), tap
/// again while it is your status to clear it. A ring round the key fills while that status is the
/// one you have. (A ring rather than button states: states are shared by every key with the same
/// command, and each preset key must light on its own.)
/// </summary>
internal sealed class StatusCommand(SlackPlugin plugin) : AccountCommandBase(plugin)
{
    public const string Name = "Slack.Status";

    public override CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = Name,
        DisplayName = "Status preset",
        Group = Group,
        Icon = "\U000F0FFB", // mdi-account-edit-outline
        Description = "Set your status, e.g. In a meeting, :calendar:, 60. Tap again to clear it. Minutes 0 keeps it until cleared.",
        ParameterTemplate = "({Text},{Emoji},{Minutes})",
        Parameters =
        [
            new CommandParameter("Text", typeof(string)) { DefaultValue = "In a meeting" },
            new CommandParameter("Emoji", typeof(string)) { DefaultValue = ":calendar:" },
            new CommandParameter("Minutes", typeof(int)) { DefaultValue = "60" }
        ],
        ButtonLayout = SlackKey.State(SlackKey.AubergineHex,
            new ButtonLayerDescriptor
            {
                Kind = ButtonLayerKind.Indicator, Name = "Active ring", IconScale = 0.9,
                Color = SlackKey.GreenHex, TrackColor = "#00000000", Thickness = 0.07,
                StartAngle = -90, SweepAngle = 360
            },
            SlackKey.Icon("Icon", "\U000F0FFB", 0.3, -15),
            SlackKey.ValueText("Status", ButtonTextSource.Value, 12, 10),
            SlackKey.ValueText("Time left", ButtonTextSource.Detail, 10, 25, SlackKey.FadedHex))
    };

    public override AdjustmentValue? GetValue(CommandContext ctx)
    {
        var text = Parameter(ctx, 0) ?? string.Empty;
        var self = Self(ctx, out var problem);
        if (self is null) return problem;

        var active = IsThisStatus(ctx, self);
        return new AdjustmentValue(active ? 1 : 0, text.Length > 0 ? text : ctx.Host.Tr("Status"))
        {
            Detail = active ? Remaining(self.StatusExpires) : null
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

        if (Plugin.Self.Current is { } self && IsThisStatus(ctx, self))
            return ChangeAsync(ctx, "clear status", api => api.ClearStatusAsync());

        return ChangeAsync(ctx, "set status", api => api.SetStatusAsync(text, emoji, minutes));
    });
}

/// <summary>Clears your status. Shows the status you have now.</summary>
internal sealed class ClearStatusCommand(SlackPlugin plugin) : AccountCommandBase(plugin)
{
    public const string Name = "Slack.ClearStatus";

    public override CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = Name,
        DisplayName = "Clear status",
        Group = Group,
        Icon = "\U000F0156", // mdi-close
        Description = "Clear your status. The key shows the status you have now.",
        ButtonLayout = SlackKey.State(SlackKey.AubergineHex,
            SlackKey.Icon("Icon", "\U000F0156", 0.36, -14),
            SlackKey.ValueText("Status", ButtonTextSource.Value, 12, 14),
            SlackKey.ValueText("Time left", ButtonTextSource.Detail, 10, 31))
    };

    public override AdjustmentValue? GetValue(CommandContext ctx)
    {
        var self = Self(ctx, out var problem);
        if (self is null) return problem;
        if (self.StatusText.Length == 0 && self.StatusEmoji.Length == 0) return new AdjustmentValue(double.NaN, ctx.Host.Tr("No status"));
        return new AdjustmentValue(double.NaN, self.StatusText.Length > 0 ? self.StatusText : self.StatusEmoji)
        {
            Detail = Remaining(self.StatusExpires)
        };
    }

    public override Task Execute(CommandContext ctx) =>
        GuardAsync(ctx, "clear status", () => ChangeAsync(ctx, "clear status", api => api.ClearStatusAsync()));
}

/// <summary>
/// Do Not Disturb: tap to pause notifications for a set time, tap again to resume them. The key
/// shows the time left, and also lights during your Do Not Disturb schedule.
/// </summary>
internal sealed class FocusCommand(SlackPlugin plugin) : AccountCommandBase(plugin)
{
    public const string Name = "Slack.Focus";
    public const string OnState = "paused";
    public const string ScheduledState = "scheduled";
    public const string OffState = "notifying";

    public override CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = Name,
        DisplayName = "Pause notifications",
        Group = Group,
        Icon = "\U000F009B", // mdi-bell-off
        Description = "Turn on Do Not Disturb for the given minutes; tap again to resume notifications. Shows the time left.",
        ParameterTemplate = "({Minutes})",
        Parameters = [new CommandParameter("Minutes", typeof(int)) { DefaultValue = "60" }],
        States =
        [
            new ButtonStateDescriptor
            {
                Name = OnState, Description = "Notifications are paused",
                Layout = SlackKey.State(SlackKey.YellowHex,
                    SlackKey.Icon("Icon", "\U000F009B", 0.4, -10, SlackKey.DarkHex),
                    SlackKey.ValueText("Time left", ButtonTextSource.Value, 13, 26, SlackKey.DarkHex))
            },
            new ButtonStateDescriptor
            {
                Name = ScheduledState, Description = "Paused by your Do Not Disturb schedule",
                Layout = SlackKey.State(SlackKey.AubergineHex,
                    SlackKey.Icon("Icon", "\U000F009B", 0.4, -10, SlackKey.YellowHex),
                    SlackKey.Caption("Scheduled"))
            },
            new ButtonStateDescriptor
            {
                Name = OffState, Description = "Notifications are on",
                Layout = SlackKey.State(SlackKey.AubergineHex,
                    SlackKey.Icon("Icon", "\U000F009A", 0.4, -10), // mdi-bell
                    SlackKey.ValueText("Status", ButtonTextSource.Value, 13, 26))
            }
        ],
        ButtonLayout = new ButtonLayoutDescriptor { Mode = ButtonLayoutMode.None }
    };

    public override AdjustmentValue? GetValue(CommandContext ctx)
    {
        var self = Self(ctx, out var problem);
        if (self is null)
        {
            Show(ctx, Name, OffState);
            return problem;
        }

        if (self.Snoozed)
        {
            Show(ctx, Name, OnState);
            return new AdjustmentValue(double.NaN, Remaining(self.SnoozeEnds));
        }

        if (self.ScheduledDnd)
        {
            Show(ctx, Name, ScheduledState);
            return null;
        }

        Show(ctx, Name, OffState);
        return new AdjustmentValue(double.NaN, $"{IntParameter(ctx, 0, 60)} min");
    }

    public override Task Execute(CommandContext ctx) => GuardAsync(ctx, "do not disturb", () =>
    {
        var snoozed = Plugin.Self.Current?.Snoozed == true;
        return snoozed
            ? ChangeAsync(ctx, "end snooze", api => api.EndSnoozeAsync())
            : ChangeAsync(ctx, "snooze", api => api.SnoozeAsync(IntParameter(ctx, 0, 60)));
    });
}

/// <summary>Away: tap to set yourself away, tap again to go back to automatic (active while you use Slack).</summary>
internal sealed class AwayCommand(SlackPlugin plugin) : AccountCommandBase(plugin)
{
    public const string Name = "Slack.Away";
    public const string AwayState = "away";
    public const string ActiveState = "active";

    public override CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = Name,
        DisplayName = "Away",
        Group = Group,
        Icon = "\U000F0004", // mdi-account
        Description = "Set yourself away; tap again to be active.",
        States =
        [
            new ButtonStateDescriptor
            {
                Name = AwayState, Description = "You are set to away",
                Layout = SlackKey.State(SlackKey.DarkHex,
                    SlackKey.Icon("Icon", "\U000F0766", 0.3, -10, SlackKey.FadedHex), // mdi-circle-outline
                    SlackKey.Caption("Away"))
            },
            new ButtonStateDescriptor
            {
                Name = ActiveState, Description = "Slack shows you active when you are",
                Layout = SlackKey.State(SlackKey.AubergineHex,
                    SlackKey.Icon("Icon", "\U000F0765", 0.4, -10, SlackKey.GreenHex), // mdi-circle-medium
                    SlackKey.ValueText("Status", ButtonTextSource.Value, 13, 26))
            }
        ],
        ButtonLayout = new ButtonLayoutDescriptor { Mode = ButtonLayoutMode.None }
    };

    public override AdjustmentValue? GetValue(CommandContext ctx)
    {
        var self = Self(ctx, out var problem);
        if (self is null)
        {
            Show(ctx, Name, ActiveState);
            return problem;
        }

        Show(ctx, Name, self.ManualAway ? AwayState : ActiveState);
        return self.ManualAway ? null : new AdjustmentValue(double.NaN, ctx.Host.Tr("Active"));
    }

    public override Task Execute(CommandContext ctx) => GuardAsync(ctx, "presence", () =>
    {
        var away = Plugin.Self.Current?.ManualAway == true;
        return ChangeAsync(ctx, "set presence", api => api.SetPresenceAsync(!away));
    });
}
