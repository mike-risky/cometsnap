using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Cometsnap.App.Theme;

namespace Cometsnap.App.Capture;

/// <summary>
/// Pixel-perfect magnifier: 15 × 15 real pixels around the cursor, nearest-neighbour zoom,
/// pixel grid, crosshair, plus X/Y, selection size and the hex color under the cursor.
/// Reads straight from the frozen screen bitmap, so it costs no extra capture.
/// All sizes are physical pixels (the overlay canvas is in physical pixels).
/// </summary>
public sealed class Loupe : Border
{
    public const int Cells = 15;
    private const int Half = Cells / 2;

    private readonly ImageBrush _brush;
    private readonly Rectangle _pixels;
    private readonly Canvas _grid;
    private readonly Rectangle _center;
    private readonly Grid _pixelArea;
    private readonly TextBlock _position;
    private readonly TextBlock _size;
    private readonly TextBlock _hex;
    private readonly Border _swatch;
    private readonly TextBlock _hint;

    private int _cell = 10;
    private double _scale = 1;

    public int PixelWidth { get; private set; }
    public int PixelHeight { get; private set; }

    public Loupe()
    {
        _brush = new ImageBrush
        {
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
        };
        RenderOptions.SetBitmapScalingMode(_brush, BitmapScalingMode.NearestNeighbor);

        _pixels = new Rectangle { Fill = _brush };
        RenderOptions.SetBitmapScalingMode(_pixels, BitmapScalingMode.NearestNeighbor);
        _grid = new Canvas { IsHitTestVisible = false };
        _center = new Rectangle { Stroke = Brushes.White, IsHitTestVisible = false };
        var centerShadow = new Rectangle { Stroke = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), IsHitTestVisible = false };
        _pixelArea = new Grid { ClipToBounds = true, Background = SpaceTheme.VoidBrush };
        _pixelArea.Children.Add(_pixels);
        _pixelArea.Children.Add(_grid);
        _pixelArea.Children.Add(centerShadow);
        _pixelArea.Children.Add(_center);
        _centerShadow = centerShadow;

        _position = Text(SpaceTheme.StarWhiteBrush);
        _size = Text(SpaceTheme.SunBrush);
        _hex = Text(SpaceTheme.StarWhiteBrush);
        _hint = Text(SpaceTheme.DustBrush);
        _swatch = new Border { BorderBrush = SpaceTheme.DustBrush, VerticalAlignment = VerticalAlignment.Center };

        var colorRow = new StackPanel { Orientation = Orientation.Horizontal };
        colorRow.Children.Add(_swatch);
        colorRow.Children.Add(_hex);
        colorRow.Children.Add(_hint);

        var info = new StackPanel();
        info.Children.Add(_position);
        info.Children.Add(_size);
        info.Children.Add(colorRow);
        _info = info;

        var stack = new StackPanel();
        stack.Children.Add(_pixelArea);
        stack.Children.Add(info);

        Child = stack;
        Background = new SolidColorBrush(Color.FromArgb(0xF0, SpaceTheme.Void.R, SpaceTheme.Void.G, SpaceTheme.Void.B));
        BorderBrush = SpaceTheme.CometBlueBrush;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = false;
    }

    private readonly Rectangle _centerShadow;
    private readonly StackPanel _info;

    private TextBlock Text(Brush fg) => new()
    {
        Foreground = fg,
        FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
    };

    /// <summary>Call once per capture with the frozen bitmap and the overlay's DPI scale.</summary>
    public void Configure(BitmapSource image, double dpiScale)
    {
        _brush.ImageSource = image;
        if (Math.Abs(dpiScale - _scale) < 0.01 && PixelWidth > 0) return;
        _scale = dpiScale;

        _cell = Math.Max(8, (int)Math.Round(9 * dpiScale));
        var area = Cells * _cell;
        var pad = (int)Math.Round(6 * dpiScale);
        var font = Math.Round(12 * dpiScale);
        var line = (int)Math.Ceiling(font * 1.45);

        _pixelArea.Width = _pixelArea.Height = area;
        _pixels.Width = _pixels.Height = area;

        _grid.Children.Clear();
        var gridBrush = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0));
        gridBrush.Freeze();
        for (var i = 1; i < Cells; i++)
        {
            _grid.Children.Add(new Line { X1 = i * _cell + 0.5, X2 = i * _cell + 0.5, Y1 = 0, Y2 = area, Stroke = gridBrush, StrokeThickness = 1 });
            _grid.Children.Add(new Line { X1 = 0, X2 = area, Y1 = i * _cell + 0.5, Y2 = i * _cell + 0.5, Stroke = gridBrush, StrokeThickness = 1 });
        }

        _center.Width = _center.Height = _cell + 2;
        _center.Margin = new Thickness(Half * _cell - 1, Half * _cell - 1, 0, 0);
        _center.HorizontalAlignment = HorizontalAlignment.Left;
        _center.VerticalAlignment = VerticalAlignment.Top;
        _center.StrokeThickness = Math.Max(1, Math.Round(dpiScale));
        _centerShadow.Width = _centerShadow.Height = _cell + 4;
        _centerShadow.Margin = new Thickness(Half * _cell - 2, Half * _cell - 2, 0, 0);
        _centerShadow.HorizontalAlignment = HorizontalAlignment.Left;
        _centerShadow.VerticalAlignment = VerticalAlignment.Top;

        foreach (var t in new[] { _position, _size, _hex, _hint })
        {
            t.FontSize = font;
            t.Height = line;
        }
        _swatch.Width = _swatch.Height = font;
        _swatch.BorderThickness = new Thickness(1);
        _swatch.Margin = new Thickness(0, 0, pad, 0);
        _hint.Margin = new Thickness(pad, 0, 0, 0);
        _hint.Text = "C copy";
        _info.Margin = new Thickness(pad, pad, pad, pad / 2.0);

        BorderThickness = new Thickness(Math.Max(1, Math.Round(dpiScale)));
        CornerRadius = new CornerRadius(10 * dpiScale);
        Padding = new Thickness(pad);

        PixelWidth = area + 2 * pad + 2 * (int)BorderThickness.Left;
        PixelHeight = PixelWidth + 3 * line + pad + pad / 2;
        Width = PixelWidth;
        Height = PixelHeight;
    }

    /// <summary>Drops the reference to the frozen screen so its memory can be reclaimed.</summary>
    public void Release() => _brush.ImageSource = null;

    public void Update(int localX, int localY, int screenX, int screenY, Color color, string? sizeText)
    {
        _brush.Viewbox = new Rect(localX - Half, localY - Half, Cells, Cells);
        _position.Text = string.Create(CultureInfo.InvariantCulture, $"X {screenX}  Y {screenY}");
        _size.Text = sizeText ?? " ";
        _hex.Text = HexOf(color);
        _swatch.Background = new SolidColorBrush(color);
    }

    public void FlashHint(string text) => _hint.Text = text;
    public void ResetHint() => _hint.Text = "C copy";

    public static string HexOf(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
