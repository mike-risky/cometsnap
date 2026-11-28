using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Cometsnap.Core.Styling;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Services;
using Cometsnap.Linux.Theme;

namespace Cometsnap.Linux.Tray;

/// <summary>
/// The Style studio: builds the share-ready look piece by piece (corners, border, drop shadow,
/// laser/neon glow, backdrop) with a big live preview. Every change applies and saves at once.
/// </summary>
public sealed class StyleStudioWindow : Window
{
    private readonly CometApp _app;
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly StackPanel _controls = new() { Margin = new Thickness(22, 8, 22, 22), Spacing = 4 };
    private readonly DispatcherTimer _debounce;
    private int _version;
    private bool _onLastCapture;

    /// <summary>Raised (debounced) after a change, so Settings can refresh its small preview.</summary>
    public event Action? StyleChanged;

    private SnapStyle St => _app.Settings.SnapStyle;

    public StyleStudioWindow(CometApp app)
    {
        _app = app;
        _app.Settings.SnapStyle ??= new SnapStyle();
        Title = T("Style studio");
        Width = 1040;
        Height = 720;
        MinWidth = 720;
        MinHeight = 480;
        Background = SpaceTheme.VoidBrush;
        Foreground = SpaceTheme.StarWhiteBrush;
        FontFamily = SpaceTheme.UiFont;
        Icon = new WindowIcon(SpaceTheme.Logo);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Render(); _app.SaveSettings(); StyleChanged?.Invoke(); };
        Closed += (_, _) => { _debounce.Stop(); _app.SaveSettings(); StyleChanged?.Invoke(); };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,420") };
        grid.Children.Add(PreviewPane());
        var scroll = new ScrollViewer { Content = _controls, Background = SpaceTheme.NebulaBrush };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        Content = grid;
        BuildControls();
        Render();
    }

    // ------------------------------------------------------------------ preview

    private Control PreviewPane()
    {
        var dock = new DockPanel { Margin = new Thickness(22) };
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 14), Spacing = 4 };
        head.Children.Add(new TextBlock { Text = T("Style studio").ToUpperInvariant(), FontFamily = SpaceTheme.DisplayFont, FontSize = 26, Foreground = SpaceTheme.SunBrush });
        head.Children.Add(Hint(T("Mix corners, border, shadow, glow and backdrop. Sizes are in pixels of the capture. Every change is saved right away.")));
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);

        var under = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        under.Children.Add(new TextBlock { Text = T("Preview on"), FontSize = 12, Foreground = SpaceTheme.DustBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 6) });
        Border? sample = null, last = null;
        void PaintChips() { Paint(sample!, !_onLastCapture); Paint(last!, _onLastCapture); }
        sample = Chip(T("Sample"), () => { _onLastCapture = false; PaintChips(); Render(); });
        last = Chip(T("Your last capture"), () => { _onLastCapture = true; PaintChips(); Render(); });
        under.Children.Add(sample);
        if (_app.LastRawCapture is not null) under.Children.Add(last);
        PaintChips();
        DockPanel.SetDock(under, Dock.Bottom);
        dock.Children.Add(under);

        dock.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(12), BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
            Background = Checker(), Padding = new Thickness(20), Child = _preview,
        });
        return dock;
    }

    /// <summary>A checkerboard, so see-through corners and shadows show as such.</summary>
    internal static IBrush Checker() => new DrawingBrush
    {
        TileMode = TileMode.Tile, DestinationRect = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute),
        Drawing = new DrawingGroup
        {
            Children =
            {
                new GeometryDrawing { Brush = SpaceTheme.NavyBrush, Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)) },
                new GeometryDrawing { Brush = SpaceTheme.WithAlpha(Colors.White, 0x14), Geometry = Geometry.Parse("M0,0 H8 V8 H0 Z M8,8 H16 V16 H8 Z") },
            },
        },
    };

    private void Changed() { _debounce.Stop(); _debounce.Start(); }

    /// <summary>Renders off the UI thread; only the newest render is shown.</summary>
    private void Render()
    {
        var version = ++_version;
        var style = St.Clone();
        var last = _onLastCapture ? _app.LastRawCapture : null;
        Task.Run(() =>
        {
            if (last is null) return StylePreview.Render(style);
            var scale = Math.Min(1.0, 1100.0 / Math.Max(last.PixelSize.Width, last.PixelSize.Height));
            var source = scale < 1
                ? last.CreateScaledBitmap(new PixelSize(Math.Max(1, (int)(last.PixelSize.Width * scale)), Math.Max(1, (int)(last.PixelSize.Height * scale))))
                : last;
            return SnapStyler.Apply(source, style, scale);
        }).ContinueWith(t =>
        {
            if (t.IsFaulted) { Log.Error("Style preview failed", t.Exception!); return; }
            Dispatcher.UIThread.Post(() => { if (version == _version) _preview.Source = t.Result; });
        });
    }

    // ------------------------------------------------------------------ controls

    /// <summary>English names of the presets, written out so the translation check sees them.</summary>
    internal static string PresetName(string id) => id switch
    {
        "clean" => T("Clean"),
        "showcase" => T("Showcase"),
        "frosted" => T("Frosted"),
        "neon" => T("Neon"),
        "laser" => T("Laser"),
        "comet" => T("Comet"),
        "polaroid" => T("Polaroid"),
        _ => id,
    };

    private static string BackdropName(StyleBackdrop b) => b switch
    {
        StyleBackdrop.None => T("None"),
        StyleBackdrop.Color => T("Color"),
        StyleBackdrop.Gradient => T("Gradient"),
        StyleBackdrop.Blur => T("Blurred capture"),
        StyleBackdrop.Space => T("Deep space"),
        _ => b.ToString(),
    };

    private void BuildControls()
    {
        _controls.Children.Clear();
        _controls.Children.Add(Section(T("Start from")));
        _controls.Children.Add(Hint(T("A starting point. Then change anything below.")));
        var presets = new WrapPanel();
        foreach (var (id, _) in SnapStylePresets.All)
        {
            var pid = id;
            var chip = Chip(PresetName(pid), () =>
            {
                _app.Settings.SnapStyle = SnapStylePresets.Get(pid);
                BuildControls();
                Changed();
            });
            Paint(chip, false);
            presets.Children.Add(chip);
        }
        _controls.Children.Add(presets);

        _controls.Children.Add(Section(T("Corners")));
        _controls.Children.Add(Slider(T("Radius"), 0, 80, St.CornerRadius, v => St.CornerRadius = v));
        _controls.Children.Add(Hint(T("0 = square corners.")));

        var border = Group(T("Border"), St.Border, v => St.Border = v);
        border.Children.Add(Slider(T("Width"), 1, 40, St.BorderWidth, v => St.BorderWidth = v));
        border.Children.Add(ColorRow(T("Color"), St.BorderColor, v => St.BorderColor = v));

        var shadow = Group(T("Drop shadow"), St.Shadow, v => St.Shadow = v);
        shadow.Children.Add(Slider(T("Left / right"), -80, 80, St.ShadowX, v => St.ShadowX = v));
        shadow.Children.Add(Slider(T("Up / down"), -80, 80, St.ShadowY, v => St.ShadowY = v));
        shadow.Children.Add(Slider(T("Blur"), 0, 120, St.ShadowBlur, v => St.ShadowBlur = v));
        shadow.Children.Add(Slider(T("Opacity"), 0, 100, St.ShadowOpacity, v => St.ShadowOpacity = v, "%"));
        shadow.Children.Add(ColorRow(T("Color"), St.ShadowColor, v => St.ShadowColor = v));

        var glow = Group(T("Glow (laser or neon)"), St.Glow, v => St.Glow = v);
        glow.Children.Add(Hint(T("Small = a sharp laser line. Big = a soft neon halo.")));
        glow.Children.Add(Slider(T("Size"), 1, 120, St.GlowSize, v => St.GlowSize = v));
        glow.Children.Add(Slider(T("Strength"), 0, 100, St.GlowStrength, v => St.GlowStrength = v, "%"));
        glow.Children.Add(ColorRow(T("Color"), St.GlowColor, v => St.GlowColor = v));

        _controls.Children.Add(Section(T("Backdrop")));
        var body = new StackPanel { Spacing = 4 };
        void BuildBackdrop()
        {
            body.Children.Clear();
            var chips = new WrapPanel();
            foreach (var b in Enum.GetValues<StyleBackdrop>())
            {
                var kind = b;
                var chip = Chip(BackdropName(kind), () => { St.Backdrop = kind; BuildBackdrop(); Changed(); });
                Paint(chip, St.Backdrop == kind);
                chips.Children.Add(chip);
            }
            body.Children.Add(chips);
            if (St.Backdrop == StyleBackdrop.None)
            {
                body.Children.Add(Hint(T("See-through: just the capture with its shadow and glow (PNG keeps it).")));
                return;
            }
            body.Children.Add(Slider(T("Space around"), 0, 240, St.Padding, v => St.Padding = v));
            if (St.Backdrop is StyleBackdrop.Color or StyleBackdrop.Gradient or StyleBackdrop.Space)
                body.Children.Add(ColorRow(St.Backdrop == StyleBackdrop.Color ? T("Color") : T("First color"), St.BackdropColor, v => St.BackdropColor = v));
            if (St.Backdrop is StyleBackdrop.Gradient or StyleBackdrop.Space)
                body.Children.Add(ColorRow(T("Second color"), St.BackdropColor2, v => St.BackdropColor2 = v));
        }
        BuildBackdrop();
        _controls.Children.Add(body);
    }

    // ------------------------------------------------------------------ building blocks

    private static Control Section(string text) => new TextBlock
    {
        Text = text.ToUpperInvariant(), FontFamily = SpaceTheme.HeadingFont, FontSize = 14, Foreground = SpaceTheme.DustBrush, Margin = new Thickness(0, 16, 0, 4),
    };

    private static TextBlock Hint(string text) => new() { Text = text, FontSize = 11, Foreground = SpaceTheme.DustBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };

    /// <summary>A section with an on/off box in its title; the returned panel holds its settings and dims when off.</summary>
    private StackPanel Group(string title, bool value, Action<bool> set)
    {
        var box = new CheckBox
        {
            IsChecked = value, Margin = new Thickness(0, 14, 0, 2),
            Content = new TextBlock { Text = title.ToUpperInvariant(), FontFamily = SpaceTheme.HeadingFont, FontSize = 14, Foreground = SpaceTheme.DustBrush },
        };
        var body = new StackPanel { Spacing = 4 };
        void Apply() { body.Opacity = box.IsChecked == true ? 1 : 0.4; body.IsHitTestVisible = box.IsChecked == true; }
        Apply();
        box.IsCheckedChanged += (_, _) => { set(box.IsChecked == true); Apply(); Changed(); };
        _controls.Children.Add(box);
        _controls.Children.Add(body);
        return body;
    }

    /// <summary>A labelled slider with a number box (type any value in range).</summary>
    private Control Slider(string title, int min, int max, int value, Action<int> set, string unit = "px")
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("108,*,60,26") };
        grid.Children.Add(new TextBlock { Text = title, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), IsSnapToTickEnabled = true, TickFrequency = 1,
            SmallChange = 1, LargeChange = 10, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);
        var box = new TextBox { Text = value.ToString(CultureInfo.InvariantCulture), FontSize = 13, MinHeight = 28, Padding = new Thickness(6, 3), HorizontalContentAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(box, 2);
        grid.Children.Add(box);
        var u = new TextBlock { Text = unit, FontSize = 12, Foreground = SpaceTheme.DustBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
        Grid.SetColumn(u, 3);
        grid.Children.Add(u);

        var v = Math.Clamp(value, min, max);
        var syncing = false;
        void SetValue(int n)
        {
            n = Math.Clamp(n, min, max);
            if (n == v) return;
            v = n;
            set(n);
            Changed();
        }
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty || syncing) return;
            var n = (int)Math.Round(slider.Value);
            syncing = true;
            box.Text = n.ToString(CultureInfo.InvariantCulture);
            syncing = false;
            SetValue(n);
        };
        box.TextChanged += (_, _) =>
        {
            if (syncing || !int.TryParse(box.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < min || n > max) return;
            syncing = true;
            slider.Value = n;
            syncing = false;
            SetValue(n);
        };
        box.LostFocus += (_, _) => box.Text = v.ToString(CultureInfo.InvariantCulture);
        return grid;
    }

    /// <summary>A color: palette swatches, a hex box, and hue / saturation / brightness sliders for any color.</summary>
    private Control ColorRow(string title, string value, Action<string> set)
    {
        var stack = new StackPanel { Spacing = 6, Margin = new Thickness(0, 2, 0, 8) };
        var current = StyleColors.Normalize(value, "#FFFFFF");
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("108,Auto,Auto,*") };
        top.Children.Add(new TextBlock { Text = title, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        var big = new Border { Width = 30, Height = 26, CornerRadius = new CornerRadius(6), BorderBrush = SpaceTheme.StarWhiteBrush, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0), Cursor = new Cursor(StandardCursorType.Hand) };
        Grid.SetColumn(big, 1);
        top.Children.Add(big);
        var box = new TextBox { Width = 92, FontSize = 13, MinHeight = 28, Padding = new Thickness(6, 3), FontFamily = new FontFamily("DejaVu Sans Mono, monospace") };
        Grid.SetColumn(box, 2);
        top.Children.Add(box);
        var more = new TextBlock { Text = "🎨 " + T("Any color"), FontSize = 12, Foreground = SpaceTheme.DustBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Cursor = new Cursor(StandardCursorType.Hand) };
        Grid.SetColumn(more, 3);
        top.Children.Add(more);
        stack.Children.Add(top);

        var swatches = new WrapPanel { Margin = new Thickness(108, 0, 0, 0) };
        var chips = new List<(Border Chip, string Hex)>();
        Action<string> pick = _ => { };
        var typing = false;
        var fromHsv = false;
        foreach (var hex in StyleColors.Palette)
        {
            var h = hex;
            var chip = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 6, 6), Background = new ImmutableSolidColorBrush(ToColor(StyleColors.Parse(h, 0))), Cursor = new Cursor(StandardCursorType.Hand) };
            ToolTip.SetTip(chip, h);
            chip.PointerPressed += (_, e) => { e.Handled = true; pick(h); };
            chips.Add((chip, h));
            swatches.Children.Add(chip);
        }
        stack.Children.Add(swatches);

        // hue / saturation / brightness: every color there is
        var hsv = new StackPanel { Spacing = 2, Margin = new Thickness(108, 0, 0, 0), IsVisible = false };
        var (h0, s0, v0) = ToHsv(StyleColors.Parse(current, 0xFFFFFF));
        var hue = HsvSlider(0, 359, h0);
        var sat = HsvSlider(0, 100, s0 * 100);
        var bri = HsvSlider(0, 100, v0 * 100);
        foreach (var (label, sl) in new[] { (T("Hue"), hue), (T("Saturation"), sat), (T("Brightness"), bri) })
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*") };
            row.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = SpaceTheme.DustBrush, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(sl, 1);
            row.Children.Add(sl);
            hsv.Children.Add(row);
        }
        stack.Children.Add(hsv);
        void HsvChanged()
        {
            if (fromHsv) return;
            fromHsv = true;
            Apply(StyleColors.Hex(FromHsv(hue.Value, sat.Value / 100, bri.Value / 100)));
            fromHsv = false;
        }
        foreach (var sl in new[] { hue, sat, bri })
            sl.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) HsvChanged(); };
        void Toggle(object? s, PointerPressedEventArgs e) { e.Handled = true; hsv.IsVisible = !hsv.IsVisible; }
        big.PointerPressed += Toggle;
        more.PointerPressed += Toggle;

        void Show()
        {
            big.Background = new ImmutableSolidColorBrush(ToColor(StyleColors.Parse(current, 0)));
            if (!typing) box.Text = current;
            foreach (var (chip, h) in chips)
            {
                var on = string.Equals(h, current, StringComparison.OrdinalIgnoreCase);
                chip.BorderBrush = on ? SpaceTheme.SunBrush : SpaceTheme.NavyLightBrush;
                chip.BorderThickness = new Thickness(on ? 3 : 1);
            }
            if (!fromHsv)
            {
                var (hh, ss, vv) = ToHsv(StyleColors.Parse(current, 0));
                fromHsv = true;
                hue.Value = hh; sat.Value = ss * 100; bri.Value = vv * 100;
                fromHsv = false;
            }
        }
        void Apply(string hex)
        {
            var n = StyleColors.Normalize(hex, current);
            if (n == current) { Show(); return; }
            current = n;
            set(n);
            Show();
            Changed();
        }
        box.TextChanged += (_, _) =>
        {
            if (!box.IsFocused) return;
            var t = box.Text?.Trim().TrimStart('#') ?? "";
            if (t.Length != 6 || StyleColors.TryParse(t) is null) return;
            typing = true;
            Apply(t);
            typing = false;
        };
        box.LostFocus += (_, _) => box.Text = current;
        pick = Apply;
        Show();
        return stack;
    }

    private static Slider HsvSlider(double min, double max, double value) => new()
    {
        Minimum = min, Maximum = max, Value = value, SmallChange = 1, LargeChange = 10, Margin = new Thickness(0, -6, 10, -6),
    };

    private static Border Chip(string text, Action click)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 6), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = text, FontSize = 13 }, Cursor = new Cursor(StandardCursorType.Hand),
        };
        b.PointerEntered += (_, _) => { if (b.Tag is not true) b.Background = SpaceTheme.HoverBrush; };
        b.PointerExited += (_, _) => { if (b.Tag is not true) b.Background = SpaceTheme.NavyBrush; };
        b.PointerPressed += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    private static void Paint(Border chip, bool on)
    {
        chip.Tag = on;
        chip.Background = on ? SpaceTheme.CometBlueBrush : SpaceTheme.NavyBrush;
        chip.BorderBrush = on ? SpaceTheme.CometBlueBrush : SpaceTheme.NavyLightBrush;
        if (chip.Child is TextBlock t) t.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
    }

    // ------------------------------------------------------------------ colors

    private static Color ToColor(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static (double H, double S, double V) ToHsv(uint rgb)
    {
        double r = ((rgb >> 16) & 0xFF) / 255.0, g = ((rgb >> 8) & 0xFF) / 255.0, b = (rgb & 0xFF) / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var d = max - Math.Min(r, Math.Min(g, b));
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }

    private static uint FromHsv(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = (h % 360) switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        static uint B(double t) => (uint)Math.Clamp(Math.Round(t * 255), 0, 255);
        return (B(r + m) << 16) | (B(g + m) << 8) | B(b + m);
    }
}
