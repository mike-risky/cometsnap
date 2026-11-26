using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Cometsnap.Linux.Platform;
using Cometsnap.Linux.Theme;

namespace Cometsnap.Linux.Fun;

/// <summary>
/// A short, click-through meteor shower with an optional caption. It never takes the focus and
/// closes itself, so it can play over anything (milestones, the Konami code). Needs a compositor:
/// without one there is no transparency, and the caller's fallback (a toast) runs instead.
/// </summary>
public sealed class MeteorShower : Window
{
    private readonly Sky _sky;
    private readonly DispatcherTimer _timer;
    private readonly Action? _fallback;

    private MeteorShower(Sky sky, Action? fallback)
    {
        _sky = sky;
        _fallback = fallback;
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
        Content = sky;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / 60), DispatcherPriority.Render, (_, _) =>
        {
            if (_sky.Elapsed > _sky.Duration + 0.15) Close();
            else _sky.InvalidateVisual();
        });
        Opened += (_, _) =>
        {
            if (ActualTransparencyLevel == WindowTransparencyLevel.None)
            {
                Close();
                _fallback?.Invoke();
                return;
            }
            X11.MakeClickThrough(this);
            _sky.Begin();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>Plays a shower over <paramref name="area"/> (desktop pixels) and closes when done.</summary>
    public static void Play(PixelRect area, double scaling, int meteors, TimeSpan duration, string? title = null, string? subtitle = null,
        bool captionAtBottom = true, Action? fallback = null)
    {
        try
        {
            var size = new Size(area.Width / scaling, area.Height / scaling);
            var sky = new Sky(size, meteors, duration.TotalSeconds, title, subtitle, captionAtBottom);
            var w = new MeteorShower(sky, fallback)
            {
                Width = size.Width, Height = size.Height, Position = area.Position,
            };
            w.Show();
        }
        catch (Exception ex)
        {
            Core.Log.Error("Meteor shower failed", ex);
            fallback?.Invoke();
        }
    }

    private sealed class Sky : Control
    {
        private readonly record struct Meteor(double Length, double Thick, IBrush Trail, double Angle, double StartX, double StartY,
            double Travel, double Begin, double Fly);

        private readonly List<Meteor> _meteors = new();
        private readonly Stopwatch _clock = new();
        private readonly FormattedText? _title;
        private readonly FormattedText? _subtitle;
        private readonly bool _bottom;

        public double Duration { get; }
        public double Elapsed => _clock.Elapsed.TotalSeconds;

        public Sky(Size size, int count, double duration, string? title, string? subtitle, bool bottom)
        {
            Duration = duration;
            _bottom = bottom;
            IsHitTestVisible = false;
            var rng = new Random();
            var (width, height) = (size.Width, size.Height);
            for (var i = 0; i < count; i++)
            {
                var length = 36 + rng.NextDouble() * Math.Min(120, width * 0.25);
                var thick = 1.4 + rng.NextDouble() * 1.2;
                var golden = rng.NextDouble() < 0.35;
                var head = golden ? SpaceTheme.Sun : Colors.White;
                var trail = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0, 0x4F, 0xC3, 0xFF), 0),
                        new GradientStop(golden ? Color.FromArgb(0x90, 0xFF, 0xB0, 0x3A) : Color.FromArgb(0x90, 0x8F, 0xD4, 0xFF), 0.7),
                        new GradientStop(head, 1),
                    },
                }.ToImmutable();
                var fly = 0.52 + rng.NextDouble() * 0.42;
                _meteors.Add(new Meteor(length, thick, trail, (18 + rng.NextDouble() * 22) * Math.PI / 180,
                    -length + rng.NextDouble() * (width * 0.85), -20 + rng.NextDouble() * (height * 0.45),
                    (0.35 + rng.NextDouble() * 0.4) * Math.Sqrt(width * width + height * height),
                    rng.NextDouble() * Math.Max(0.001, duration - fly - 0.2), fly));
            }
            if (title is not null)
                _title = new FormattedText(title, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(SpaceTheme.HeadingFont), 18, SpaceTheme.SunBrush);
            if (subtitle is not null)
                _subtitle = new FormattedText(subtitle, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(SpaceTheme.UiFont), 12, SpaceTheme.DustBrush);
        }

        public void Begin() => _clock.Restart();

        private static double MeteorOpacity(double k) =>
            k < 0.2 ? k / 0.2 * 0.95 : k < 0.7 ? 0.95 - (k - 0.2) / 0.5 * 0.2 : 0.75 * (1 - (k - 0.7) / 0.3);

        public override void Render(DrawingContext dc)
        {
            if (!_clock.IsRunning) return;
            var t = Elapsed;
            foreach (var m in _meteors)
            {
                var k = (t - m.Begin) / m.Fly;
                if (k is <= 0 or >= 1) continue;
                var e = k * k; // quadratic ease-in
                var x = m.StartX + Math.Cos(m.Angle) * m.Travel * e;
                var y = m.StartY + Math.Sin(m.Angle) * m.Travel * e;
                using (dc.PushOpacity(MeteorOpacity(k)))
                using (dc.PushTransform(Matrix.CreateRotation(m.Angle) * Matrix.CreateTranslation(x, y)))
                    dc.DrawRectangle(m.Trail, null, new Rect(0, 0, m.Length, m.Thick), m.Thick / 2, m.Thick / 2);
            }

            if (_title is null) return;
            var p = t / Duration;
            var opacity = p < 0.1 ? p / 0.1 : p < 0.82 ? 1 : Math.Max(0, 1 - (p - 0.82) / 0.18);
            var pop = t < 0.32 ? 0.9 + 0.1 * BackOut(t / 0.32) : 1;
            var textW = Math.Max(_title.Width, _subtitle?.Width ?? 0);
            var textH = _title.Height + (_subtitle is null ? 0 : _subtitle.Height + 2);
            var pill = new Size(textW + 32, textH + 19);
            var w = Bounds.Width;
            var h = Bounds.Height;
            var left = (w - pill.Width) / 2;
            var top = _bottom ? h - 16 - pill.Height : (h - pill.Height) / 2;
            var rect = new Rect(left, top, pill.Width, pill.Height);
            var c = rect.Center;
            using (dc.PushOpacity(opacity))
            using (dc.PushTransform(Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateScale(pop, pop) * Matrix.CreateTranslation(c.X, c.Y)))
            {
                dc.DrawRectangle(SpaceTheme.WithAlpha(SpaceTheme.Void, 0xEE), new ImmutablePen(SpaceTheme.NavyLightBrush, 1),
                    new RoundedRect(rect, 12), new BoxShadows(new BoxShadow { Blur = 16, Color = Color.FromArgb(0x80, 0, 0, 0) }));
                dc.DrawText(_title, new Point(left + (pill.Width - _title.Width) / 2, top + 9));
                if (_subtitle is not null)
                    dc.DrawText(_subtitle, new Point(left + (pill.Width - _subtitle.Width) / 2, top + 9 + _title.Height + 2));
            }
        }

        private static double BackOut(double t)
        {
            const double s = 0.8;
            t -= 1;
            return t * t * ((s + 1) * t + s) + 1;
        }
    }
}
