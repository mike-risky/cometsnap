namespace Cometsnap.Core.Settings;

/// <summary>
/// One look for the capture screen, taken from the brand "Electric" palette (assets/BRAND.md).
/// Accent: crosshair, selection and window frame, glow, loupe border, scrolling frame, Copied card.
/// Tag: the size label (and the Copied card title), with <see cref="TagText"/> written on it.
/// Colors are 0xRRGGBB.
/// </summary>
public sealed record CaptureTheme(string Id, string Name, uint Accent, uint Tag, uint TagText);

public static class CaptureThemes
{
    public const string DefaultId = "blue";

    private const uint Night = 0x07113A;
    private const uint White = 0xFFFFFF;

    // Names are English; the UI translates them with L.T (see SettingsWindow.ThemeName).
    public static readonly IReadOnlyList<CaptureTheme> All = new[]
    {
        new CaptureTheme("blue", "Comet blue", 0x3D6BFF, 0xFFD60A, Night),
        new CaptureTheme("yellow", "Sun yellow", 0xFFD60A, 0x3D6BFF, White),
        new CaptureTheme("terracotta", "Terracotta", 0xE2553A, 0xFFD60A, Night),
        new CaptureTheme("orange", "Flame orange", 0xF09625, 0x3D6BFF, White),
        new CaptureTheme("stardust", "Stardust", 0xA9B6F0, 0xFFD60A, Night),
    };

    public static CaptureTheme Default => All[0];

    /// <summary>The theme with this id, or the default (blue) for empty or unknown ids.</summary>
    public static CaptureTheme Get(string? id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;

    public static string Hex(uint rgb) => $"#{rgb:X6}";
}

/// <summary>Text forms of a picked color, for the color picker.</summary>
public static class ColorText
{
    public static string Hex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";
    public static string Rgb(byte r, byte g, byte b) => $"rgb({r}, {g}, {b})";
}
