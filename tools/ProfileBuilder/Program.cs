// Builds Profiles/LoupeDeck-CT-example.loupixprofile: a ready-made Slack workspace for the
// Loupedeck CT, written as a LoupixDeck portable package (manifest.json + payload.json in a zip).
//
// Every key takes its look from the plugin's own CommandDescriptor (ButtonLayout, or the layouts of
// its States), translated to layers the way LoupixDeck's ActionAssignment does when a command is
// assigned in the editor. LoupixDeck does not build those layers on import, so the package carries
// them. Icons are stored by Material Design Icons name, looked up in LoupixDeck's mdi-catalog.json.
//
// Usage: dotnet run --project tools/ProfileBuilder [-- <path to mdi-catalog.json>]

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using LoupixDeck.Plugin.Slack;
using LoupixDeck.PluginSdk;

var repo = FindRepoRoot();
var catalogPath = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(repo, "..", "LoupixDeck", "LoupixDeck", "Assets", "Fonts", "mdi-catalog.json"));
if (!File.Exists(catalogPath))
{
    Console.Error.WriteLine($"mdi-catalog.json not found at {catalogPath}; pass its path as the first argument.");
    return 1;
}

var glyphNames = LoadGlyphNames(catalogPath);
var plugin = new SlackPlugin();
var commands = plugin.GetCommands().ToDictionary(c => c.Descriptor.CommandName);
var pluginJson = JsonNode.Parse(File.ReadAllText(Path.Combine(repo, "plugin.json")))!;
var ids = new IdSource();

// ---- the layout ----
// The CT's touch screen is a 4×3 grid (slots 0–11, row by row); slots 12 and 13 are the side strips,
// which this workspace leaves to the dials.

var pages = new[]
{
    new Page("Slack",
    [
        "Slack.Open", "Slack.Unread", "Slack.JumpTo", "Slack.Search",
        "Slack.Activity", "Slack.DMs", "Slack.Threads", "Slack.Unreads",
        "Slack.MarkRead", "Slack.MarkAllRead", "Slack.NewMessage", "Slack.Channels"
    ]),
    new Page("Write",
    [
        "Slack.Bold", "Slack.Italic", "Slack.Strikethrough", "Slack.Code",
        "Slack.BulletedList", "Slack.NumberedList", "Slack.Quote", "Slack.Link",
        "Slack.CodeBlock", "Slack.NewLine", "Slack.Upload", "Slack.EditLast"
    ]),
    new Page("Status",
    [
        "Slack.Huddle", "Slack.HuddleMute", "Slack.Focus(60)", "Slack.Away",
        "Slack.Status(In a meeting,:calendar:,60)", "Slack.Status(Lunch,:sandwich:,60)",
        "Slack.Status(Focusing,:headphones:,120)", "Slack.Status(Commuting,:bus:,0)",
        "Slack.ClearStatus", "Slack.StatusDialog", "Slack.WorkspaceSwitcher", "Slack.Preferences"
    ])
};

// Left and right dials (top to bottom), then the wheel's modes.
var leftDials = new[] { Dial("Chats", "Slack.ConversationDial"), Dial("Unread", "Slack.UnreadDial"), Dial("History", "Slack.HistoryDial") };
var rightDials = new[]
{
    Dial("Zoom", "Slack.ZoomDial"),
    new DialSpec("Scroll", "System.MouseScroll(1)", "System.MouseScroll(-1)", ""),
    new DialSpec("Volume", "System.KeyCombination(VolumeDown)", "System.KeyCombination(VolumeUp)", "System.KeyCombination(VolumeMute)")
};
var wheel = new[]
{
    new DialSpec("Scroll", "System.MouseScroll(1)", "System.MouseScroll(-1)", ""),
    Dial("Chats", "Slack.ConversationDial"),
    Dial("Unread", "Slack.UnreadDial")
};

// ---- build ----

var touchPages = new JsonArray(pages.Select((p, i) => (JsonNode)TouchPage(p, i + 1)).ToArray());
var workspace = new JsonObject
{
    ["Id"] = ids.Next(),
    ["Name"] = "Slack",
    ["StartupTouchPageIndex"] = 0,
    ["RotaryButtonPages"] = new JsonArray(),
    ["LeftRotaryButtonPages"] = new JsonArray(RotaryPage("Slack", "Left", leftDials)),
    ["RightRotaryButtonPages"] = new JsonArray(RotaryPage("Slack", "Right", rightDials)),
    ["TouchButtonPages"] = touchPages
};

var manifest = new JsonObject
{
    ["FormatVersion"] = 1,
    ["ConfigSchemaVersion"] = 13,
    ["Kind"] = "Workspace",
    ["Name"] = "Slack",
    ["Description"] =
        "A Slack workspace for the Loupedeck CT. Page Slack: open Slack, unread badge, jump to, search, Activity, DMs, threads, " +
        "unreads and mark read. Page Write: formatting, lists, code, upload and edit last. Page Status: huddle and mute, pause " +
        "notifications, away, four status presets, clear status and workspace switcher. Dials: conversations, unreads, history, " +
        "zoom, scroll and volume; the wheel scrolls or steps through conversations. Status keys need a Slack token in the plugin settings.",
    ["SourceDeviceSlug"] = "loupedeck-ct",
    ["SourceDeviceName"] = "Loupedeck CT",
    ["SourceTouchButtonCount"] = 14,
    ["SourceRotaryButtonCount"] = 7,
    ["SourceHasSideStrips"] = true,
    ["RequiredPlugins"] = new JsonArray(new JsonObject
    {
        ["Id"] = (string)pluginJson["id"]!,
        ["Name"] = (string)pluginJson["name"]!,
        ["Version"] = (string)pluginJson["version"]!,
        ["ProjectUrl"] = (string)pluginJson["projectUrl"]!
    }),
    // Empty: the importer scans the payload for the commands it uses.
    ["ReferencedCommands"] = new JsonArray(),
    ["Assets"] = new JsonArray(),
    ["Macros"] = new JsonArray(),
    ["PayloadFile"] = "payload.json"
};

var payload = new JsonObject { ["Workspace"] = workspace };
var output = Path.Combine(repo, "Profiles", "LoupeDeck-CT-example.loupixprofile");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.Delete(output);
using (var zip = ZipFile.Open(output, ZipArchiveMode.Create))
{
    Write(zip, "manifest.json", manifest);
    Write(zip, "payload.json", payload);
}

Console.WriteLine($"Wrote {Path.GetRelativePath(repo, output)}: {pages.Length} touch pages, {leftDials.Length + rightDials.Length} dials, {wheel.Length} wheel modes");
return 0;

// ---- helpers ----

JsonObject TouchPage(Page page, int number)
{
    var buttons = new JsonArray();
    for (var slot = 0; slot < 14; slot++)
        buttons.Add(slot < page.Keys.Length ? TouchButton(slot, page.Keys[slot]) : EmptyButton(slot));

    return new JsonObject
    {
        ["Id"] = ids.Next(),
        ["Name"] = page.Name,
        ["Page"] = number,
        ["TouchButtons"] = buttons,
        ["WheelModes"] = new JsonArray(wheel.Select(d => (JsonNode)RotaryButton(6, d)).ToArray()),
        ["WheelModeIndex"] = 0
    };
}

JsonObject TouchButton(int slot, string command)
{
    var name = command.Split('(')[0];
    if (!commands.TryGetValue(name, out var cmd)) throw new InvalidOperationException($"Unknown command {name}");
    var d = cmd.Descriptor;
    var states = new JsonArray();

    if (d.States.Count > 0)
    {
        // The command owns the states: one per declared state, in order, each with its own layers.
        foreach (var s in d.States)
            states.Add(State(s.Name, command, s.Layout ?? (states.Count == 0 ? d.ButtonLayout : null), d.DisplayName, d.Icon));
    }
    else
    {
        states.Add(State("Default", command, d.ButtonLayout, d.DisplayName, d.Icon));
    }

    var button = new JsonObject
    {
        ["Index"] = slot,
        ["States"] = states,
        ["DefaultStateId"] = states[0]!["Id"]!.GetValue<string>(),
        ["Mode"] = d.States.Count > 0 ? 1 : 0,
        ["Command"] = command
    };
    if (d.States.Count > 0) button["StateOwnerCommand"] = OwnerKey(command);
    return button;
}

JsonObject EmptyButton(int slot)
{
    var id = ids.Next();
    return new JsonObject
    {
        ["Index"] = slot,
        ["States"] = new JsonArray(new JsonObject { ["Id"] = id, ["Name"] = "Default", ["Layers"] = new JsonArray() }),
        ["DefaultStateId"] = id
    };
}

JsonObject State(string name, string command, ButtonLayoutDescriptor? layout, string label, string? icon)
{
    var state = new JsonObject
    {
        ["Id"] = ids.Next(),
        ["Name"] = name,
        ["Command"] = command,
        ["VibrationEnabled"] = true,
        ["Layers"] = Layers(layout, label, icon)
    };
    if (layout?.BackgroundColor is { } bg)
    {
        state["BackColor"] = bg;
        state["BackgroundEnabled"] = true;
    }

    return state;
}

// Mirrors ActionAssignment.AddLayers for a 90 px key (the CT's), where offsets need no scaling.
JsonArray Layers(ButtonLayoutDescriptor? layout, string label, string? icon)
{
    var layers = new JsonArray();
    switch (layout?.Mode ?? ButtonLayoutMode.Default)
    {
        case ButtonLayoutMode.None:
            break;
        case ButtonLayoutMode.Custom:
            foreach (var l in layout!.Layers ?? []) if (Layer(l, label, icon) is { } node) layers.Add(node);
            break;
        default:
            // The host's standard look: the command's icon above its name.
            if (icon is not null) layers.Add(Symbol(label, icon, 0, -8, 46 / 90.0, null));
            layers.Add(new JsonObject
            {
                ["kind"] = "text", ["Id"] = ids.Next(), ["Name"] = label, ["Text"] = label, ["TextSize"] = 11,
                ["Centered"] = true, ["PositionX"] = 0, ["PositionY"] = 27, ["BoxWidth"] = 88, ["BoxHeight"] = 22, ["TextColor"] = "White"
            });
            break;
    }

    return layers;
}

JsonObject? Layer(ButtonLayerDescriptor l, string label, string? icon)
{
    var name = string.IsNullOrEmpty(l.Name) ? label : l.Name;
    switch (l.Kind)
    {
        case ButtonLayerKind.Symbol:
            return Symbol(name, l.Glyph ?? icon ?? string.Empty, l.OffsetX, l.OffsetY, Math.Clamp(l.IconScale, 0.1, 1.0), l.Color);
        case ButtonLayerKind.Text:
            var text = new JsonObject
            {
                ["kind"] = "text", ["Id"] = ids.Next(), ["Name"] = name, ["Text"] = l.Text ?? label, ["TextSize"] = l.TextSize,
                ["Centered"] = true, ["PositionX"] = l.OffsetX, ["PositionY"] = l.OffsetY, ["BoxWidth"] = l.BoxWidth, ["BoxHeight"] = l.BoxHeight,
                // LoupixDeck's TextSource: Static 0, DialValue 2, ValueDetail 3.
                ["TextSource"] = l.TextSource switch { ButtonTextSource.Value => 2, ButtonTextSource.Detail => 3, _ => 0 }
            };
            if (l.Color is not null) text["TextColor"] = l.Color;
            return text;
        case ButtonLayerKind.Indicator:
            var ring = new JsonObject
            {
                ["kind"] = "indicator", ["Id"] = ids.Next(), ["Name"] = name, ["Scale"] = Math.Clamp(l.IconScale, 0.1, 1.0),
                ["PositionX"] = l.OffsetX, ["PositionY"] = l.OffsetY
            };
            if (l.Color is not null) ring["FillColor"] = l.Color;
            if (l.TrackColor is not null) ring["TrackColor"] = l.TrackColor;
            if (l.Thickness is { } t) ring["Thickness"] = t;
            if (l.StartAngle is { } a) ring["StartAngle"] = a;
            if (l.SweepAngle is { } s) ring["SweepAngle"] = s;
            return ring;
        default:
            throw new InvalidOperationException($"Layer kind {l.Kind} is not used by this plugin; teach the builder about it.");
    }
}

JsonObject Symbol(string name, string glyph, int x, int y, double scale, string? color)
{
    if (!glyphNames.TryGetValue(glyph, out var symbolId))
        throw new InvalidOperationException($"Glyph U+{char.ConvertToUtf32(glyph, 0):X} is not in mdi-catalog.json");
    var node = new JsonObject
    {
        ["kind"] = "symbol", ["Id"] = ids.Next(), ["Name"] = name, ["SymbolId"] = symbolId,
        ["PositionX"] = x, ["PositionY"] = y, ["Scale"] = scale, ["Tint"] = color ?? "White"
    };
    return node;
}

JsonObject RotaryPage(string name, string side, DialSpec[] dials) => new()
{
    ["Id"] = ids.Next(),
    ["Name"] = name,
    ["Page"] = 1,
    ["Side"] = side,
    ["StripMode"] = "Segmented",
    ["RotaryButtons"] = new JsonArray(dials.Select((d, i) => (JsonNode)RotaryButton(i, d)).ToArray())
};

static JsonObject RotaryButton(int index, DialSpec d) => new()
{
    ["Index"] = index,
    ["DisplayText"] = d.Label,
    ["RotaryLeftCommand"] = d.Left,
    ["RotaryRightCommand"] = d.Right,
    ["Command"] = d.Press
};

// An adjustment command goes on both turns and the press: the host passes the ticks and the press.
static DialSpec Dial(string label, string command) => new(label, command, command, command);

static string OwnerKey(string command)
{
    var open = command.IndexOf('(');
    if (open < 0) return command.Trim();
    var name = command[..open].Trim();
    var parameters = command[(open + 1)..].TrimEnd(')').Split(',').Select(p => p.Trim()).Where(p => p.Length > 0);
    return $"{name}({string.Join(",", parameters)})";
}

static Dictionary<string, string> LoadGlyphNames(string path)
{
    var catalog = JsonNode.Parse(File.ReadAllText(path))!;
    var map = new Dictionary<string, string>();
    foreach (var icon in catalog["icons"]!.AsArray())
    {
        var name = (string)icon![0]!;
        var glyph = char.ConvertFromUtf32(Convert.ToInt32((string)icon[1]!, 16));
        map.TryAdd(glyph, name);
    }

    return map;
}

static void Write(ZipArchive zip, string entry, JsonNode json)
{
    using var stream = zip.CreateEntry(entry, CompressionLevel.Optimal).Open();
    using var writer = new StreamWriter(stream);
    writer.Write(json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LoupixDeck.Plugin.Slack.csproj"))) dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Run from inside the repository.");
}

internal sealed record Page(string Name, string[] Keys);

internal sealed record DialSpec(string Label, string Left, string Right, string Press);

/// <summary>Stable GUIDs, so regenerating the package gives the same file; the importer renews them anyway.</summary>
internal sealed class IdSource
{
    private int _n;
    public string Next() => $"5ac1d0e0-0000-4000-8000-{++_n:x12}";
}
