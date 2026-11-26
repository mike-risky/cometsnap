using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Cometsnap.App.Theme;
using Path = System.Windows.Shapes.Path;

namespace Cometsnap.App.Capture;

public enum AnnotationTool { None, Ellipse, Arrow, Text, Rectangle }

/// <summary>
/// The little toolbar next to a selection: annotate right on the frozen screen, then copy or save.
/// Authored in DIPs and scaled with a LayoutTransform, because the overlay canvas is in physical pixels.
/// </summary>
public sealed class QuickBar : Border
{
    public static readonly string[] Palette = { "#E5484D", "#FFD60A", "#2ED573", "#3D6BFF", "#FFFFFF", "#111111" };

    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Dictionary<AnnotationTool, Border> _toolButtons = new();
    private readonly Dictionary<string, Border> _swatches = new();
    private AnnotationTool _tool;
    private string _color = Palette[0];

    public event Action<AnnotationTool>? ToolSelected;
    public event Action<string>? ColorSelected;
    public event Action? UndoClicked;
    public event Action? CopyClicked;
    public event Action? SaveClicked;
    public event Action? CloseClicked;

    public QuickBar()
    {
        LayoutTransform = _scale;
        Background = new SolidColorBrush(Color.FromArgb(0xF2, SpaceTheme.Void.R, SpaceTheme.Void.G, SpaceTheme.Void.B));
        BorderBrush = SpaceTheme.NavyLightBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(4);

        var row = new StackPanel { Orientation = Orientation.Horizontal };

        AddTool(row, AnnotationTool.Ellipse, Icon("M8,2.5 A5.5,5.5 0 1 1 7.99,2.5 Z"), "Circle (E) · Shift = perfect circle");
        AddTool(row, AnnotationTool.Arrow, Icon("M3,13 L13,3 M7.5,3 L13,3 L13,8.5"), "Arrow (A) · Shift = 45° steps");
        AddTool(row, AnnotationTool.Text, new TextBlock
        {
            Text = "T", FontFamily = SpaceTheme.UiFont, FontWeight = FontWeights.Bold, FontSize = 16,
            Foreground = SpaceTheme.StarWhiteBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        }, "Text (T) · Enter to finish, Shift+Enter new line");
        AddTool(row, AnnotationTool.Rectangle, Icon("M2.5,3.5 H13.5 V12.5 H2.5 Z"), "Rectangle (R) · Shift = square");

        row.Children.Add(Divider());
        for (var i = 0; i < Palette.Length; i++) AddSwatch(row, Palette[i], i + 1);
        row.Children.Add(Divider());

        row.Children.Add(Button(Icon("M5.5,4 L2.5,7 L5.5,10 M2.5,7 H10 A3.5,3.5 0 0 1 10,14 H7"), "Undo (Ctrl+Z)", () => UndoClicked?.Invoke()));
        row.Children.Add(Divider());
        row.Children.Add(Button(Icon("M5.5,5.5 H13.5 V13.5 H5.5 Z M2.5,10.5 V2.5 H10.5"), "Copy (Enter)", () => CopyClicked?.Invoke(), accent: true));
        row.Children.Add(Button(Icon("M8,2.5 V10 M4.5,6.5 L8,10 L11.5,6.5 M2.5,13.5 H13.5"), "Save as… (Ctrl+S)", () => SaveClicked?.Invoke()));
        row.Children.Add(Button(Icon("M4,4 L12,12 M12,4 L4,12"), "Close (Esc)", () => CloseClicked?.Invoke()));

        Child = row;
        SetActive(AnnotationTool.None, Palette[0]);
    }

    public void Configure(double dpiScale)
    {
        _scale.ScaleX = dpiScale;
        _scale.ScaleY = dpiScale;
    }

    public void SetActive(AnnotationTool tool, string color)
    {
        _tool = tool;
        _color = color;
        foreach (var (t, b) in _toolButtons)
            b.Background = t == tool ? ActiveBrush : Brushes.Transparent;
        foreach (var (c, b) in _swatches)
            b.BorderBrush = string.Equals(c, color, StringComparison.OrdinalIgnoreCase) ? SpaceTheme.StarWhiteBrush : Brushes.Transparent;
    }

    private static readonly SolidColorBrush ActiveBrush = Frozen(Color.FromArgb(0x66, SpaceTheme.CometBlue.R, SpaceTheme.CometBlue.G, SpaceTheme.CometBlue.B));
    private static readonly SolidColorBrush HoverBrush = Frozen(Color.FromArgb(0xFF, SpaceTheme.NavyLight.R, SpaceTheme.NavyLight.G, SpaceTheme.NavyLight.B));
    private static readonly SolidColorBrush AccentBrush = SpaceTheme.TerracottaBrush;

    private void AddTool(Panel row, AnnotationTool tool, UIElement icon, string tip)
    {
        var b = Button(icon, tip, () =>
        {
            var next = _tool == tool ? AnnotationTool.None : tool;
            ToolSelected?.Invoke(next);
        });
        _toolButtons[tool] = b;
        row.Children.Add(b);
    }

    private void AddSwatch(Panel row, string color, int number)
    {
        var dot = new Border
        {
            Width = 16, Height = 16, CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
            BorderBrush = SpaceTheme.DustBrush, BorderThickness = new Thickness(1),
        };
        var ring = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent, Child = dot, Margin = new Thickness(2, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, ToolTip = $"Color {number}",
            Background = Brushes.Transparent,
        };
        ring.MouseLeftButtonDown += (_, e) => e.Handled = true;
        ring.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ColorSelected?.Invoke(color);
        };
        _swatches[color] = ring;
        row.Children.Add(ring);
    }

    private Border Button(UIElement icon, string tip, Action click, bool accent = false)
    {
        var normal = accent ? AccentBrush : Brushes.Transparent;
        var b = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(7), Child = icon,
            Background = normal, ToolTip = tip, Cursor = Cursors.Hand, Margin = new Thickness(1, 0, 1, 0),
        };
        b.MouseEnter += (_, _) => { if (!IsActiveTool(b)) b.Background = HoverBrush; };
        b.MouseLeave += (_, _) => { if (!IsActiveTool(b)) b.Background = normal; };
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return b;
    }

    private bool IsActiveTool(Border b) => _toolButtons.TryGetValue(_tool, out var active) && active == b;

    private static Path Icon(string data) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = SpaceTheme.StarWhiteBrush,
        StrokeThickness = 1.6,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        Width = 16,
        Height = 16,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Border Divider() => new()
    {
        Width = 1, Margin = new Thickness(5, 6, 5, 6), Background = SpaceTheme.NavyLightBrush,
    };

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
