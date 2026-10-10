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
    private const string RowsKey = "workspace_rows";

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

        // Learn the workspace names now, so the settings page and the keys can show them.
        if (Workspaces.Any) _ = Workspaces.IdentifyAsync();

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

        var set = new WorkspaceSet(tokens, _host.Logger, HandlerFactory, Workspaces);
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
    //
    // The host rebuilds the page from SettingsSchema and SettingsActions after every action, so
    // the workspace cards and the Add / Remove buttons below follow the tokens as they change.

    private const string SetupSteps =
        "Each workspace needs its own copy of a small Slack app. For each one:\n" +
        "1. Press \"Copy app manifest\", then \"Manage Slack apps\".\n" +
        "2. In Slack: Create New App → From a manifest → Continue → pick the workspace → Next.\n" +
        "3. On the JSON tab, replace the text with the manifest (paste) → Next → Create.\n" +
        "4. In the app's sidebar: Install App → Install to the workspace → Allow.\n" +
        "5. Copy the User OAuth Token (xoxp-…) into the field below, press Save, then \"Test tokens\".";

    private string Token(int slot) => _host.Settings.Get(TokenKeyPrefix + slot, string.Empty)?.Trim() ?? string.Empty;

    /// <summary>Workspace cards on the page: every filled slot, plus empty ones the user added, at least one.</summary>
    private int Rows
    {
        get
        {
            var lastFilled = Enumerable.Range(1, WorkspaceSet.Slots).LastOrDefault(i => Token(i).Length > 0);
            return Math.Clamp(Math.Max(lastFilled, _host.Settings.Get(RowsKey, 1)), 1, WorkspaceSet.Slots);
        }
    }

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
        .. Enumerable.Range(1, Rows).SelectMany(WorkspaceCard),
        new PluginSettingDescriptor
        {
            Key = "about",
            Label = "About and support",
            Kind = PluginSettingKind.Heading,
            Description = "Made by web-engineer. Not affiliated with or endorsed by Slack Technologies."
        }
    ];

    /// <summary>One card per workspace: its name and details as the heading, its token as the only field.</summary>
    private IEnumerable<PluginSettingDescriptor> WorkspaceCard(int slot)
    {
        var token = Token(slot);
        var w = Workspaces.All.FirstOrDefault(x => x.Slot == slot);
        string title, description;
        if (token.Length == 0)
        {
            title = slot == 1 && !Workspaces.Any ? "Connect a Slack workspace" : "New workspace";
            description = SetupSteps;
        }
        else if (w?.Identity is { } id)
        {
            title = id.Team;
            var domain = id.Domain.Length > 0 ? $"{id.Domain}.slack.com · " : string.Empty;
            var missing = SlackApi.Scopes.Where(sc => !id.Scopes.Contains(sc)).ToList();
            description = $"{domain}{id.TeamId} · signed in as {id.User} · workspace {slot}" +
                          (missing.Count > 0 ? $"\nMissing scopes: {string.Join(", ", missing)}. Add them under OAuth & Permissions and reinstall the app." : string.Empty);
        }
        else if (w?.Error is { } error)
        {
            title = $"Workspace {slot}: token not working";
            description = error is "invalid_auth" or "not_authed" or "token_revoked"
                ? $"Slack rejected this token ({error}). Copy the User OAuth Token again from the app's OAuth & Permissions page."
                : $"Slack said: {error}. Press \"Test tokens\" to try again.";
        }
        else
        {
            title = $"Workspace {slot}";
            description = "Press \"Test tokens\" to check this token and show the workspace's name.";
        }

        yield return new PluginSettingDescriptor { Key = $"workspace_{slot}", Label = title, Kind = PluginSettingKind.Heading, Description = description };
        yield return new PluginSettingDescriptor
        {
            Key = TokenKeyPrefix + slot,
            Label = "User OAuth token",
            Kind = PluginSettingKind.Password,
            DefaultValue = string.Empty
        };
    }

    public IReadOnlyList<PluginSettingAction> SettingsActions
    {
        get
        {
            var actions = new List<PluginSettingAction>
            {
                new() { Label = "Copy app manifest", Invoke = CopyManifestAsync },
                new() { Label = "Manage Slack apps", Invoke = () => OpenLink(SlackApi.AppsUrl, "Slack's app page") },
                new() { Label = "Test tokens", Invoke = TestTokensAsync }
            };

            var rows = Rows;
            var hasEmpty = Enumerable.Range(1, rows).Any(i => Token(i).Length == 0);
            if (!hasEmpty && rows < WorkspaceSet.Slots)
                actions.Add(new PluginSettingAction { Label = "+ Add workspace", Invoke = AddWorkspaceAsync });

            foreach (var slot in Enumerable.Range(1, rows).Where(i => Token(i).Length > 0))
            {
                var name = Workspaces.All.FirstOrDefault(x => x.Slot == slot)?.Identity?.Team ?? $"workspace {slot}";
                actions.Add(new PluginSettingAction { Label = $"× Remove {name}", Invoke = () => RemoveWorkspaceAsync(slot) });
            }

            actions.Add(new PluginSettingAction { Label = "Get support", Invoke = () => OpenLink(SupportUrl, SupportUrl) });
            actions.Add(new PluginSettingAction { Label = "Report a bug or request a feature", Invoke = () => OpenLink(IssuesUrl, IssuesUrl) });
            return actions;
        }
    }

    private Task<string> OpenLink(string url, string name) =>
        Task.FromResult(_host.OpenBrowser(url) ? $"Opened {name}" : $"Could not open a browser; visit {url}");

    private Task<string> CopyManifestAsync()
    {
        if (Clipboard.SetText(SlackApi.Manifest()))
            return Task.FromResult("Manifest copied. Paste it on the JSON tab after choosing From a manifest.");

        var file = Path.Combine(Path.GetDirectoryName(typeof(SlackPlugin).Assembly.Location) ?? ".", "slack-app-manifest.json");
        return Task.FromResult($"Could not reach the clipboard; copy the manifest from {file}.");
    }

    private Task<string> AddWorkspaceAsync()
    {
        _host.Settings.Set(RowsKey, Math.Min(Rows + 1, WorkspaceSet.Slots));
        _host.Settings.Save();
        return Task.FromResult("Added an empty workspace card. Follow its steps, paste the token, then Save.");
    }

    /// <summary>Drops one workspace and closes the gap, so the remaining cards stay in order.</summary>
    private Task<string> RemoveWorkspaceAsync(int slot)
    {
        var name = Workspaces.All.FirstOrDefault(x => x.Slot == slot)?.Name ?? $"workspace {slot}";
        var kept = Enumerable.Range(1, WorkspaceSet.Slots).Where(i => i != slot).Select(Token).Where(t => t.Length > 0).ToList();
        for (var i = 1; i <= WorkspaceSet.Slots; i++)
            _host.Settings.Set(TokenKeyPrefix + i, i <= kept.Count ? kept[i - 1] : string.Empty);
        _host.Settings.Remove(LegacyTokenKey);
        _host.Settings.Set(RowsKey, Math.Max(1, kept.Count));
        _host.Settings.Save();
        OnSettingsSaved();
        return Task.FromResult($"Removed {name}. The Slack app itself stays in that workspace; delete it under Manage Slack apps if you no longer want it.");
    }

    public void OnSettingsSaved()
    {
        // Stop the old poller, then swap in a set built from the new tokens (keeping the names
        // already learnt); the poller restarts from the next key render.
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
            return "No tokens yet. Follow the steps in the workspace card, paste the User OAuth Token, then Save.";

        var lines = await Task.WhenAll(Workspaces.All.Select(DescribeAsync)).ConfigureAwait(false);
        var duplicates = Workspaces.All.Where(w => w.Identity is not null).GroupBy(w => w.Identity!.TeamId).Where(g => g.Count() > 1)
            .Select(g => $"{g.First().Name} is connected twice; remove one.");
        _host.RequestButtonRefresh(WorkspaceCommand.Name);
        return string.Join("\n", lines.Concat(duplicates));
    }

    private static async Task<string> DescribeAsync(Workspace w)
    {
        try
        {
            var id = await w.Api.AuthTestAsync().ConfigureAwait(false);
            w.Identity = id;
            w.Error = null;
            var missing = SlackApi.Scopes.Where(s => !id.Scopes.Contains(s)).ToList();
            return missing.Count == 0
                ? $"{id.Team}: working, signed in as {id.User}."
                : $"{id.Team}: missing {string.Join(", ", missing)}.";
        }
        catch (SlackApiException ex)
        {
            w.Error = ex.Error;
            return $"Workspace {w.Slot}: {(ex.Error is "invalid_auth" or "not_authed" or "token_revoked" ? "Slack rejected the token" : "Slack said " + ex.Error)}.";
        }
        catch (HttpRequestException ex)
        {
            return $"Workspace {w.Slot}: could not reach Slack ({ex.Message}).";
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
