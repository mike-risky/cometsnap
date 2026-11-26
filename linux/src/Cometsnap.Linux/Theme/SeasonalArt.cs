using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Cometsnap.Linux.Core;

namespace Cometsnap.Linux.Theme;

/// <summary>
/// Seasonal touches drawn over the logo, in the logo's own 64×64 coordinates:
/// a snow cap in December, a jack-o'-lantern head (a pumpkin comet) at Halloween.
/// </summary>
public static class SeasonalArt
{
    private static readonly IImmutableSolidColorBrush InkBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x07, 0x11, 0x3A));
    private static readonly IPen Ink = new ImmutablePen(InkBrush, 2.0, lineJoin: PenLineJoin.Round);
    private static readonly IPen Flake = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xE8, 0xF4, 0xFF)), 1.1, lineCap: PenLineCap.Round);
    private static readonly IPen Rib = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xB8, 0x55, 0x12)), 1.3);
    private static readonly IBrush Pumpkin = new ImmutableSolidColorBrush(Color.FromRgb(0xF2, 0x84, 0x1F));
    private static readonly IBrush Stem = new ImmutableSolidColorBrush(Color.FromRgb(0x3F, 0x8A, 0x2E));

    private static readonly Geometry SnowCap = Geometry.Parse(
        "M26.6,24 Q26.8,14.6 33.8,11.6 Q42.6,9.2 48.8,14.6 Q52.6,18.4 52.2,24.6 Q50.2,23.4 49,25.4 Q47.6,23 45.4,24.4 " +
        "Q44.4,27.6 42.4,26.2 Q41.6,23.4 39,23.8 Q36.6,22.2 34.6,24.4 Q33.4,27.2 31.4,25.6 Q30.6,23.4 28.8,23.6 Q27.6,24.8 26.6,24 Z");
    private static readonly Geometry StemShape = Geometry.Parse("M37.6,13.4 Q37.8,8.6 41.8,7.2 L43.4,9.4 Q40.6,10.6 41,13.4 Z");
    private static readonly Geometry RibLine = Geometry.Parse("M39.2,13 V37.4");
    private static readonly Geometry Eyes = Geometry.Parse("M32.2,23.4 L35.4,18.6 L38,23.4 Z M40.6,23.4 L43.2,18.6 L46.4,23.4 Z");
    private static readonly Geometry Mouth = Geometry.Parse(
        "M31.4,27.4 Q39.2,34.6 47.2,27.4 L45.6,27.8 L44.6,29.6 L42.6,28.6 L39.4,30.6 L36.4,28.6 L34.2,29.8 L33.2,28 Z");

    /// <summary>Draws the decoration over a logo drawn at <paramref name="logo"/>. Nothing on an ordinary day.</summary>
    public static void DrawOverlay(DrawingContext dc, Season season, Rect logo)
    {
        if (season == Season.None) return;
        using var _ = dc.PushTransform(Matrix.CreateScale(logo.Width / 64, logo.Height / 64) * Matrix.CreateTranslation(logo.X, logo.Y));
        if (season == Season.Winter)
        {
            // A thick snow cap with soft drips over the comet's head, and a few six-armed snowflakes.
            dc.DrawGeometry(Brushes.White, Ink, SnowCap);
            foreach (var (x, y, r) in new[] { (12.0, 12.0, 3.2), (56.0, 46.0, 2.6), (7.0, 29.0, 2.2), (22.0, 6.0, 2.0) })
                for (var i = 0; i < 3; i++)
                {
                    var a = Math.PI / 3 * i + Math.PI / 2;
                    dc.DrawLine(Flake, new Point(x - r * Math.Cos(a), y - r * Math.Sin(a)), new Point(x + r * Math.Cos(a), y + r * Math.Sin(a)));
                }
        }
        else
        {
            // Jack-o'-lantern over the head: orange body, darker ribs, green stem, a glowing face.
            var center = new Point(39.2, 25.2);
            dc.DrawEllipse(Pumpkin, Ink, center, 14.2, 12.6);
            dc.DrawEllipse(null, Rib, center, 7.2, 12.2);
            dc.DrawGeometry(null, Rib, RibLine);
            dc.DrawGeometry(Stem, Ink, StemShape);
            dc.DrawGeometry(SpaceTheme.SunBrush, null, Eyes);
            dc.DrawGeometry(SpaceTheme.SunBrush, null, Mouth);
        }
    }

    /// <summary>The logo with the seasonal decoration, for the tray icon. Null on an ordinary day or on failure.</summary>
    public static Bitmap? CreateIcon(Season season, int size = 256)
    {
        if (season == Season.None) return null;
        try
        {
            var rtb = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
            using (var dc = rtb.CreateDrawingContext())
            {
                var box = new Rect(0, 0, size, size);
                using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
                    dc.DrawImage(SpaceTheme.Logo, box);
                DrawOverlay(dc, season, box);
            }
            return rtb;
        }
        catch (Exception ex)
        {
            Log.Error("Could not build the seasonal icon", ex);
            return null;
        }
    }
}
