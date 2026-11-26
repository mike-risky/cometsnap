using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Cometsnap.Linux.Theme;

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

    public static readonly IImmutableSolidColorBrush VoidBrush = new ImmutableSolidColorBrush(Void);
    public static readonly IImmutableSolidColorBrush NavyBrush = new ImmutableSolidColorBrush(Navy);
    public static readonly IImmutableSolidColorBrush NavyLightBrush = new ImmutableSolidColorBrush(NavyLight);
    public static readonly IImmutableSolidColorBrush CometBlueBrush = new ImmutableSolidColorBrush(CometBlue);
    public static readonly IImmutableSolidColorBrush TerracottaBrush = new ImmutableSolidColorBrush(Terracotta);
    public static readonly IImmutableSolidColorBrush SunBrush = new ImmutableSolidColorBrush(Sun);
    public static readonly IImmutableSolidColorBrush StarWhiteBrush = new ImmutableSolidColorBrush(StarWhite);
    public static readonly IImmutableSolidColorBrush DustBrush = new ImmutableSolidColorBrush(Dust);
    public static readonly IImmutableSolidColorBrush RecordRedBrush = new ImmutableSolidColorBrush(RecordRed);
    public static readonly IImmutableSolidColorBrush HoverBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x1A, 0x2A, 0x70));

    public static readonly IBrush NebulaBrush = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Navy, 0), new GradientStop(Void, 1) },
    }.ToImmutable();

    /// <summary>H1 / display font, 28 px or larger only.</summary>
    public static readonly FontFamily DisplayFont = new("avares://cometsnap/Assets/Fonts#Bungee Shade");

    /// <summary>H2 / H3 font.</summary>
    public static readonly FontFamily HeadingFont = new("avares://cometsnap/Assets/Fonts#Bungee");

    /// <summary>UI text: the desktop's own sans-serif fonts.</summary>
    public static readonly FontFamily UiFont = new("Inter, Cantarell, Ubuntu, Noto Sans, DejaVu Sans, Liberation Sans, sans-serif");

    public static IImmutableSolidColorBrush WithAlpha(Color c, byte alpha) => new ImmutableSolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));

    private static Bitmap? _logo;
    public static Bitmap Logo => _logo ??= new Bitmap(AssetLoader.Open(new Uri("avares://cometsnap/Assets/cometsnap-256.png")));

    public static Bitmap LoadAsset(string name) => new(AssetLoader.Open(new Uri($"avares://cometsnap/Assets/{name}")));
}
