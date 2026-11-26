using System.Windows.Media;

namespace Cometsnap.App.Theme;

/// <summary>
/// The "Electric" brand palette (see assets/BRAND.md).
/// Blue is for the tool (selection, focus); fire colors are for the brand (primary buttons, celebrations).
/// </summary>
public static class SpaceTheme
{
    public static readonly Color Void = Color.FromRgb(0x07, 0x11, 0x3A);        // night
    public static readonly Color Navy = Color.FromRgb(0x0F, 0x1D, 0x57);        // surface
    public static readonly Color NavyLight = Color.FromRgb(0x22, 0x34, 0x8A);   // line
    public static readonly Color CometBlue = Color.FromRgb(0x3D, 0x6B, 0xFF);   // blue: UI accent
    public static readonly Color Terracotta = Color.FromRgb(0xE2, 0x55, 0x3A);  // primary buttons
    public static readonly Color FlameOrange = Color.FromRgb(0xF0, 0x96, 0x25);
    public static readonly Color Sun = Color.FromRgb(0xFF, 0xD6, 0x0A);         // yellow: size tag, celebrations
    public static readonly Color StarWhite = Color.FromRgb(0xFF, 0xFF, 0xFF);   // text
    public static readonly Color Dust = Color.FromRgb(0xA9, 0xB6, 0xF0);        // muted text
    public static readonly Color RecordRed = Color.FromRgb(0xE5, 0x48, 0x4D);

    public static readonly SolidColorBrush VoidBrush = Frozen(Void);
    public static readonly SolidColorBrush NavyBrush = Frozen(Navy);
    public static readonly SolidColorBrush NavyLightBrush = Frozen(NavyLight);
    public static readonly SolidColorBrush CometBlueBrush = Frozen(CometBlue);
    public static readonly SolidColorBrush TerracottaBrush = Frozen(Terracotta);
    public static readonly SolidColorBrush SunBrush = Frozen(Sun);
    public static readonly SolidColorBrush StarWhiteBrush = Frozen(StarWhite);
    public static readonly SolidColorBrush DustBrush = Frozen(Dust);

    public static readonly LinearGradientBrush NebulaBrush = FrozenGradient();

    /// <summary>H1 / display font, 28 px or larger only. Chinese and Hindi fall back to Windows fonts.</summary>
    public static readonly FontFamily DisplayFont = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Bungee Shade, Microsoft YaHei UI, Nirmala UI, Segoe UI");

    /// <summary>H2 / H3 font.</summary>
    public static readonly FontFamily HeadingFont = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Bungee, Microsoft YaHei UI, Nirmala UI, Segoe UI");

    /// <summary>UI text: native Windows 11 font, falling back to Segoe UI on Windows 10.</summary>
    public static readonly FontFamily UiFont = new("Segoe UI Variable Text, Segoe UI");

    public static SolidColorBrush WithAlpha(Color c, byte alpha) => Frozen(Color.FromArgb(alpha, c.R, c.G, c.B));

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static LinearGradientBrush FrozenGradient()
    {
        var b = new LinearGradientBrush(Navy, Void, 90);
        b.Freeze();
        return b;
    }
}
