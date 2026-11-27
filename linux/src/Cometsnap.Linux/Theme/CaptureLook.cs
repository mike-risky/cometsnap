using Avalonia.Media;
using Avalonia.Media.Immutable;
using Cometsnap.Core.Settings;

namespace Cometsnap.Linux.Theme;

/// <summary>
/// The capture screen's colors for the theme picked in Settings (shared CaptureThemes, same as Windows).
/// Crosshair, frame and glow, loupe border, size tag, Copied card and the comet's tail read from here.
/// </summary>
public static class CaptureLook
{
    public static CaptureTheme Theme { get; private set; } = CaptureThemes.Default;
    public static Color Accent { get; private set; } = ToColor(CaptureThemes.Default.Accent);
    public static IImmutableSolidColorBrush AccentBrush { get; private set; } = new ImmutableSolidColorBrush(ToColor(CaptureThemes.Default.Accent));
    public static IImmutableSolidColorBrush TagBrush { get; private set; } = new ImmutableSolidColorBrush(ToColor(CaptureThemes.Default.Tag));
    public static IImmutableSolidColorBrush TagTextBrush { get; private set; } = new ImmutableSolidColorBrush(ToColor(CaptureThemes.Default.TagText));

    /// <summary>Changes whenever the theme does, so cached pens can be rebuilt.</summary>
    public static int Version { get; private set; }

    public static bool IsClassic => Theme.Id == CaptureThemes.DefaultId;

    public static void Use(string? id)
    {
        var theme = CaptureThemes.Get(id);
        if (theme == Theme) return;
        Theme = theme;
        Accent = ToColor(theme.Accent);
        AccentBrush = new ImmutableSolidColorBrush(Accent);
        TagBrush = new ImmutableSolidColorBrush(ToColor(theme.Tag));
        TagTextBrush = new ImmutableSolidColorBrush(ToColor(theme.TagText));
        Version++;
    }

    public static string Name(CaptureTheme t) => t.Id switch
    {
        "blue" => T("Comet blue"),
        "yellow" => T("Sun yellow"),
        "terracotta" => T("Terracotta"),
        "orange" => T("Flame orange"),
        "stardust" => T("Stardust"),
        _ => t.Name,
    };

    public static Color ToColor(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
