using LoupixDeck.Plugin.Slack.Api;
using LoupixDeck.Plugin.Slack.Commands;
using LoupixDeck.Plugin.Slack.Platform;
using LoupixDeck.Plugin.Slack.Shortcuts;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Slack;

/// <summary>
/// LoupixDeck plugin entry point. Shortcut keys and dials drive the Slack desktop app with its own
/// keyboard shortcuts; the unread key mirrors Slack's Dock badge (macOS); the status, Do Not
/// Disturb and away keys use the Slack Web API with one user token per connected workspace.
/// The two background workers (badge watcher, workspace poller) run only while their keys are shown.
/// </summary>
public sealed class SlackPlugin : LoupixPlugin, IPluginSettingsPage
{
    private const string TokenKeyPrefix = "token_";
    private const string LegacyTokenKey = "user_token";
    private const string BringToFrontKey = "bring_to_front";

    private const string SupportUrl = "https://web-engineer.co.uk/contact";
    private const string IssuesUrl = "https://github.com/web-engineer/LoupixDeck.Plugin.Slack/issues";

    private static readonly TimeSpan BadgeIdle = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WorkspaceIdle = TimeSpan.FromMinutes(3);

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
        Description = "Slack from the deck: shortcut keys and dials, an unread badge, huddle keys, and status, Do Not Disturb and away keys across all your workspaces.",
        Icon = LoadEmbeddedIcon("LoupixDeck.Plugin.Slack.icon.png")
    };

    /// <summary>Test hook: the HTTP handler each workspace's API client uses, by token.</summary>
    internal Func<string, HttpMessageHandler?>? HandlerFactory { get; set; }

    // ---- state the commands read ----

    internal KeySender Keys { get; private set; } = null!;
    internal BadgeMonitor Badges { get; private set; } = null!;
    internal DemandGate BadgeGate { get; private set; } = null!;
    internal WorkspaceSet Workspaces { get; private set; } = null!;
    internal DemandGate WorkspaceGate { get; private set; } = null!;

    private bool BringToFront => _host.Settings.Get(BringToFrontKey, true);

    // ---- lifecycle ----

    public override void Initialize(IPluginHost host)
    {
        _host = host;
        Keys = new KeySender(() => BringToFront);

        Badges = new BadgeMonitor();
        Badges.Changed += () => host.RequestButtonRefresh(UnreadCommand.Name);
        BadgeGate = new DemandGate(OperatingSystem.IsMacOS, Badges.Start, Badges.Stop, BadgeIdle);

        Workspaces = CreateWorkspaces();
        // Through the property, so the gate drives whichever set is current after a settings save.
        WorkspaceGate = new DemandGate(() => Workspaces.Any, () => Workspaces.Start(), () => Workspaces.Stop(), WorkspaceIdle);

        host.Logger.Info((SlackApp.IsInstalled ? "Slack desktop app found" : "Slack desktop app not found; shortcut keys need it") +
                         $"; {Workspaces.All.Count} workspace token(s)");
    }

    public override void Shutdown()
    {
        BadgeGate?.Dispose();
        WorkspaceGate?.Dispose();
        Badges?.Dispose();
        Workspaces?.Dispose();
    }

    private WorkspaceSet CreateWorkspaces()
    {
        var tokens = Enumerable.Range(1, WorkspaceSet.Slots).Select(i => _host.Settings.Get(TokenKeyPrefix + i, string.Empty)).ToArray();
        // Builds before multi-workspace support kept one token under user_token; it becomes slot 1.
        if (string.IsNullOrWhiteSpace(tokens[0])) tokens[0] = _host.Settings.Get(LegacyTokenKey, string.Empty);

        var set = new WorkspaceSet(tokens, _host.Logger, HandlerFactory);
        set.Changed += RefreshAccountKeys;
        return set;
    }

    public override IEnumerable<IPluginCommand> GetCommands() => _commands.Value;

    private IReadOnlyList<IPluginCommand> BuildCommands() =>
    [
        new OpenCommand(this),
        new WorkspaceCommand(this),
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
            Description = "Slack: workspaces, conversations, moving around, unread badge, view and dials",
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
            Description = "Slack: status presets, pause notifications and away, in every workspace at once or chosen ones (needs Slack tokens)",
            Icon = "\U000F0FFB" // mdi-account-edit-outline
        }
    ];

    private void RefreshAccountKeys()
    {
        foreach (var name in new[] { StatusCommand.Name, ClearStatusCommand.Name, FocusCommand.Name, AwayCommand.Name, WorkspaceCommand.Name })
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
            Label = "Workspaces: status, notifications and away",
            Kind = PluginSettingKind.Heading,
            Description = "These keys need one token per Slack workspace, from a small Slack app of your own in that workspace. Repeat for each workspace: " +
                          "1. Press \"Copy app manifest and open Slack\". " +
                          "2. In Slack, press Create New App, choose From a manifest, Continue, and pick the workspace. " +
                          "3. On the JSON tab, delete what is there and paste (Cmd+V / Ctrl+V), then Next and Create. " +
                          "4. Open Install App in the app's sidebar, press Install to the workspace, then Allow. " +
                          "5. Copy the User OAuth Token (starts xoxp-) into the next free slot below and Save. " +
                          "Then press \"Test tokens\" to see which workspace each slot is."
        },
        .. Enumerable.Range(1, WorkspaceSet.Slots).Select(TokenSetting),
        new PluginSettingDescriptor
        {
            Key = "about",
            Label = "About and support",
            Kind = PluginSettingKind.Heading,
            Description = "Made by web-engineer. Not affiliated with or endorsed by Slack Technologies. For help, our public Slack and our tracker are on the contact page."
        }
    ];

    private PluginSettingDescriptor TokenSetting(int slot)
    {
        var known = Workspaces?.All.FirstOrDefault(w => w.Slot == slot)?.Identity;
        return new PluginSettingDescriptor
        {
            Key = TokenKeyPrefix + slot,
            Label = known is null ? $"Workspace {slot} token" : $"Workspace {slot} token ({known.Team})",
            Kind = PluginSettingKind.Password,
            DefaultValue = string.Empty,
            Description = slot == 1
                ? "User OAuth Token, starts with xoxp-. Keys refer to a workspace by its name, subdomain (acme), team id (T…) or slot number."
                : string.Empty
        };
    }

    public IReadOnlyList<PluginSettingAction> SettingsActions =>
    [
        new PluginSettingAction { Label = "Copy app manifest and open Slack", Invoke = CopyManifestAsync },
        new PluginSettingAction { Label = "Test tokens", Invoke = TestTokensAsync },
        new PluginSettingAction { Label = "Get support", Invoke = () => OpenLink(SupportUrl, SupportUrl) },
        new PluginSettingAction { Label = "Report a bug or request a feature", Invoke = () => OpenLink(IssuesUrl, IssuesUrl) }
    ];

    private Task<string> OpenLink(string url, string name) =>
        Task.FromResult(_host.OpenBrowser(url) ? $"Opened {name}" : $"Could not open a browser; visit {url}");

    /// <summary>
    /// Puts the app manifest on the clipboard and opens Slack's app list, where the user picks
    /// Create New App → From a manifest and pastes it. (Slack's documented pre-filled
    /// <c>?new_app=1&amp;manifest_json=</c> link opens the plain Create new app dialog without the
    /// manifest, so it is not used.)
    /// </summary>
    private Task<string> CopyManifestAsync()
    {
        var file = Path.Combine(Path.GetDirectoryName(typeof(SlackPlugin).Assembly.Location) ?? ".", "slack-app-manifest.json");
        var copied = Clipboard.SetText(SlackApi.Manifest());
        var opened = _host.OpenBrowser(SlackApi.AppsUrl);

        var first = copied
            ? "Manifest copied to the clipboard."
            : $"Could not reach the clipboard; copy the manifest from {file}.";
        var where = opened ? "Slack's app page is open" : $"Open {SlackApi.AppsUrl}";
        return Task.FromResult($"{first} {where}: press Create New App, choose From a manifest, Continue, pick the workspace, " +
                               "then paste over everything on the JSON tab and press Next, then Create. Repeat for each workspace.");
    }

    public void OnSettingsSaved()
    {
        // Stop the old poller, then swap in a set built from the new tokens; the poller restarts
        // from the next key render.
        WorkspaceGate.Reset();
        var old = Workspaces;
        Workspaces = CreateWorkspaces();
        old.Changed -= RefreshAccountKeys;
        old.Dispose();

        Keys.Invalidate();
        RefreshAccountKeys();
        _ = Workspaces.IdentifyAsync();
    }

    private async Task<string> TestTokensAsync()
    {
        if (!Workspaces.Any)
            return "No tokens set. Follow the steps above, starting with \"Copy app manifest and open Slack\", then paste the User OAuth Token and Save.";

        var lines = await Task.WhenAll(Workspaces.All.Select(DescribeAsync)).ConfigureAwait(false);
        var duplicates = Workspaces.All.Where(w => w.Identity is not null).GroupBy(w => w.Identity!.TeamId).Where(g => g.Count() > 1)
            .Select(g => $"Slots {string.Join(" and ", g.Select(w => w.Slot))} are the same workspace ({g.First().Name}).");
        _host.RequestButtonRefresh(WorkspaceCommand.Name);
        return string.Join(" ", lines.Concat(duplicates));
    }

    private static async Task<string> DescribeAsync(Workspace w)
    {
        try
        {
            var id = await w.Api.AuthTestAsync().ConfigureAwait(false);
            w.Identity = id;
            w.Error = null;
            var missing = SlackApi.Scopes.Where(s => !id.Scopes.Contains(s)).ToList();
            var scopes = missing.Count == 0 ? "all scopes" : $"missing {string.Join(", ", missing)}; add them under OAuth & Permissions and reinstall";
            var domain = id.Domain.Length > 0 ? $"{id.Domain}, " : string.Empty;
            return $"Slot {w.Slot}: {id.Team} ({domain}{id.TeamId}) as {id.User}, {scopes}.";
        }
        catch (SlackApiException ex)
        {
            w.Error = ex.Error;
            return ex.Error is "invalid_auth" or "not_authed" or "token_revoked"
                ? $"Slot {w.Slot}: Slack rejected the token ({ex.Error}); copy the User OAuth Token again."
                : $"Slot {w.Slot}: Slack said {ex.Error}.";
        }
        catch (HttpRequestException ex)
        {
            return $"Slot {w.Slot}: could not reach Slack ({ex.Message}).";
        }
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
