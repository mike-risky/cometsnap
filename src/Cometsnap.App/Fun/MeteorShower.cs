using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Cometsnap.App.Theme;
using Cometsnap.Core.Native;

namespace Cometsnap.App.Fun;

/// <summary>
/// A short, click-through meteor shower with an optional caption. It never takes focus and
/// closes itself, so it can play over anything (milestones, the Konami code).
/// </summary>
public sealed class MeteorShower : Window
{
    private const int FrameRate = 60;

    private MeteorShower(Rect areaDip)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;
        Focusable = false;
        Left = areaDip.Left;
        Top = areaDip.Top;
        Width = areaDip.Width;
        Height = areaDip.Height;
        FontFamily = SpaceTheme.UiFont;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var ex = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
            SetWindowLong(hwnd, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_NOACTIVATE | 0x80 /* WS_EX_TOOLWINDOW */);
        };
    }

    /// <summary>Plays a shower over <paramref name="areaDip"/> and closes when done.</summary>
    public static void Play(Rect areaDip, int meteors, TimeSpan duration, string? title = null, string? subtitle = null, bool captionAtBottom = true)
    {
        var w = new MeteorShower(areaDip);
        var root = new Grid();
        var sky = new Canvas { IsHitTestVisible = false };
        root.Children.Add(sky);
        w.Content = root;

        var rng = new Random();
        var width = areaDip.Width;
        var height = areaDip.Height;
        for (var i = 0; i < meteors; i++)
        {
            var length = 36 + rng.NextDouble() * Math.Min(120, width * 0.25);
            var thick = 1.4 + rng.NextDouble() * 1.2;
            var golden = rng.NextDouble() < 0.35;
            var head = golden ? SpaceTheme.Sun : Colors.White;
            var trail = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            trail.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x4F, 0xC3, 0xFF), 0));
            trail.GradientStops.Add(new GradientStop(Color.FromArgb(0x90, golden ? (byte)0xFF : (byte)0x8F, golden ? (byte)0xB0 : (byte)0xD4, golden ? (byte)0x3A : (byte)0xFF), 0.7));
            trail.GradientStops.Add(new GradientStop(head, 1));
            trail.Freeze();

            var angle = 18 + rng.NextDouble() * 22;
            var rad = angle * Math.PI / 180;
            var move = new TranslateTransform();
            var meteor = new Rectangle
            {
                Width = length, Height = thick, RadiusX = thick / 2, RadiusY = thick / 2, Fill = trail, Opacity = 0,
                RenderTransform = new TransformGroup { Children = { new RotateTransform(angle), move } },
            };
            sky.Children.Add(meteor);

            var startX = -length + rng.NextDouble() * (width * 0.85);
            var startY = -20 + rng.NextDouble() * (height * 0.45);
            var travel = (0.35 + rng.NextDouble() * 0.4) * Math.Sqrt(width * width + height * height);
            var fly = TimeSpan.FromMilliseconds(520 + rng.NextDouble() * 420);
            var begin = TimeSpan.FromMilliseconds(rng.NextDouble() * Math.Max(1, duration.TotalMilliseconds - fly.TotalMilliseconds - 200));
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            ease.Freeze();
            Animate(move, TranslateTransform.XProperty, new DoubleAnimation(startX, startX + Math.Cos(rad) * travel, fly) { BeginTime = begin, EasingFunction = ease });
            Animate(move, TranslateTransform.YProperty, new DoubleAnimation(startY, startY + Math.Sin(rad) * travel, fly) { BeginTime = begin, EasingFunction = ease });
            var fade = new DoubleAnimationUsingKeyFrames { BeginTime = begin, Duration = fly };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, KeyTime.FromPercent(0.2)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.75, KeyTime.FromPercent(0.7)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            Animate(meteor, OpacityProperty, fade);
        }

        if (title is not null)
        {
            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = title, FontFamily = SpaceTheme.HeadingFont, FontSize = 18, Foreground = SpaceTheme.SunBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            if (subtitle is not null)
                text.Children.Add(new TextBlock
                {
                    Text = subtitle, FontSize = 12, Foreground = SpaceTheme.DustBrush, Margin = new Thickness(0, 2, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
            var pill = new Border
            {
                Child = text, Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xEE), BorderBrush = SpaceTheme.NavyLightBrush,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 9, 16, 10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = captionAtBottom ? VerticalAlignment.Bottom : VerticalAlignment.Center,
                Margin = new Thickness(12, 12, 12, 16), Opacity = 0,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 0, Opacity = 0.5, Color = Colors.Black },
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(0.9, 0.9),
            };
            root.Children.Add(pill);
            var show = new DoubleAnimationUsingKeyFrames { Duration = duration };
            show.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.1)));
            show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.82)));
            show.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            Animate(pill, OpacityProperty, show);
            var pop = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } };
            var scale = (ScaleTransform)pill.RenderTransform;
            Animate(scale, ScaleTransform.ScaleXProperty, pop);
            Animate(scale, ScaleTransform.ScaleYProperty, pop.Clone());
        }

        w.Show();
        var done = new DispatcherTimer { Interval = duration + TimeSpan.FromMilliseconds(150) };
        done.Tick += (_, _) => { done.Stop(); w.Close(); };
        done.Start();
    }

    private static void Animate(IAnimatable target, DependencyProperty property, AnimationTimeline animation)
    {
        Timeline.SetDesiredFrameRate(animation, FrameRate);
        target.BeginAnimation(property, animation);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
