using System.IO.Compression;
using System.Text.Json.Nodes;
using LoupixDeck.PluginSdk;
using Xunit;

namespace LoupixDeck.Plugin.Slack.Tests;

/// <summary>
/// Keeps Profiles/LoupeDeck-CT-example.loupixprofile in step with the plugin. When a command's
/// layout changes, regenerate it: dotnet run --project tools/ProfileBuilder.
/// </summary>
public sealed class ProfileTests
{
    private static readonly string PackagePath = Path.Combine(RepoRoot(), "Profiles", "LoupeDeck-CT-example.loupixprofile");

    private static JsonNode Entry(string name)
    {
        using var zip = ZipFile.OpenRead(PackagePath);
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return JsonNode.Parse(reader.ReadToEnd())!;
    }

    private static IEnumerable<JsonNode> UsedKeys() =>
        Entry("payload.json")["Workspace"]!["TouchButtonPages"]!.AsArray()
            .SelectMany(p => p!["TouchButtons"]!.AsArray())
            .Where(b => b!["Command"] is not null)!;

    [Fact]
    public void Manifest_is_a_ct_workspace_needing_this_plugin()
    {
        var manifest = Entry("manifest.json");
        Assert.Equal("Workspace", (string?)manifest["Kind"]);
        Assert.Equal("loupedeck-ct", (string?)manifest["SourceDeviceSlug"]);
        Assert.Equal("slack", (string?)manifest["RequiredPlugins"]![0]!["Id"]);
        Assert.Equal("payload.json", (string?)manifest["PayloadFile"]);
    }

    [Fact]
    public void Every_command_exists()
    {
        var known = new SlackPlugin().GetCommands().Select(c => c.Descriptor.CommandName).ToHashSet();
        var workspace = Entry("payload.json")["Workspace"]!;
        var dials = new[] { "LeftRotaryButtonPages", "RightRotaryButtonPages" }
            .SelectMany(side => workspace[side]!.AsArray())
            .SelectMany(p => p!["RotaryButtons"]!.AsArray())
            .Concat(workspace["TouchButtonPages"]!.AsArray().SelectMany(p => p!["WheelModes"]!.AsArray()))
            .SelectMany(d => new[] { (string?)d!["RotaryLeftCommand"], (string?)d["RotaryRightCommand"], (string?)d["Command"] });

        foreach (var command in UsedKeys().Select(k => (string?)k["Command"]).Concat(dials))
        {
            if (string.IsNullOrEmpty(command) || command.StartsWith("System.", StringComparison.Ordinal)) continue;
            Assert.Contains(command.Split('(')[0], known);
        }
    }

    [Fact]
    public void Keys_carry_the_states_and_layers_their_command_declares()
    {
        var commands = new SlackPlugin().GetCommands().ToDictionary(c => c.Descriptor.CommandName);
        foreach (var key in UsedKeys())
        {
            var command = (string)key["Command"]!;
            var d = commands[command.Split('(')[0]].Descriptor;
            var states = key["States"]!.AsArray();

            if (d.States.Count > 0)
            {
                Assert.Equal(d.States.Select(s => s.Name), states.Select(s => (string)s!["Name"]!));
                Assert.Equal(1, (int)key["Mode"]!);
                for (var i = 0; i < d.States.Count; i++)
                    Assert.Equal(d.States[i].Layout!.Layers!.Count, states[i]!["Layers"]!.AsArray().Count);
            }
            else if (d.ButtonLayout is { Mode: ButtonLayoutMode.Custom } layout)
            {
                Assert.Equal(layout.Layers!.Count, states.Single()!["Layers"]!.AsArray().Count);
            }

            Assert.All(states, s => Assert.Equal(command, (string?)s!["Command"]));
            Assert.Contains(states, s => (string?)s!["Id"] == (string?)key["DefaultStateId"]);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LoupixDeck.Plugin.Slack.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
