using LoupixDeck.Plugin.Slack.Api;
using LoupixDeck.Plugin.Slack.Commands;
using LoupixDeck.Plugin.Slack.Platform;
using LoupixDeck.Plugin.Slack.Shortcuts;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Slack;

/// <summary>
/// LoupixDeck plugin entry point. Shortcut keys and dials drive the Slack desktop app with its own
/// keyboard shortcuts; the unread key mirrors Slack's Dock badge (macOS); the status, Do Not
/// Disturb and away keys use the Slack Web API with a user token from the user's own Slack app.
/// The two background workers (badge watcher, account poller) run only while their keys are shown.
/// </summary>
public sealed class SlackPlugin : LoupixPlugin, IPluginSettingsPage
{
    private const string TokenKey = "user_token";
    private const string TeamIdKey = "team_id";
    private const string BringToFrontKey = "bring_to_front";

    private const string SupportUrl = "https://web-engineer.co.uk/contact";
    private const string IssuesUrl = "https://github.com/web-engineer/LoupixDeck.Plugin.Slack/issues";

    private static readonly TimeSpan BadgeIdle = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SelfIdle = TimeSpan.FromMinutes(3);

    private readonly Lazy<IReadOnlyList<IPluginCommand>> _commands;
    private IPluginHost _host = null!;

    public SlackPlugin()
    {
        _commands = new Lazy<IReadOnlyList<IPluginCommand>>(BuildCommands, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "slack",
        Name = "Slack",
        Version = new Version(0, 1, 0),
        SdkVersion = SdkInfo.Version,
        Author = "web-engineer",
        Description = "Slack from the deck: shortcut keys and dials, an unread badge, huddle keys, and status, Do Not Disturb and away keys.",
        Icon = LoadEmbeddedIcon("LoupixDeck.Plugin.Slack.icon.png")
    };

    // ---- state the commands read ----

    internal KeySender Keys { get; private set; } = null!;
    internal BadgeMonitor Badges { get; private set; } = null!;
    internal DemandGate BadgeGate { get; private set; } = null!;
    internal SlackApi Api { get; private set; } = null!;
    internal SelfMonitor Self { get; private set; } = null!;
    internal DemandGate SelfGate { get; private set; } = null!;

    internal string? TeamId => _host.Settings.Get(TeamIdKey, string.Empty) is { Length: > 0 } id ? id.Trim() : null;

    private bool BringToFront => _host.Settings.Get(BringToFrontKey, true);

    // ---- lifecycle ----

    public override void Initialize(IPluginHost host)
    {
        _host = host;
        Keys = new KeySender(() => BringToFront);

        Badges = new BadgeMonitor();
        Badges.Changed += () => host.RequestButtonRefresh(UnreadCommand.Name);
        BadgeGate = new DemandGate(OperatingSystem.IsMacOS, Badges.Start, Badges.Stop, BadgeIdle);

        Api = new SlackApi(() => host.Settings.Get<string>(TokenKey));
        Self = new SelfMonitor(Api, host.Logger);
        Self.Changed += RefreshAccountKeys;
        SelfGate = new DemandGate(() => Api.HasToken, Self.Start, Self.Stop, SelfIdle);

        host.Logger.Info(SlackApp.IsInstalled
            ? "Slack desktop app found"
            : "Slack desktop app not found; shortcut keys need it");
    }

    public override void Shutdown()
    {
        BadgeGate?.Dispose();
        SelfGate?.Dispose();
        Badges?.Dispose();
        Self?.Dispose();
        Api?.Dispose();
    }

    public override IEnumerable<IPluginCommand> GetCommands() => _commands.Value;

    private IReadOnlyList<IPluginCommand> BuildCommands() =>
    [
        new OpenCommand(this),
        new ConversationCommand(this),
        new UnreadCommand(this),
        .. ShortcutCommand.All(this),
        .. DialCommand.All(this),
        new StatusCommand(this),
        new ClearStatusCommand(this),
        new FocusCommand(this),
        new AwayCommand(this)
    ];

    public override IReadOnlyList<CommandGroupDescriptor> GetCommandGroups() =>
    [
        new CommandGroupDescriptor
        {
            Group = ShortcutCatalog.Navigate,
            Description = "Slack: open conversations, move around, unread badge, view and dials",
            Icon = "\U000F0424" // mdi-pound-box
        },
        new CommandGroupDescriptor
        {
            Group = ShortcutCatalog.Messages,
            Description = "Slack: mark read, write and format messages",
            Icon = "\U000F0369" // mdi-message-text
        },
        new CommandGroupDescriptor
        {
            Group = ShortcutCatalog.Huddles,
            Description = "Slack: start, leave and mute huddles",
            Icon = "\U000F02CB" // mdi-headphones
        },
        new CommandGroupDescriptor
        {
            Group = AccountCommandBase.Group,
            Description = "Slack: status presets, pause notifications and away (needs a Slack token)",
            Icon = "\U000F0FFB" // mdi-account-edit-outline
        }
    ];

    private void RefreshAccountKeys()
    {
        foreach (var name in new[] { StatusCommand.Name, ClearStatusCommand.Name, FocusCommand.Name, AwayCommand.Name })
            _host.RequestButtonRefresh(name);
    }

    // ---- IPluginSettingsPage ----

    public IReadOnlyList<PluginSettingDescriptor> SettingsSchema =>
    [
        new PluginSettingDescriptor
        {
            Key = "keys",
            Label = "Shortcut keys",
            Kind = PluginSettingKind.Heading,
            Description = "Shortcut keys and dials press Slack's own keyboard shortcuts. On macOS they need the Accessibility permission LoupixDeck already asks for."
        },
        new PluginSettingDescriptor
        {
            Key = BringToFrontKey,
            Label = "Bring Slack to the front first",
            Kind = PluginSettingKind.Toggle,
            DefaultValue = true,
            Description = "Off: shortcuts go to whichever app has focus, so they only work while Slack is in front."
        },
        new PluginSettingDescriptor
        {
            Key = "account",
            Label = "Status, notifications and away",
            Kind = PluginSettingKind.Heading,
            Description = "These keys talk to Slack with a token from your own Slack app. Press \"Create the Slack app\", install it to your workspace, " +
                          "then copy the User OAuth Token (xoxp-…) from OAuth & Permissions into the field below. The keys ask Slack once a minute, only while one is on screen."
        },
        new PluginSettingDescriptor
        {
            Key = TokenKey,
            Label = "User OAuth token",
            Kind = PluginSettingKind.Password,
            DefaultValue = string.Empty,
            Description = "Starts with xoxp-. Stored in this plugin's settings file on this computer."
        },
        new PluginSettingDescriptor
        {
            Key = TeamIdKey,
            Label = "Workspace id (optional)",
            Kind = PluginSettingKind.Text,
            DefaultValue = string.Empty,
            Description = "Used by Open conversation keys given a bare channel id. \"Test token\" fills it in."
        },
        new PluginSettingDescriptor
        {
            Key = "about",
            Label = "About and support",
            Kind = PluginSettingKind.Heading,
            Description = "Made by web-engineer. Not affiliated with or endorsed by Slack Technologies. For help, our public Slack and our tracker are on the contact page."
        }
    ];

    public IReadOnlyList<PluginSettingAction> SettingsActions =>
    [
        new PluginSettingAction { Label = "Create the Slack app", Invoke = () => OpenLink(SlackApi.CreateAppUrl(), "Slack's app page") },
        new PluginSettingAction { Label = "Test token", Invoke = TestTokenAsync },
        new PluginSettingAction { Label = "Get support", Invoke = () => OpenLink(SupportUrl, SupportUrl) },
        new PluginSettingAction { Label = "Report a bug or request a feature", Invoke = () => OpenLink(IssuesUrl, IssuesUrl) }
    ];

    private Task<string> OpenLink(string url, string name) =>
        Task.FromResult(_host.OpenBrowser(url) ? $"Opened {name}" : $"Could not open a browser; visit {url}");

    public void OnSettingsSaved()
    {
        // The poller restarts from the next key render with the new token.
        SelfGate.Reset();
        Keys.Invalidate();
        RefreshAccountKeys();
    }

    private async Task<string> TestTokenAsync()
    {
        if (!Api.HasToken) return "No token set. Press \"Create the Slack app\" first, then paste its User OAuth Token.";

        Identity id;
        try
        {
            id = await Api.AuthTestAsync().ConfigureAwait(false);
        }
        catch (SlackApiException ex)
        {
            return ex.Error is "invalid_auth" or "not_authed" or "token_revoked"
                ? $"Slack rejected the token ({ex.Error}). Copy the User OAuth Token again."
                : $"Slack said: {ex.Error}";
        }
        catch (HttpRequestException ex)
        {
            return $"Could not reach Slack: {ex.Message}";
        }

        if (TeamId != id.TeamId)
        {
            _host.Settings.Set(TeamIdKey, id.TeamId);
            _host.Settings.Save();
        }

        var missing = SlackApi.Scopes.Where(s => !id.Scopes.Contains(s)).ToList();
        var scopes = missing.Count == 0
            ? "All scopes granted."
            : $"Missing scopes: {string.Join(", ", missing)}. Add them under OAuth & Permissions and reinstall the app.";
        return $"Signed in as {id.User} on {id.Team} ({id.TeamId}). {scopes}";
    }

    private static byte[]? LoadEmbeddedIcon(string name)
    {
        try
        {
            using var stream = typeof(SlackPlugin).Assembly.GetManifestResourceStream(name);
            if (stream is null) return null;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
