using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Cometsnap.App.Theme;
using Path = System.Windows.Shapes.Path;
using Cometsnap.Core.Localization;

namespace Cometsnap.App.Editing;

public enum EditorTool { Select, Arrow, Line, Rectangle, Ellipse, Text, Callout, Highlighter, Pen, Step, Blur, Spotlight, Crop }

public sealed record ToolInfo(EditorTool Tool, string Label, Key Key, string Tip, string? Icon, string? Glyph = null);

/// <summary>Tool definitions and the small, discreet UI pieces of the editor (buttons, animations).</summary>
public static class EditorUi
{
    /// <summary>The tools, labelled in the current language.</summary>
    public static ToolInfo[] Tools => new ToolInfo[]
    {
        new(EditorTool.Select, L.T("Select"), Key.V, L.T("Select (V): click a shape to move, recolor or delete it"),
            "M3.5,2 V13 L6.3,10.4 L8.6,14.6 L10.6,13.6 L8.3,9.4 H12 Z"),
        new(EditorTool.Arrow, L.T("Arrow"), Key.A, L.T("Arrow (A) · Shift = 45° steps"), "M3,13 L13,3 M7.5,3 L13,3 L13,8.5"),
        new(EditorTool.Line, L.T("Line"), Key.L, L.T("Line (L) · Shift = 45° steps"), "M3,13 L13,3"),
        new(EditorTool.Rectangle, L.T("Box"), Key.R, L.T("Rectangle (R) · Shift = square"), "M2.5,3.5 H13.5 V12.5 H2.5 Z"),
        new(EditorTool.Ellipse, L.T("Circle"), Key.E, L.T("Circle (E) · Shift = perfect circle"), "M8,2.5 A5.5,5.5 0 1 1 7.99,2.5 Z"),
        new(EditorTool.Text, L.T("Text"), Key.T, L.T("Text (T) · Enter to finish, Shift+Enter new line"), null, "T"),
        new(EditorTool.Callout, L.T("Bubble"), Key.K, L.T("Speech bubble (K): drag from what you point at to where the bubble goes"),
            "M2.5,3 H13.5 V10 H7 L4,13.5 V10 H2.5 Z"),
        new(EditorTool.Highlighter, L.T("Marker"), Key.H, L.T("Highlighter (H)"), "M9.5,2.5 L13.5,6.5 L7.5,12.5 H3.5 V8.5 Z M2,14.5 H14"),
        new(EditorTool.Pen, L.T("Pen"), Key.P, L.T("Freehand pen (P)"), "M10.5,2.5 L13.5,5.5 L5.5,13.5 H2.5 V10.5 Z"),
        new(EditorTool.Step, L.T("Steps"), Key.N, L.T("Numbered steps (N): each click adds the next number"), null, "1"),
        new(EditorTool.Blur, L.T("Blur"), Key.B, L.T("Blur / pixelate (B): hide passwords, e-mails, faces"),
            "M2.5,2.5 H6 V6 H2.5 Z M10,2.5 H13.5 V6 H10 Z M6.25,6.25 H9.75 V9.75 H6.25 Z M2.5,10 H6 V13.5 H2.5 Z M10,10 H13.5 V13.5 H10 Z"),
        new(EditorTool.Spotlight, L.T("Spotlight"), Key.S, L.T("Spotlight (S): greys out everything except the area you drag"),
            "M8,1.5 V3 M2,7.5 H3.5 M12.5,7.5 H14 M3.8,3.3 L4.8,4.3 M12.2,3.3 L11.2,4.3 M6,11.5 A3.6,3.6 0 1 1 10,11.5 V13.5 H6 Z"),
        new(EditorTool.Crop, L.T("Crop"), Key.C, L.T("Crop (C): keep only the area you drag"), "M4.5,1.5 V11.5 H14.5 M1.5,4.5 H11.5 V14.5"),
    };

    public static readonly string[] Palette = { "#E5484D", "#F09625", "#FFD60A", "#2ED573", "#3D6BFF", "#A55EEA", "#FFFFFF", "#111111" };
    public static string[] PaletteNames => new[] { L.T("Red"), L.T("Orange"), L.T("Yellow"), L.T("Green"), L.T("Blue"), L.T("Purple"), L.T("White"), L.T("Black") };

    public static readonly SolidColorBrush ActiveBrush = Frozen(SpaceTheme.CometBlue);
    public static readonly SolidColorBrush HoverBrush = Frozen(Color.FromRgb(0x1A, 0x2A, 0x70));

    public static UIElement Icon(string? data, string? glyph, double size = 20)
    {
        if (glyph is not null)
        {
            if (glyph == "1")
            {
                var g = new Grid { Width = size, Height = size };
                g.Children.Add(new System.Windows.Shapes.Ellipse { Stroke = SpaceTheme.StarWhiteBrush, StrokeThickness = 1.6 });
                g.Children.Add(new TextBlock { Text = "1", FontWeight = FontWeights.Bold, FontSize = size * 0.6, Foreground = SpaceTheme.StarWhiteBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                return g;
            }
            return new TextBlock
            {
                Text = glyph, FontFamily = SpaceTheme.UiFont, FontWeight = FontWeights.Bold, FontSize = size,
                Foreground = SpaceTheme.StarWhiteBrush, HorizontalAlignment = HorizontalAlignment.Center, Height = size, LineHeight = size,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextAlignment = TextAlignment.Center,
            };
        }
        return new Viewbox
        {
            Width = size, Height = size,
            Child = new Path
            {
                Data = Geometry.Parse(data!), Stroke = SpaceTheme.StarWhiteBrush, StrokeThickness = 1.5,
                StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Width = 16, Height = 16, Stretch = Stretch.None,
            },
        };
    }

    /// <summary>A button with an icon above a label. Background color animates; a press gives a tiny bounce.</summary>
    public static Border LabeledButton(UIElement icon, string label, string tip, Action click, double width = 64, double height = 54)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(icon);
        stack.Children.Add(new TextBlock
        {
            Text = label, FontSize = 11, Foreground = SpaceTheme.StarWhiteBrush, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        });
        var b = new Border
        {
            Width = width, Height = height, CornerRadius = new CornerRadius(10), Child = stack, ToolTip = tip,
            Cursor = Cursors.Hand, Margin = new Thickness(0, 2, 0, 2), Background = new SolidColorBrush(Colors.Transparent),
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1),
        };
        Wire(b, click);
        return b;
    }

    /// <summary>A pill button with an icon and a label side by side, for the top bar.</summary>
    public static Border PillButton(UIElement icon, string label, string tip, Action click, Brush? fill = null, bool bold = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(icon);
        row.Children.Add(new TextBlock
        {
            Text = label, FontSize = 13, Foreground = SpaceTheme.StarWhiteBrush, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 0, 0), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        });
        var b = new Border
        {
            Height = 36, Padding = new Thickness(12, 0, 14, 0), CornerRadius = new CornerRadius(9), Child = row, ToolTip = tip,
            Cursor = Cursors.Hand, Margin = new Thickness(4, 0, 4, 0),
            Background = fill is SolidColorBrush s ? new SolidColorBrush(s.Color) : new SolidColorBrush(Colors.Transparent),
            BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(fill is null ? 1 : 0),
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1),
            Tag = fill is SolidColorBrush f ? f.Color : Colors.Transparent,
        };
        Wire(b, click);
        return b;
    }

    private static void Wire(Border b, Action click)
    {
        b.MouseEnter += (_, _) => { if (b.Tag is not true) AnimateBackground(b, HoverBrush.Color, hover: true); };
        b.MouseLeave += (_, _) => { if (b.Tag is not true) AnimateBackground(b, RestColor(b), hover: false); };
        b.MouseLeftButtonDown += (_, e) => { e.Handled = true; Press(b); };
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
    }

    private static Color RestColor(Border b) => b.Tag is Color c ? c : Colors.Transparent;

    /// <summary>Marks a tool button as the active one (Tag = true) with a quick color fade.</summary>
    public static void SetActive(Border b, bool active)
    {
        b.Tag = active ? true : null;
        AnimateBackground(b, active ? ActiveBrush.Color : Colors.Transparent, hover: false);
    }

    public static void AnimateBackground(Border b, Color to, bool hover)
    {
        var current = b.Background as SolidColorBrush;
        var brush = current;
        if (brush is null || brush.IsFrozen)
        {
            brush = new SolidColorBrush(current?.Color ?? Colors.Transparent);
            b.Background = brush;
        }
        // Hover over a colored (e.g. terracotta) button: just brighten it a bit.
        if (hover && b.Tag is Color rest && rest != Colors.Transparent)
            to = Color.FromRgb((byte)Math.Min(255, rest.R + 24), (byte)Math.Min(255, rest.G + 24), (byte)Math.Min(255, rest.B + 24));
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(120)));
    }

    /// <summary>A tiny press bounce: 94% and back, 160 ms.</summary>
    public static void Press(FrameworkElement e)
    {
        if (e.RenderTransform is not ScaleTransform st || st.IsFrozen)
        {
            st = new ScaleTransform(1, 1);
            e.RenderTransformOrigin = new Point(0.5, 0.5);
            e.RenderTransform = st;
        }
        var anim = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    /// <summary>Fades and lifts an element in (used when windows open).</summary>
    public static void Entrance(FrameworkElement e, double lift = 8, int ms = 170)
    {
        var tt = new TranslateTransform(0, lift);
        e.RenderTransform = tt;
        e.Opacity = 0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        e.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(lift, 0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
    }

    public static Border Divider(bool vertical) => vertical
        ? new Border { Width = 1, Margin = new Thickness(8, 8, 8, 8), Background = SpaceTheme.NavyLightBrush }
        : new Border { Height = 1, Margin = new Thickness(10, 5, 10, 5), Background = SpaceTheme.NavyLightBrush };

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
