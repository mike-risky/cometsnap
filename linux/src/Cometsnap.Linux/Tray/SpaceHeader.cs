using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Cometsnap.Linux.Theme;

namespace Cometsnap.Linux.Tray;

/// <summary>
/// The flyout header: a soft night-sky gradient with twinkling stars, a floating comet logo,
/// and one shooting star each time the flyout opens. Animations only run while the flyout is
/// visible (Start/Stop) and are capped at 30 fps, so it stays light on the CPU.
/// </summary>
public sealed class SpaceHeader : Control
{
    private readonly record struct Star(double X, double Y, double Size, Color Tint, double Low, double High, double Seconds, double Delay);

    private readonly List<Star> _stars = new();
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private readonly IBrush _sky;
    private readonly FormattedText _comet;
    private readonly FormattedText _snap;

    public SpaceHeader(double width, double height)
    {
        Width = width;
        Height = height;
        ClipToBounds = true;
        IsHitTestVisible = false;

        _sky = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0xD0, 0x2B, 0x1C, 0x72), 0),     // violet
                new GradientStop(Color.FromArgb(0x70, 0x22, 0x34, 0x8A), 0.55),  // navy glow
                new GradientStop(Color.FromArgb(0x00, 0x22, 0x34, 0x8A), 1),
            },
        }.ToImmutable();

        // Fixed seed: the sky looks the same every time. More stars to the right, calmer behind the logo.
        var rng = new Random(7);
        var tints = new[] { SpaceTheme.StarWhite, SpaceTheme.StarWhite, SpaceTheme.Dust, SpaceTheme.Sun };
        for (var i = 0; i < 26; i++)
        {
            var size = 1 + rng.NextDouble() * 1.6;
            var tint = tints[rng.Next(tints.Length)];
            var x = Math.Pow(rng.NextDouble(), 0.7) * (width - 6) + 3;
            var y = rng.NextDouble() * (height * 0.8) + 2;
            var low = 0.12 + rng.NextDouble() * 0.2;
            var high = 0.55 + rng.NextDouble() * 0.4;
            _stars.Add(new Star(x, y, size, tint, low, high, 1.4 + rng.NextDouble() * 2.2, rng.NextDouble() * 2));
        }

        var display = new Typeface(SpaceTheme.DisplayFont);
        _comet = new FormattedText("COMET", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, display, 28, SpaceTheme.StarWhiteBrush);
        _snap = new FormattedText("SNAP", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, display, 28, SpaceTheme.SunBrush);

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / 30), DispatcherPriority.Render, (_, _) => InvalidateVisual());
    }

    /// <summary>Starts the sky. Call when the flyout appears.</summary>
    public void Start()
    {
        _clock.Restart();
        _timer.Start();
        InvalidateVisual();
    }

    /// <summary>Stops every animation, so a hidden flyout costs nothing.</summary>
    public void Stop()
    {
        _timer.Stop();
        _clock.Stop();
    }

    private static double EaseInOut(double t) => -(Math.Cos(Math.PI * t) - 1) / 2;

    /// <summary>A sine wave between a and b with the given period, like an auto-reversing animation.</summary>
    private static double Swing(double t, double a, double b, double seconds)
    {
        var phase = t / seconds % 2;
        var k = phase < 1 ? phase : 2 - phase;
        return a + (b - a) * EaseInOut(k);
    }

    public override void Render(DrawingContext dc)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var t = _clock.Elapsed.TotalSeconds;
        dc.FillRectangle(_sky, new Rect(0, 0, w, h));

        foreach (var s in _stars)
        {
            var o = t < s.Delay ? (s.Low + s.High) / 2 : Swing(t - s.Delay, s.Low, s.High, s.Seconds);
            var brush = new ImmutableSolidColorBrush(s.Tint, o);
            dc.DrawEllipse(brush, null, new Point(s.X + s.Size / 2, s.Y + s.Size / 2), s.Size / 2, s.Size / 2);
        }

        // One shooting star crosses the top right, shortly after opening.
        const double begin = 0.28, fly = 0.75;
        if (t > begin && t < begin + fly)
        {
            var k = (t - begin) / fly;
            var e = k * k * k; // ease in
            var x = w * 0.42 + (w + 40 - w * 0.42) * e;
            var y = -14 + (h * 0.75 + 14) * e;
            var alpha = k < 0.25 ? k / 0.25 * 0.95 : k < 0.7 ? 0.95 - (k - 0.25) / 0.45 * 0.15 : 0.8 * (1 - (k - 0.7) / 0.3);
            var trail = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(0x90, 0xFF, 0xD6, 0x0A), 0.7),
                    new GradientStop(Colors.White, 1),
                },
            };
            using (dc.PushOpacity(alpha))
            using (dc.PushTransform(Matrix.CreateRotation(Math.PI * 16 / 180) * Matrix.CreateTranslation(x, y)))
                dc.DrawRectangle(trail, null, new Rect(0, 0, 54, 1.6), 0.8, 0.8);
        }

        // The comet arrives with a small pop, then drifts and tilts gently.
        var pop = t < 0.36 ? 0.82 + 0.18 * BackOut(t / 0.36) : 1;
        var floatY = Swing(t, -1.8, 1.8, 2.6);
        var floatX = Swing(t, -0.8, 0.8, 3.7);
        var tilt = Swing(t, -3, 3, 4.3);
        const double logo = 36, left = 16;
        var cy = h / 2;
        var center = new Point(left + logo / 2, cy);
        var m = Matrix.CreateTranslation(-center.X, -center.Y)
                * Matrix.CreateScale(pop, pop)
                * Matrix.CreateRotation(tilt * Math.PI / 180)
                * Matrix.CreateTranslation(center.X + floatX, center.Y + floatY);
        using (dc.PushTransform(m))
        using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            dc.DrawImage(SpaceTheme.Logo, new Rect(left, cy - logo / 2, logo, logo));

        var textY = cy - _comet.Height / 2;
        dc.DrawText(_comet, new Point(left + logo + 10, textY));
        dc.DrawText(_snap, new Point(left + logo + 10 + _comet.WidthIncludingTrailingWhitespace, textY));
    }

    private static double BackOut(double t)
    {
        const double s = 1.2;
        t -= 1;
        return t * t * ((s + 1) * t + s) + 1;
    }
}
