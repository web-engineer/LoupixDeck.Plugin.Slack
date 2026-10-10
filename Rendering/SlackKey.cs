using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Slack.Rendering;

/// <summary>
/// The shared Slack look: white icons and text on Slack's aubergine, so Slack keys read as one
/// set on the deck. Commands declare it as ordinary layers the user can restyle.
/// </summary>
internal static class SlackKey
{
    public const string AubergineHex = "#4A154B";
    public const string WhiteHex = "#FFFFFF";
    public const string FadedHex = "#B3FFFFFF";

    /// <summary>Slack's own accent colours, for keys that change state.</summary>
    public const string RedHex = "#E01E5A";
    public const string GreenHex = "#2EB67D";
    public const string YellowHex = "#ECB22E";
    public const string DarkHex = "#1D1C1D";

    /// <summary>Icon above a caption.</summary>
    public static ButtonLayoutDescriptor IconAndCaption(string glyph, string caption, string background = AubergineHex) => new()
    {
        Mode = ButtonLayoutMode.Custom,
        BackgroundColor = background,
        Layers =
        [
            new ButtonLayerDescriptor { Kind = ButtonLayerKind.Symbol, Name = "Icon", Glyph = glyph, IconScale = 0.46, OffsetY = -9, Color = WhiteHex },
            Caption(caption)
        ]
    };

    public static ButtonLayerDescriptor Caption(string text) => new()
    {
        Kind = ButtonLayerKind.Text, Name = "Caption", Text = text, TextSize = 13,
        OffsetY = 26, BoxWidth = 88, BoxHeight = 22, Color = WhiteHex
    };

    /// <summary>A text layer fed by the command's reported value (<see cref="ButtonTextSource.Value"/>) or its detail.</summary>
    public static ButtonLayerDescriptor ValueText(string name, ButtonTextSource source, int size, int offsetY, string color = WhiteHex) => new()
    {
        Kind = ButtonLayerKind.Text, Name = name, TextSource = source, TextSize = size,
        OffsetY = offsetY, BoxWidth = 88, BoxHeight = size + 8, Color = color
    };

    public static ButtonLayerDescriptor Icon(string name, string glyph, double scale, int offsetY, string color = WhiteHex) => new()
    {
        Kind = ButtonLayerKind.Symbol, Name = name, Glyph = glyph, IconScale = scale, OffsetY = offsetY, Color = color
    };

    public static ButtonLayoutDescriptor State(string background, params ButtonLayerDescriptor[] layers) => new()
    {
        Mode = ButtonLayoutMode.Custom,
        BackgroundColor = background,
        Layers = layers
    };
}
