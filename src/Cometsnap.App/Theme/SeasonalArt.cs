using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cometsnap.App.Recording;
using Cometsnap.Core.Diagnostics;
using Cometsnap.Core.Fun;

namespace Cometsnap.App.Theme;

/// <summary>
/// Seasonal touches drawn over the logo, in the logo's own 64×64 coordinates:
/// a snow cap in December, a jack-o'-lantern head (a pumpkin comet) at Halloween.
/// </summary>
public static class SeasonalArt
{
    private static readonly Color Ink = Color.FromRgb(0x07, 0x11, 0x3A);

    /// <summary>The decoration to lay over the logo, or null for an ordinary day.</summary>
    public static DrawingGroup? Overlay(Season season)
    {
        if (season == Season.None) return null;
        var g = new DrawingGroup();
        // Fixes the bounds to the logo's 64×64 box, so the overlay lines up when stretched.
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 64, 64))));
        var ink = new Pen(new SolidColorBrush(Ink), 2.0) { LineJoin = PenLineJoin.Round };
        if (season == Season.Winter)
        {
            // A thick snow cap with soft drips over the comet's head.
            g.Children.Add(new GeometryDrawing(Brushes.White, ink, Geometry.Parse(
                "M26.6,24 Q26.8,14.6 33.8,11.6 Q42.6,9.2 48.8,14.6 Q52.6,18.4 52.2,24.6 Q50.2,23.4 49,25.4 Q47.6,23 45.4,24.4 " +
                "Q44.4,27.6 42.4,26.2 Q41.6,23.4 39,23.8 Q36.6,22.2 34.6,24.4 Q33.4,27.2 31.4,25.6 Q30.6,23.4 28.8,23.6 Q27.6,24.8 26.6,24 Z")));
            // A few snowflakes around the tail: six-armed asterisks.
            var flake = new Pen(new SolidColorBrush(Color.FromRgb(0xE8, 0xF4, 0xFF)), 1.1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            foreach (var (x, y, r) in new[] { (12.0, 12.0, 3.2), (56.0, 46.0, 2.6), (7.0, 29.0, 2.2), (22.0, 6.0, 2.0) })
            {
                var arms = new GeometryGroup();
                for (var i = 0; i < 3; i++)
                {
                    var a = Math.PI / 3 * i + Math.PI / 2;
                    arms.Children.Add(new LineGeometry(new Point(x - r * Math.Cos(a), y - r * Math.Sin(a)), new Point(x + r * Math.Cos(a), y + r * Math.Sin(a))));
                }
                g.Children.Add(new GeometryDrawing(null, flake, arms));
            }
        }
        else
        {
            // Jack-o'-lantern over the head: orange body, darker ribs, green stem, a glowing face.
            var center = new Point(39.2, 25.2);
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xF2, 0x84, 0x1F)), ink, new EllipseGeometry(center, 14.2, 12.6)));
            var rib = new Pen(new SolidColorBrush(Color.FromRgb(0xB8, 0x55, 0x12)), 1.3);
            g.Children.Add(new GeometryDrawing(null, rib, new EllipseGeometry(center, 7.2, 12.2)));
            g.Children.Add(new GeometryDrawing(null, rib, Geometry.Parse("M39.2,13 V37.4")));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x3F, 0x8A, 0x2E)), ink,
                Geometry.Parse("M37.6,13.4 Q37.8,8.6 41.8,7.2 L43.4,9.4 Q40.6,10.6 41,13.4 Z")));
            var glow = new SolidColorBrush(SpaceTheme.Sun);
            g.Children.Add(new GeometryDrawing(glow, null, Geometry.Parse("M32.2,23.4 L35.4,18.6 L38,23.4 Z M40.6,23.4 L43.2,18.6 L46.4,23.4 Z")));
            g.Children.Add(new GeometryDrawing(glow, null, Geometry.Parse(
                "M31.4,27.4 Q39.2,34.6 47.2,27.4 L45.6,27.8 L44.6,29.6 L42.6,28.6 L39.4,30.6 L36.4,28.6 L34.2,29.8 L33.2,28 Z")));
        }
        g.Freeze();
        return g;
    }

    /// <summary>The decoration alone as an image (lay it over the logo), or null for an ordinary day.</summary>
    public static ImageSource? OverlayImage(Season season) =>
        Overlay(season) is { } overlay ? Frozen(new DrawingImage(overlay)) : null;

    /// <summary>A tray icon of the logo with the seasonal decoration. Zero on an ordinary day or on failure.</summary>
    public static IntPtr CreateTrayIcon(Season season, int size)
    {
        if (Overlay(season) is not { } overlay) return IntPtr.Zero;
        try
        {
            var logo = new BitmapImage(new Uri("pack://application:,,,/Assets/cometsnap-256.png"));
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(logo, new Rect(0, 0, size, size));
                dc.PushTransform(new ScaleTransform(size / 64.0, size / 64.0));
                dc.DrawDrawing(overlay);
                dc.Pop();
            }
            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            var straight = new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[size * size * 4];
            straight.CopyPixels(pixels, size * 4, 0);
            return RecordingIcons.CreateIcon(pixels, size);
        }
        catch (Exception ex)
        {
            Log.Error("Could not build the seasonal tray icon", ex);
            return IntPtr.Zero;
        }
    }

    private static ImageSource Frozen(DrawingImage image)
    {
        image.Freeze();
        return image;
    }
}
