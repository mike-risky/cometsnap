using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Cometsnap.App.Theme;

namespace Cometsnap.App.Tray;

/// <summary>
/// The flyout header: a soft night-sky gradient with twinkling stars, a floating comet logo,
/// and one shooting star each time the flyout opens. Animations only run while the flyout is
/// visible (Start/Stop) and are capped at 30 fps, so it stays light on the CPU.
/// </summary>
public sealed class SpaceHeader : Grid
{
    private const int FrameRate = 30;
    private readonly List<(IAnimatable Target, DependencyProperty Property)> _running = new();
    private readonly List<(Ellipse Star, double Low, double High, double Seconds, double Delay)> _stars = new();
    private readonly Image _logo;
    private readonly TranslateTransform _logoFloat = new();
    private readonly RotateTransform _logoTilt = new();
    private readonly ScaleTransform _logoPop = new(1, 1);
    private readonly Rectangle _shootingStar;
    private readonly TranslateTransform _shootingMove = new();
    private readonly double _width;

    public SpaceHeader(double width, double height)
    {
        _width = width;
        Height = height;
        ClipToBounds = true;

        // Night-sky gradient that melts into the flyout background.
        var sky = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        sky.GradientStops.Add(new GradientStop(Color.FromArgb(0xD0, 0x2B, 0x1C, 0x72), 0));     // violet
        sky.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, 0x22, 0x34, 0x8A), 0.55));  // navy glow
        sky.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x22, 0x34, 0x8A), 1));
        sky.Freeze();
        Children.Add(new Rectangle { Fill = sky, IsHitTestVisible = false });

        // Stars: fixed seed, so the sky looks the same every time.
        var sky2 = new Canvas { IsHitTestVisible = false };
        var rng = new Random(7);
        var tints = new[] { SpaceTheme.StarWhite, SpaceTheme.StarWhite, SpaceTheme.Dust, SpaceTheme.Sun };
        for (var i = 0; i < 26; i++)
        {
            var size = 1 + rng.NextDouble() * 1.6;
            var tint = tints[rng.Next(tints.Length)];
            var brush = new SolidColorBrush(tint);
            brush.Freeze();
            var star = new Ellipse { Width = size, Height = size, Fill = brush };
            // Keep the left side (logo and wordmark) a bit calmer: more stars to the right.
            var x = Math.Pow(rng.NextDouble(), 0.7) * (width - 6) + 3;
            var y = rng.NextDouble() * (height * 0.8) + 2;
            Canvas.SetLeft(star, x);
            Canvas.SetTop(star, y);
            var low = 0.12 + rng.NextDouble() * 0.2;
            var high = 0.55 + rng.NextDouble() * 0.4;
            star.Opacity = (low + high) / 2;
            sky2.Children.Add(star);
            _stars.Add((star, low, high, 1.4 + rng.NextDouble() * 2.2, rng.NextDouble() * 2));
        }

        // One shooting star per opening.
        var trail = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        trail.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0));
        trail.GradientStops.Add(new GradientStop(Color.FromArgb(0x90, 0xFF, 0xD6, 0x0A), 0.7));
        trail.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 1));
        trail.Freeze();
        _shootingStar = new Rectangle
        {
            Width = 54, Height = 1.6, RadiusX = 0.8, RadiusY = 0.8, Fill = trail, Opacity = 0,
            RenderTransform = new TransformGroup { Children = { new RotateTransform(16), _shootingMove } },
        };
        Canvas.SetLeft(_shootingStar, 0);
        Canvas.SetTop(_shootingStar, 0);
        sky2.Children.Add(_shootingStar);
        Children.Add(sky2);

        // Logo and wordmark.
        var word = new TextBlock { FontFamily = SpaceTheme.DisplayFont, FontSize = 28, VerticalAlignment = VerticalAlignment.Center };
        word.Inlines.Add(new System.Windows.Documents.Run("COMET") { Foreground = SpaceTheme.StarWhiteBrush });
        word.Inlines.Add(new System.Windows.Documents.Run("SNAP") { Foreground = SpaceTheme.SunBrush });
        _logo = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Assets/cometsnap-256.png")),
            Width = 36, Height = 36, Margin = new Thickness(0, 0, 10, 0),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { _logoPop, _logoTilt, _logoFloat } },
        };
        RenderOptions.SetBitmapScalingMode(_logo, BitmapScalingMode.HighQuality);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_logo);
        row.Children.Add(word);
        Children.Add(row);
    }

    /// <summary>Starts the sky. Call when the flyout appears.</summary>
    public void Start()
    {
        Stop();
        var sine = new SineEase { EasingMode = EasingMode.EaseInOut };
        sine.Freeze();

        foreach (var (star, low, high, seconds, delay) in _stars)
        {
            Run(star, UIElement.OpacityProperty, new DoubleAnimation(low, high, TimeSpan.FromSeconds(seconds))
            {
                BeginTime = TimeSpan.FromSeconds(delay), AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine,
            });
        }

        // The comet arrives with a small pop, then drifts gently.
        Run(_logoPop, ScaleTransform.ScaleXProperty, Pop());
        Run(_logoPop, ScaleTransform.ScaleYProperty, Pop());
        Run(_logoFloat, TranslateTransform.YProperty, new DoubleAnimation(-1.8, 1.8, TimeSpan.FromSeconds(2.6))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine,
        });
        Run(_logoFloat, TranslateTransform.XProperty, new DoubleAnimation(-0.8, 0.8, TimeSpan.FromSeconds(3.7))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine,
        });
        Run(_logoTilt, RotateTransform.AngleProperty, new DoubleAnimation(-3, 3, TimeSpan.FromSeconds(4.3))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine,
        });

        // Shooting star crosses the top right once, shortly after opening.
        var begin = TimeSpan.FromMilliseconds(280);
        var fly = TimeSpan.FromMilliseconds(750);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        Run(_shootingMove, TranslateTransform.XProperty, new DoubleAnimation(_width * 0.42, _width + 40, fly) { BeginTime = begin, EasingFunction = ease });
        Run(_shootingMove, TranslateTransform.YProperty, new DoubleAnimation(-14, Height * 0.75, fly) { BeginTime = begin, EasingFunction = ease });
        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = begin, Duration = fly };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, KeyTime.FromPercent(0.25)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.8, KeyTime.FromPercent(0.7)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        Run(_shootingStar, UIElement.OpacityProperty, fade);
    }

    /// <summary>Stops every animation, so a hidden flyout costs nothing.</summary>
    public void Stop()
    {
        foreach (var (target, property) in _running) target.BeginAnimation(property, null);
        _running.Clear();
        _shootingStar.Opacity = 0;
    }

    private static DoubleAnimation Pop() => new(0.82, 1, TimeSpan.FromMilliseconds(360))
    {
        EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 },
    };

    private void Run(IAnimatable target, DependencyProperty property, AnimationTimeline animation)
    {
        Timeline.SetDesiredFrameRate(animation, FrameRate);
        target.BeginAnimation(property, animation);
        _running.Add((target, property));
    }
}
