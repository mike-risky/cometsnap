using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cometsnap.Core.Styling;

/// <summary>What goes behind a styled capture.</summary>
public enum StyleBackdrop
{
    /// <summary>Transparent: just the capture with its shadow, border and glow.</summary>
    None,
    /// <summary>One solid color.</summary>
    Color,
    /// <summary>A diagonal gradient between two colors.</summary>
    Gradient,
    /// <summary>The capture itself, blurred and enlarged, like frosted glass.</summary>
    Blur,
    /// <summary>Deep space: a night gradient between the two colors, with stars.</summary>
    Space,
}

/// <summary>
/// A fully custom "share-ready" look, built by the user in the Style studio: rounded corners,
/// border, drop shadow (offset, blur, color, opacity), laser or neon glow, and a backdrop.
/// Every part can be on or off, so they combine freely. Sizes are in pixels of the capture.
/// Colors are "#RRGGBB". Stored as is in the settings file (same names on every platform).
/// </summary>
public sealed class SnapStyle
{
    /// <summary>Corner radius in px; 0 = square corners.</summary>
    public int CornerRadius { get; set; } = 12;

    public bool Border { get; set; }
    public int BorderWidth { get; set; } = 3;
    public string BorderColor { get; set; } = "#FFFFFF";

    public bool Shadow { get; set; } = true;
    /// <summary>Shadow offset in px: positive X = right, positive Y = down.</summary>
    public int ShadowX { get; set; }
    public int ShadowY { get; set; } = 10;
    /// <summary>Blur radius in px (0 = a hard shadow).</summary>
    public int ShadowBlur { get; set; } = 28;
    public string ShadowColor { get; set; } = "#000000";
    /// <summary>0 to 100.</summary>
    public int ShadowOpacity { get; set; } = 45;

    /// <summary>Glow around the edge: small = a laser line, big = neon.</summary>
    public bool Glow { get; set; }
    public string GlowColor { get; set; } = "#3D6BFF";
    /// <summary>How far the glow reaches, in px.</summary>
    public int GlowSize { get; set; } = 18;
    /// <summary>0 to 100.</summary>
    public int GlowStrength { get; set; } = 85;

    public StyleBackdrop Backdrop { get; set; } = StyleBackdrop.None;
    public string BackdropColor { get; set; } = "#3D6BFF";
    public string BackdropColor2 { get; set; } = "#07113A";
    /// <summary>Space around the capture when there is a backdrop, in px.</summary>
    public int Padding { get; set; } = 64;

    public SnapStyle Clone() => (SnapStyle)MemberwiseClone();

    /// <summary>Keeps every number in a sensible range (a hand-edited settings file can hold anything).</summary>
    public SnapStyle Normalized()
    {
        var s = Clone();
        s.CornerRadius = Math.Clamp(s.CornerRadius, 0, 200);
        s.BorderWidth = Math.Clamp(s.BorderWidth, 1, 60);
        s.ShadowX = Math.Clamp(s.ShadowX, -200, 200);
        s.ShadowY = Math.Clamp(s.ShadowY, -200, 200);
        s.ShadowBlur = Math.Clamp(s.ShadowBlur, 0, 200);
        s.ShadowOpacity = Math.Clamp(s.ShadowOpacity, 0, 100);
        s.GlowSize = Math.Clamp(s.GlowSize, 1, 200);
        s.GlowStrength = Math.Clamp(s.GlowStrength, 0, 100);
        s.Padding = Math.Clamp(s.Padding, 0, 600);
        if (!Enum.IsDefined(s.Backdrop)) s.Backdrop = StyleBackdrop.None;
        s.BorderColor = StyleColors.Normalize(s.BorderColor, "#FFFFFF");
        s.ShadowColor = StyleColors.Normalize(s.ShadowColor, "#000000");
        s.GlowColor = StyleColors.Normalize(s.GlowColor, "#3D6BFF");
        s.BackdropColor = StyleColors.Normalize(s.BackdropColor, "#3D6BFF");
        s.BackdropColor2 = StyleColors.Normalize(s.BackdropColor2, "#07113A");
        return s;
    }
}

/// <summary>Ready-made starting points for the Style studio. Picking one copies it; then everything can be changed.</summary>
public static class SnapStylePresets
{
    /// <summary>Ids and English names (the UI translates them with L.T).</summary>
    public static readonly IReadOnlyList<(string Id, string Name)> All = new[]
    {
        ("clean", "Clean"), ("showcase", "Showcase"), ("frosted", "Frosted"),
        ("neon", "Neon"), ("laser", "Laser"), ("comet", "Comet"), ("polaroid", "Polaroid"),
    };

    public static SnapStyle Get(string id) => id switch
    {
        "showcase" => new SnapStyle { Backdrop = StyleBackdrop.Gradient, BackdropColor = "#6F8FFF", BackdropColor2 = "#1C2C77", Padding = 64, ShadowY = 14, ShadowBlur = 36 },
        "frosted" => new SnapStyle { Backdrop = StyleBackdrop.Blur, Padding = 64, ShadowY = 12, ShadowBlur = 32, Border = true, BorderWidth = 2, BorderColor = "#FFFFFF" },
        "neon" => new SnapStyle { Shadow = false, Glow = true, GlowColor = "#FF2BD6", GlowSize = 36, GlowStrength = 90, Backdrop = StyleBackdrop.Color, BackdropColor = "#0B0B1E", Padding = 72 },
        "laser" => new SnapStyle { Shadow = false, Glow = true, GlowColor = "#FF3B30", GlowSize = 10, GlowStrength = 100, CornerRadius = 6 },
        "comet" => new SnapStyle { Shadow = false, Glow = true, GlowColor = "#3D6BFF", GlowSize = 24, Backdrop = StyleBackdrop.Space, BackdropColor = "#1A2470", BackdropColor2 = "#07113A", Padding = 64 },
        "polaroid" => new SnapStyle { CornerRadius = 0, Border = true, BorderWidth = 14, BorderColor = "#FFFFFF", ShadowX = 8, ShadowY = 10, ShadowBlur = 16, ShadowOpacity = 40 },
        _ => new SnapStyle(),
    };
}

/// <summary>"#RRGGBB" helpers.</summary>
public static class StyleColors
{
    /// <summary>A palette for the color chips (the hex box takes any color).</summary>
    public static readonly IReadOnlyList<string> Palette = new[]
    {
        "#000000", "#FFFFFF", "#8A8FA3", "#FF3B30", "#E2553A", "#F09625", "#FFD60A",
        "#34C759", "#00E5FF", "#3D6BFF", "#7B5CFF", "#FF2BD6", "#07113A",
    };

    /// <summary>Parses "#RGB", "#RRGGBB" or "RRGGBB" into 0xRRGGBB; null when it isn't a color.</summary>
    public static uint? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().TrimStart('#');
        if (t.Length == 3) t = string.Concat(t.Select(c => new string(c, 2)));
        if (t.Length != 6) return null;
        return uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public static uint Parse(string? text, uint fallback) => TryParse(text) ?? fallback;

    public static string Hex(uint rgb) => $"#{rgb & 0xFFFFFF:X6}";

    public static string Normalize(string? text, string fallback) => TryParse(text) is { } v ? Hex(v) : fallback;
}

/// <summary>
/// Reads the style from the settings file. Early test builds stored a preset id ("showcase");
/// that still loads (as that preset) instead of throwing away the whole settings file.
/// </summary>
public sealed class SnapStyleJsonConverter : JsonConverter<SnapStyle>
{
    public override SnapStyle? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return SnapStylePresets.Get(reader.GetString() ?? "");
            case JsonTokenType.StartObject:
                return JsonSerializer.Deserialize<SnapStyle>(ref reader, options) ?? new SnapStyle();
            default:
                reader.Skip();
                return new SnapStyle();
        }
    }

    public override void Write(Utf8JsonWriter writer, SnapStyle value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}

public static class StyledFiles
{
    /// <summary>Name of the file that holds the styled copy of a capture: "snap_1.png" → "snap_1-styled.png".</summary>
    public static string StyledPath(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? "";
        return Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + "-styled" + Path.GetExtension(path));
    }
}
