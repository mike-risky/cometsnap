using System.Windows.Media;

namespace Cometsnap.App.Theme;

/// <summary>Deep-space palette shared by every Cometsnap surface.</summary>
public static class SpaceTheme
{
    public static readonly Color Void = Color.FromRgb(0x0B, 0x10, 0x26);
    public static readonly Color Navy = Color.FromRgb(0x14, 0x1B, 0x3D);
    public static readonly Color NavyLight = Color.FromRgb(0x22, 0x2B, 0x55);
    public static readonly Color Nebula = Color.FromRgb(0x6C, 0x4B, 0xD8);
    public static readonly Color CometBlue = Color.FromRgb(0x4F, 0xC3, 0xFF);
    public static readonly Color StarWhite = Color.FromRgb(0xEE, 0xF4, 0xFF);
    public static readonly Color Dust = Color.FromRgb(0x9A, 0xA6, 0xC8);

    public static readonly SolidColorBrush VoidBrush = Frozen(Void);
    public static readonly SolidColorBrush NavyBrush = Frozen(Navy);
    public static readonly SolidColorBrush NavyLightBrush = Frozen(NavyLight);
    public static readonly SolidColorBrush CometBlueBrush = Frozen(CometBlue);
    public static readonly SolidColorBrush StarWhiteBrush = Frozen(StarWhite);
    public static readonly SolidColorBrush DustBrush = Frozen(Dust);

    public static readonly LinearGradientBrush NebulaBrush = FrozenGradient();

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static LinearGradientBrush FrozenGradient()
    {
        var b = new LinearGradientBrush(Navy, Color.FromRgb(0x2A, 0x17, 0x5C), 45);
        b.Freeze();
        return b;
    }
}
