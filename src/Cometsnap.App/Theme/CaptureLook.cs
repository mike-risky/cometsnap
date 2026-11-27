using System.Windows.Media;
using Cometsnap.Core.Settings;

namespace Cometsnap.App.Theme;

/// <summary>
/// The capture screen's colors for the theme picked in Settings (see <see cref="CaptureThemes"/>).
/// Everything in capture reads from here: crosshair, frames, loupe, scrolling frame, Copied card.
/// </summary>
public static class CaptureLook
{
    public static CaptureTheme Theme { get; private set; } = CaptureThemes.Default;

    public static Color Accent { get; private set; }
    public static Color Tag { get; private set; }
    public static SolidColorBrush AccentBrush { get; private set; } = null!;
    public static SolidColorBrush TagBrush { get; private set; } = null!;
    public static SolidColorBrush TagTextBrush { get; private set; } = null!;

    /// <summary>Changes whenever the theme does, so windows built once can repaint.</summary>
    public static int Version { get; private set; }

    static CaptureLook() => Use(CaptureThemes.DefaultId);

    public static void Use(string? id)
    {
        var theme = CaptureThemes.Get(id);
        if (AccentBrush is not null && theme == Theme) return;
        Theme = theme;
        Accent = ToColor(theme.Accent);
        Tag = ToColor(theme.Tag);
        AccentBrush = SpaceTheme.WithAlpha(Accent, 0xFF);
        TagBrush = SpaceTheme.WithAlpha(Tag, 0xFF);
        TagTextBrush = SpaceTheme.WithAlpha(ToColor(theme.TagText), 0xFF);
        Version++;
    }

    public static SolidColorBrush AccentWithAlpha(byte alpha) => SpaceTheme.WithAlpha(Accent, alpha);

    public static Color ToColor(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
