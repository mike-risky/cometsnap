using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Path = Avalonia.Controls.Shapes.Path;

namespace Cometsnap.Linux.Theme;

/// <summary>Small, discreet UI pieces shared by the flyout, the editor and the cards.</summary>
public static class Ui
{
    /// <summary>A 16×16 line icon from path data, scaled to <paramref name="size"/>.</summary>
    public static Control Icon(string data, double size = 20, IBrush? stroke = null, double thickness = 1.5, IBrush? fill = null) =>
        new Viewbox
        {
            Width = size,
            Height = size,
            Child = new Path
            {
                Data = Geometry.Parse(data),
                Stroke = stroke ?? SpaceTheme.StarWhiteBrush,
                Fill = fill,
                StrokeThickness = thickness,
                StrokeJoin = PenLineJoin.Round,
                StrokeLineCap = PenLineCap.Round,
                Width = 16,
                Height = 16,
                Stretch = Stretch.None,
            },
        };

    public static TextBlock Glyph(string text, double size = 20) => new()
    {
        Text = text,
        FontFamily = SpaceTheme.UiFont,
        FontWeight = FontWeight.Bold,
        FontSize = size * 0.95,
        Foreground = SpaceTheme.StarWhiteBrush,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Height = size,
        LineHeight = size,
        TextAlignment = TextAlignment.Center,
    };

    public static Control StepGlyph(double size = 20)
    {
        var g = new Grid { Width = size, Height = size };
        g.Children.Add(new Ellipse { Stroke = SpaceTheme.StarWhiteBrush, StrokeThickness = 1.6 });
        g.Children.Add(new TextBlock
        {
            Text = "1", FontWeight = FontWeight.Bold, FontSize = size * 0.58, Foreground = SpaceTheme.StarWhiteBrush,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        return g;
    }

    /// <summary>Background color animates on hover; a press gives a tiny bounce.</summary>
    public static Border Button(Control child, string tip, Action click, IBrush? background = null, IBrush? hover = null,
        Thickness? padding = null, double radius = 8)
    {
        var normal = background ?? Brushes.Transparent;
        var over = hover ?? SpaceTheme.HoverBrush;
        var scale = new ScaleTransform(1, 1);
        var b = new Border
        {
            Child = child,
            Background = normal,
            CornerRadius = new CornerRadius(radius),
            Padding = padding ?? new Thickness(8, 6),
            Cursor = new Cursor(StandardCursorType.Hand),
            RenderTransform = scale,
            RenderTransformOrigin = RelativePoint.Center,
            Transitions = new Transitions
            {
                new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(120) },
            },
        };
        ToolTip.SetTip(b, tip);
        var active = false;
        b.PointerEntered += (_, _) => { if (!active) b.Background = over; };
        b.PointerExited += (_, _) => { if (!active) b.Background = normal; scale.ScaleX = scale.ScaleY = 1; };
        b.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
            scale.ScaleX = scale.ScaleY = 0.94;
            e.Handled = true;
        };
        b.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            scale.ScaleX = scale.ScaleY = 1;
            e.Handled = true;
            if (b.IsPointerOver) click();
        };
        b.Tag = (Action<bool>)(on =>
        {
            active = on;
            b.Background = on ? SpaceTheme.CometBlueBrush : normal;
        });
        return b;
    }

    /// <summary>Marks a toggle button (tool, size) as active.</summary>
    public static void SetActive(Border button, bool on)
    {
        if (button.Tag is Action<bool> set) set(on);
    }

    /// <summary>An icon above a label, like the Windows editor rail.</summary>
    public static Border LabeledButton(Control icon, string label, string tip, Action click, double width = 64, double height = 54)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 3 };
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(icon);
        stack.Children.Add(new TextBlock
        {
            Text = label, FontSize = 11, Foreground = SpaceTheme.StarWhiteBrush, HorizontalAlignment = HorizontalAlignment.Center,
        });
        var b = Button(stack, tip, click, padding: new Thickness(2));
        b.Width = width;
        b.Height = height;
        b.Margin = new Thickness(0, 2);
        return b;
    }

    /// <summary>Primary (terracotta) or quiet action button with text and an optional icon.</summary>
    public static Border ActionButton(string text, string? icon, string tip, Action click, bool primary = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (icon is not null) row.Children.Add(Icon(icon, 16));
        row.Children.Add(new TextBlock
        {
            Text = text, FontWeight = FontWeight.SemiBold, FontSize = 13, Foreground = SpaceTheme.StarWhiteBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return Button(row, tip, click,
            primary ? SpaceTheme.TerracottaBrush : SpaceTheme.NavyBrush,
            primary ? new ImmutableSolidColorBrush(Color.FromRgb(0xF0, 0x6A, 0x4E)) : SpaceTheme.HoverBrush,
            new Thickness(12, 7, 14, 7));
    }

    public static Control Divider(bool vertical) => vertical
        ? new Border { Width = 1, Margin = new Thickness(10, 6), Background = SpaceTheme.NavyLightBrush }
        : new Border { Height = 1, Margin = new Thickness(6, 6), Background = SpaceTheme.NavyLightBrush };

    /// <summary>Discreet entrance: a short fade and lift.</summary>
    public static void Entrance(Control target, double lift = 8, int ms = 160)
    {
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(ms),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, lift) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) } },
            },
        };
        if (target.RenderTransform is not TranslateTransform) target.RenderTransform = new TranslateTransform();
        _ = anim.RunAsync(target);
    }
}
