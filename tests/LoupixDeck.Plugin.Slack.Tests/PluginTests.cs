using LoupixDeck.PluginSdk;
using Xunit;

namespace LoupixDeck.Plugin.Slack.Tests;

/// <summary>A host that records what the plugin asks of it.</summary>
internal sealed class FakeHost : IPluginHost, IPluginLogger, IPluginSettings
{
    public readonly List<string> Executed = new();
    public readonly List<string> Opened = new();
    public readonly List<string> Overlays = new();
    public readonly Dictionary<string, object?> Values = new();

    public IPluginLogger Logger => this;
    public IPluginSettings Settings => this;
    public string CurrentLanguage => "en";
    public string Tr(string english) => english;
    public FolderGridInfo FolderGrid => new(5, 3, 0);
    public DeviceInfo? ActiveDevice => null;
    public readonly List<string> Refreshed = new();
    public void RequestButtonRefresh(string commandName) { lock (Refreshed) Refreshed.Add(commandName); }
    public void ExecuteCommand(string command) => Executed.Add(command);
    public void OpenFolder(IFolderProvider provider) { }
    public bool OpenBrowser(string url) { Opened.Add(url); return true; }
    public void OverlayTouchText(int slot, string text, TimeSpan duration) => Overlays.Add(text);
    public int GetTouchSlotForRotary(int rotaryIndex) => -1;
    public bool RequestExclusiveMode(IExclusiveModeProvider provider) => false;
    public void ReleaseExclusiveMode(IExclusiveModeProvider provider) { }
    public bool IsInExclusiveMode => false;
    public IFullDisplayRenderSession? RequestFullDisplayRenderer(IFullDisplayRenderer renderer) => null;
    public IReadOnlyList<string> GetButtonStates(string commandName) => [];
    public string? GetActiveButtonState(string commandName) => null;
    public bool SetActiveButtonState(string commandName, string stateNameOrId) => false;

    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }

    public T? Get<T>(string key, T? defaultValue = default) => Values.TryGetValue(key, out var v) && v is T t ? t : defaultValue;
    public void Set<T>(string key, T value) => Values[key] = value;
    public bool Contains(string key) => Values.ContainsKey(key);
    public void Remove(string key) => Values.Remove(key);
    public IEnumerable<string> Keys => Values.Keys;
    public void Save() { }
}

public sealed class PluginTests
{
    private static (SlackPlugin Plugin, FakeHost Host) Load()
    {
        var host = new FakeHost();
        host.Values["bring_to_front"] = false; // never touch the real Slack from a test
        var plugin = new SlackPlugin();
        plugin.Initialize(host);
        return (plugin, host);
    }

    private static CommandContext Ctx(FakeHost host, ButtonTargets target = ButtonTargets.TouchButton, params string[] p) =>
        new() { Parameters = p, Target = target, SourceIndex = 0, Host = host };

    [Fact]
    public void Command_names_are_unique_and_prefixed()
    {
        var (plugin, _) = Load();
        var names = plugin.GetCommands().Select(c => c.Descriptor.CommandName).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.All(names, n => Assert.StartsWith("Slack.", n));
        plugin.Shutdown();
    }

    [Fact]
    public void Every_group_has_a_card()
    {
        var (plugin, _) = Load();
        var cards = plugin.GetCommandGroups().Select(g => g.Group).ToHashSet();
        Assert.All(plugin.GetCommands(), c => Assert.Contains(c.Descriptor.Group, cards));
        plugin.Shutdown();
    }

    [Fact]
    public void Icons_are_single_mdi_code_points()
    {
        var (plugin, _) = Load();
        foreach (var c in plugin.GetCommands())
        {
            var icon = c.Descriptor.Icon!;
            Assert.True(char.IsSurrogatePair(icon, 0) && icon.Length == 2, $"{c.Descriptor.CommandName}: icon is not one code point");
            Assert.InRange(char.ConvertToUtf32(icon, 0), 0xF0000, 0xF1FFF);
        }

        plugin.Shutdown();
    }

    [Fact]
    public async Task Shortcut_key_presses_the_shortcut_for_this_os()
    {
        var (plugin, host) = Load();
        var dms = plugin.GetCommands().Single(c => c.Descriptor.CommandName == "Slack.DMs");

        await dms.Execute(Ctx(host));

        var expected = OperatingSystem.IsMacOS() ? "Cmd+Shift+K" : "Ctrl+Shift+2";
        Assert.Equal([$"System.KeyCombination({expected})"], host.Executed);
        plugin.Shutdown();
    }

    [Fact]
    public async Task Dial_sends_one_press_per_tick_capped()
    {
        var (plugin, host) = Load();
        var dial = (IAdjustmentCommand)plugin.GetCommands().Single(c => c.Descriptor.CommandName == "Slack.UnreadDial");

        await dial.ApplyAdjustment(Ctx(host, ButtonTargets.RotaryEncoder), 2);
        await dial.ApplyAdjustment(Ctx(host, ButtonTargets.RotaryEncoder), -20);
        await dial.ApplyReset(Ctx(host, ButtonTargets.RotaryEncoder));

        Assert.Equal(2, host.Executed.Count(e => e == "System.KeyCombination(Alt+Shift+Down)"));
        Assert.Equal(5, host.Executed.Count(e => e == "System.KeyCombination(Alt+Shift+Up)"));
        Assert.Equal("System.KeyCombination(Esc)", host.Executed[^1]);
        plugin.Shutdown();
    }

    [Fact]
    public async Task Conversation_key_opens_a_deep_link_or_asks_for_one()
    {
        var (plugin, host) = Load();
        var cmd = plugin.GetCommands().Single(c => c.Descriptor.CommandName == "Slack.Conversation");

        await cmd.Execute(Ctx(host, ButtonTargets.TouchButton, "C0123ABCD", "general", "T0001AAAA"));
        await cmd.Execute(Ctx(host, ButtonTargets.TouchButton, "#general"));

        Assert.Equal(["slack://channel?team=T0001AAAA&id=C0123ABCD"], host.Opened);
        Assert.Equal(["Set channel"], host.Overlays);
        plugin.Shutdown();
    }

    [Fact]
    public async Task Account_keys_without_a_token_say_so_and_call_nothing()
    {
        var (plugin, host) = Load();
        foreach (var name in new[] { "Slack.Status", "Slack.Focus", "Slack.Away", "Slack.ClearStatus" })
        {
            var cmd = plugin.GetCommands().Single(c => c.Descriptor.CommandName == name);
            var value = ((IValueDisplayCommand)cmd).GetValue(Ctx(host, ButtonTargets.TouchButton, "Lunch", ":taco:", "30"));
            Assert.Equal("Set token", value?.Text);
            await cmd.Execute(Ctx(host, ButtonTargets.TouchButton, "Lunch", ":taco:", "30"));
        }

        Assert.All(host.Overlays, o => Assert.Equal("Set token", o));
        Assert.Empty(host.Executed);
        plugin.Shutdown();
    }
}
