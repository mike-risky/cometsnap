using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Cometsnap.Linux.Platform;
using Cometsnap.Linux.Theme;

namespace Cometsnap.Linux.Fun;

/// <summary>
/// The little celebration after a capture: the capture itself, as a small card, flies from the
/// captured area towards the tray, pulled by a comet. A tiny click-through window moved along a
/// curve; it never takes the focus. Skipped without a compositor (it would be a black square).
/// </summary>
public sealed class CometFlight : Window
{
    private const double SizeDip = 170;
    private const double ThumbMax = 84;
    private const double Seconds = 0.62;

    private readonly Comet _comet = new();
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private Point _from, _to, _control;
    private double _scaling = 1;
    private bool _noTransparency;

    public CometFlight()
    {
        Title = "Cometsnap";
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None };
        Background = Brushes.Transparent;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        Width = Height = SizeDip;
        Position = new PixelPoint(-32000, -32000);
        Content = _comet;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / 60), DispatcherPriority.Render, (_, _) => OnFrame());
        Opened += (_, _) =>
        {
            if (ActualTransparencyLevel == WindowTransparencyLevel.None)
            {
                _noTransparency = true;
                Stop();
                return;
            }
            X11.MakeClickThrough(this);
        };
    }

    /// <summary>Fly from one desktop pixel point to another, carrying <paramref name="image"/> if given.</summary>
    public void Launch(PixelPoint from, PixelPoint to, double scaling, Bitmap? image)
    {
        if (_noTransparency) return;
        _comet.Thumb = image;
        _scaling = scaling;
        _from = new Point(from.X, from.Y);
        _to = new Point(to.X, to.Y);
        // Arc a little, like a thrown comet: upwards, or downwards when the tray is at the top.
        var mid = new Point((_from.X + _to.X) / 2, (_from.Y + _to.Y) / 2);
        var lift = Math.Min(220 * scaling, Point.Distance(_from, _to) * 0.35);
        _control = _to.Y < _from.Y ? new Point(mid.X, Math.Max(_from.Y, _to.Y) + lift * 0.4) : new Point(mid.X, Math.Min(_from.Y, _to.Y) - lift);
        Opacity = 1;
        _clock.Restart();
        Move(0);
        if (!IsVisible) Show();
        _timer.Start();
    }

    private void OnFrame()
    {
        var t = _clock.Elapsed.TotalSeconds / Seconds;
        if (t >= 1)
        {
            Stop();
            return;
        }
        var eased = t * t * (3 - 2 * t);
        Move(eased);
        Opacity = t > 0.8 ? (1 - t) / 0.2 : 1;
    }

    private void Stop()
    {
        _timer.Stop();
        _clock.Stop();
        _comet.Thumb = null; // don't keep the capture alive
        if (IsVisible) Hide();
    }

    private void Move(double t)
    {
        var ahead = Bezier(Math.Min(1, t + 0.02));
        var behind = Bezier(Math.Max(0, t - 0.02));
        _comet.Angle = Math.Atan2(ahead.Y - behind.Y, ahead.X - behind.X);
        _comet.Scale = 1 - 0.45 * t;
        _comet.ThumbScale = 1 - 0.85 * t * t;
        _comet.InvalidateVisual();
        var p = Bezier(t);
        var half = SizeDip * _scaling / 2;
        Position = new PixelPoint((int)(p.X - half), (int)(p.Y - half));
    }

    private Point Bezier(double t)
    {
        var u = 1 - t;
        return new Point(
            u * u * _from.X + 2 * u * t * _control.X + t * t * _to.X,
            u * u * _from.Y + 2 * u * t * _control.Y + t * t * _to.Y);
    }

    private sealed class Comet : Control
    {
        private static readonly IBrush Tail = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0, 79, 195, 255), 0), new GradientStop(Color.FromArgb(200, 120, 200, 255), 1) },
        }.ToImmutable();

        private static readonly IBrush Glow = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Color.FromArgb(170, 120, 200, 255), 0), new GradientStop(Color.FromArgb(0, 79, 195, 255), 1) },
        }.ToImmutable();

        private static readonly Geometry TailShape = Geometry.Parse("M0,-9 L-80,-2 L-80,2 L0,9 Z");

        public Bitmap? Thumb { get; set; }
        public double Angle { get; set; }
        public double Scale { get; set; } = 1;
        public double ThumbScale { get; set; } = 1;

        public Comet() => IsHitTestVisible = false;

        public override void Render(DrawingContext dc)
        {
            var c = new Point(SizeDip / 2, SizeDip / 2);
            using (dc.PushTransform(Matrix.CreateScale(Scale, Scale) * Matrix.CreateRotation(Angle) * Matrix.CreateTranslation(c.X, c.Y)))
            {
                dc.DrawGeometry(Tail, null, TailShape);
                dc.DrawEllipse(Glow, null, new Point(0, 0), 17, 17);
                dc.DrawEllipse(SpaceTheme.StarWhiteBrush, null, new Point(0, 0), 7, 7);
            }
            if (Thumb is not { } img) return;
            var px = img.PixelSize;
            var s = ThumbMax / Math.Max(1, Math.Max(px.Width, px.Height));
            var w = Math.Max(12, px.Width * s) * ThumbScale;
            var h = Math.Max(12, px.Height * s) * ThumbScale;
            var frame = new Rect(c.X - w / 2 - 2, c.Y - h / 2 - 2, w + 4, h + 4);
            dc.DrawRectangle(SpaceTheme.StarWhiteBrush, null, new RoundedRect(frame, 4));
            using (dc.PushClip(new RoundedRect(frame.Deflate(2), 2)))
            using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.MediumQuality }))
                dc.DrawImage(img, frame.Deflate(2));
        }
    }
}
