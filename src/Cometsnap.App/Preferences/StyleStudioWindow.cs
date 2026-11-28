using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Cometsnap.App.Theme;
using Cometsnap.Core.Localization;
using Cometsnap.Core.Native;
using Cometsnap.Core.Settings;
using Cometsnap.Core.Styling;

namespace Cometsnap.App.Preferences;

/// <summary>
/// The Style studio: builds the share-ready look piece by piece (corners, border, drop shadow,
/// laser/neon glow, backdrop) with a big live preview. Every change applies and saves right away.
/// </summary>
public sealed class StyleStudioWindow : Window
{
    private readonly CometApp _app;
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly StackPanel _controls = new() { Margin = new Thickness(24, 8, 24, 24) };
    private readonly DispatcherTimer _debounce;
    private int _version;
    private bool _onLastCapture;

    /// <summary>Raised (debounced) after the style changed, so Settings can refresh its small preview.</summary>
    public event Action? StyleChanged;

    private AppSettings S => _app.Settings;
    private SnapStyle St => S.SnapStyle;

    public StyleStudioWindow(CometApp app)
    {
        _app = app;
        S.SnapStyle ??= new SnapStyle();
        Title = L.T("Style studio");
        Width = 1080;
        Height = 760;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = SpaceTheme.VoidBrush;
        Foreground = SpaceTheme.StarWhiteBrush;
        FontFamily = SpaceTheme.UiFont;
        FontSize = 14;
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/cometsnap.ico")); }
        catch { /* fine without */ }
        SourceInitialized += (_, _) =>
        {
            var dark = 1;
            Win32.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        };
        _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(45) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Render(); _app.TrySaveSettings(); StyleChanged?.Invoke(); };
        Closed += (_, _) => { _debounce.Stop(); _app.TrySaveSettings(); StyleChanged?.Invoke(); };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });
        grid.Children.Add(PreviewPane());
        var scroll = new ScrollViewer { Content = _controls, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = SpaceTheme.NebulaBrush };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        Content = grid;
        BuildControls();
        Render();
    }

    // ------------------------------------------------------------------ preview

    private UIElement PreviewPane()
    {
        var dock = new DockPanel { Margin = new Thickness(24) };
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        head.Children.Add(new TextBlock { Text = L.T("Style studio").ToUpper(L.Culture), FontFamily = SpaceTheme.DisplayFont, FontSize = 26, Foreground = SpaceTheme.StarWhiteBrush });
        head.Children.Add(new TextBlock
        {
            Text = L.T("Mix corners, border, shadow, glow and backdrop. Sizes are in pixels of the capture. Every change is saved right away."),
            Foreground = SpaceTheme.DustBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        });
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);

        var under = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        under.Children.Add(new TextBlock { Text = L.T("Preview on"), Foreground = SpaceTheme.DustBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 8) });
        Border? sampleChip = null, lastChip = null;
        void PaintChips()
        {
            Paint(sampleChip!, !_onLastCapture);
            Paint(lastChip!, _onLastCapture);
        }
        sampleChip = Chip(L.T("Sample"), () => { _onLastCapture = false; PaintChips(); Render(); });
        lastChip = Chip(L.T("Your last capture"), () => { _onLastCapture = true; PaintChips(); Render(); });
        under.Children.Add(sampleChip);
        if (_app.Capture.LastRawCapture is not null) under.Children.Add(lastChip);
        PaintChips();
        DockPanel.SetDock(under, Dock.Bottom);
        dock.Children.Add(under);

        RenderOptions.SetBitmapScalingMode(_preview, BitmapScalingMode.HighQuality);
        dock.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(12), BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
            Background = StyleSample.Checker(), Padding = new Thickness(20), Child = _preview,
        });
        return dock;
    }

    private void Changed() { _debounce.Stop(); _debounce.Start(); }

    /// <summary>Renders the preview off the UI thread; only the newest render is shown.</summary>
    private void Render()
    {
        var version = ++_version;
        var style = St.Clone();
        var source = StyleSample.Sample;
        var scale = 1.0;
        if (_onLastCapture && _app.Capture.LastRawCapture is { } last)
        {
            // Big captures are shrunk for the preview; the style shrinks with them so it looks the same.
            scale = Math.Min(1.0, 1100.0 / Math.Max(last.PixelWidth, last.PixelHeight));
            source = scale < 1 ? StyleSample.Shrink(last, scale) : last;
        }
        Task.Run(() => StyledBitmaps.Apply(source, style, scale)).ContinueWith(t =>
        {
            if (version != _version || t.Result is null) return;
            _preview.Source = t.Result;
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.FromCurrentSynchronizationContext());
    }

    // ------------------------------------------------------------------ controls

    /// <summary>English names of the presets, here so the translation check sees them.</summary>
    internal static string PresetName(string id) => id switch
    {
        "clean" => L.T("Clean"),
        "showcase" => L.T("Showcase"),
        "frosted" => L.T("Frosted"),
        "neon" => L.T("Neon"),
        "laser" => L.T("Laser"),
        "comet" => L.T("Comet"),
        "polaroid" => L.T("Polaroid"),
        _ => id,
    };

    private static string BackdropName(StyleBackdrop b) => b switch
    {
        StyleBackdrop.None => L.T("None"),
        StyleBackdrop.Color => L.T("Color"),
        StyleBackdrop.Gradient => L.T("Gradient"),
        StyleBackdrop.Blur => L.T("Blurred capture"),
        StyleBackdrop.Space => L.T("Deep space"),
        _ => b.ToString(),
    };

    private void BuildControls()
    {
        _controls.Children.Clear();

        Section(L.T("Start from"));
        _controls.Children.Add(Hint(L.T("A starting point. Then change anything below.")));
        var presets = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var (id, _) in SnapStylePresets.All)
        {
            var pid = id;
            var chip = Chip(PresetName(pid), () =>
            {
                S.SnapStyle = SnapStylePresets.Get(pid);
                BuildControls();
                Changed();
            });
            Paint(chip, false);
            presets.Children.Add(chip);
        }
        _controls.Children.Add(presets);

        // ---- corners
        Section(L.T("Corners"));
        _controls.Children.Add(Slider(L.T("Radius"), 0, 80, St.CornerRadius, v => St.CornerRadius = v));
        _controls.Children.Add(Hint(L.T("0 = square corners.")));

        // ---- border
        var border = Group(L.T("Border"), St.Border, v => St.Border = v);
        border.Children.Add(Slider(L.T("Width"), 1, 40, St.BorderWidth, v => St.BorderWidth = v));
        border.Children.Add(ColorRow(L.T("Color"), St.BorderColor, v => St.BorderColor = v));

        // ---- shadow
        var shadow = Group(L.T("Drop shadow"), St.Shadow, v => St.Shadow = v);
        shadow.Children.Add(Slider(L.T("Left / right"), -80, 80, St.ShadowX, v => St.ShadowX = v));
        shadow.Children.Add(Slider(L.T("Up / down"), -80, 80, St.ShadowY, v => St.ShadowY = v));
        shadow.Children.Add(Slider(L.T("Blur"), 0, 120, St.ShadowBlur, v => St.ShadowBlur = v));
        shadow.Children.Add(Slider(L.T("Opacity"), 0, 100, St.ShadowOpacity, v => St.ShadowOpacity = v, "%"));
        shadow.Children.Add(ColorRow(L.T("Color"), St.ShadowColor, v => St.ShadowColor = v));

        // ---- glow
        var glow = Group(L.T("Glow (laser or neon)"), St.Glow, v => St.Glow = v);
        glow.Children.Add(Hint(L.T("Small = a sharp laser line. Big = a soft neon halo.")));
        glow.Children.Add(Slider(L.T("Size"), 1, 120, St.GlowSize, v => St.GlowSize = v));
        glow.Children.Add(Slider(L.T("Strength"), 0, 100, St.GlowStrength, v => St.GlowStrength = v, "%"));
        glow.Children.Add(ColorRow(L.T("Color"), St.GlowColor, v => St.GlowColor = v));

        // ---- backdrop
        Section(L.T("Backdrop"));
        var backdropBody = new StackPanel();
        void BuildBackdrop()
        {
            backdropBody.Children.Clear();
            var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var b in Enum.GetValues<StyleBackdrop>())
            {
                var kind = b;
                var chip = Chip(BackdropName(kind), () => { St.Backdrop = kind; BuildBackdrop(); Changed(); });
                Paint(chip, St.Backdrop == kind);
                chips.Children.Add(chip);
            }
            backdropBody.Children.Add(chips);
            if (St.Backdrop == StyleBackdrop.None)
            {
                backdropBody.Children.Add(Hint(L.T("See-through: just the capture with its shadow and glow (PNG keeps it).")));
                return;
            }
            backdropBody.Children.Add(Slider(L.T("Space around"), 0, 240, St.Padding, v => St.Padding = v));
            if (St.Backdrop is StyleBackdrop.Color or StyleBackdrop.Gradient or StyleBackdrop.Space)
                backdropBody.Children.Add(ColorRow(St.Backdrop == StyleBackdrop.Color ? L.T("Color") : L.T("First color"), St.BackdropColor, v => St.BackdropColor = v));
            if (St.Backdrop is StyleBackdrop.Gradient or StyleBackdrop.Space)
                backdropBody.Children.Add(ColorRow(L.T("Second color"), St.BackdropColor2, v => St.BackdropColor2 = v));
        }
        BuildBackdrop();
        _controls.Children.Add(backdropBody);
    }

    // ------------------------------------------------------------------ building blocks

    private void Section(string title) =>
        _controls.Children.Add(new TextBlock
        {
            Text = title.ToUpper(L.Culture), FontFamily = SpaceTheme.HeadingFont, FontSize = 15, Foreground = SpaceTheme.CometBlueBrush,
            Margin = new Thickness(0, 22, 0, 10),
        });

    private static TextBlock Hint(string text) =>
        new() { Text = text, Foreground = SpaceTheme.DustBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, -2, 0, 10) };

    /// <summary>A section whose title has an on/off switch; the returned panel holds its settings and hides when off.</summary>
    private StackPanel Group(string title, bool value, Action<bool> set)
    {
        var head = new Grid { Margin = new Thickness(0, 22, 0, 10), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock { Text = title.ToUpper(L.Culture), FontFamily = SpaceTheme.HeadingFont, FontSize = 15, Foreground = SpaceTheme.CometBlueBrush, VerticalAlignment = VerticalAlignment.Center });
        var knob = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = SpaceTheme.StarWhiteBrush, Margin = new Thickness(3, 0, 3, 0) };
        var track = new Border { Width = 42, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1), Child = knob, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(track, 1);
        head.Children.Add(track);
        var body = new StackPanel();
        var on = value;
        void PaintSwitch()
        {
            track.Background = on ? SpaceTheme.TerracottaBrush : SpaceTheme.NavyBrush;
            track.BorderBrush = on ? SpaceTheme.TerracottaBrush : SpaceTheme.NavyLightBrush;
            knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            body.Opacity = on ? 1 : 0.4;
            body.IsHitTestVisible = on;
        }
        PaintSwitch();
        head.MouseLeftButtonUp += (_, e) => { e.Handled = true; on = !on; set(on); PaintSwitch(); Changed(); };
        _controls.Children.Add(head);
        _controls.Children.Add(body);
        return body;
    }

    /// <summary>A labelled slider with a number box: drag, click, arrow keys (Shift = 10) or type a value.</summary>
    private UIElement Slider(string title, int min, int max, int value, Action<int> set, string unit = "px")
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.Children.Add(new TextBlock { Text = title, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = title });

        var canvas = new Canvas { Height = 24, Background = Brushes.Transparent, Cursor = Cursors.Hand, Focusable = true, Margin = new Thickness(0, 0, 10, 0), FocusVisualStyle = null };
        var rail = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = SpaceTheme.NavyLightBrush };
        var fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = SpaceTheme.CometBlueBrush };
        var thumb = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = SpaceTheme.StarWhiteBrush, BorderBrush = SpaceTheme.CometBlueBrush, BorderThickness = new Thickness(3) };
        Canvas.SetTop(rail, 10); Canvas.SetTop(fill, 10); Canvas.SetTop(thumb, 4);
        canvas.Children.Add(rail); canvas.Children.Add(fill); canvas.Children.Add(thumb);
        Grid.SetColumn(canvas, 1);
        grid.Children.Add(canvas);

        var box = new TextBox
        {
            Text = value.ToString(CultureInfo.InvariantCulture), FontSize = 13, Height = 26, VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Right, Padding = new Thickness(4, 0, 4, 0),
            Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xC0), Foreground = SpaceTheme.StarWhiteBrush, CaretBrush = SpaceTheme.StarWhiteBrush,
            BorderBrush = SpaceTheme.NavyLightBrush,
        };
        Grid.SetColumn(box, 2);
        grid.Children.Add(box);
        var unitText = new TextBlock { Text = unit, FontSize = 12, Foreground = SpaceTheme.DustBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
        Grid.SetColumn(unitText, 3);
        grid.Children.Add(unitText);

        var v = Math.Clamp(value, min, max);
        double X(int n) => 8 + (n - min) / (double)Math.Max(1, max - min) * Math.Max(0, canvas.ActualWidth - 16);
        void Layout()
        {
            rail.Width = Math.Max(0, canvas.ActualWidth);
            var x = X(v);
            var origin = X(Math.Clamp(0, min, max));
            Canvas.SetLeft(fill, Math.Min(origin, x));
            fill.Width = Math.Abs(x - origin);
            Canvas.SetLeft(thumb, x - 8);
        }
        void SetValue(int n, bool updateBox = true)
        {
            n = Math.Clamp(n, min, max);
            if (updateBox) box.Text = n.ToString(CultureInfo.InvariantCulture);
            if (n == v) return;
            v = n;
            Layout();
            set(n);
            Changed();
        }
        void FromMouse(MouseEventArgs e)
        {
            var w = Math.Max(1, canvas.ActualWidth - 16);
            var t = Math.Clamp((e.GetPosition(canvas).X - 8) / w, 0, 1);
            SetValue(min + (int)Math.Round(t * (max - min)));
        }
        canvas.SizeChanged += (_, _) => Layout();
        canvas.MouseLeftButtonDown += (_, e) => { canvas.Focus(); canvas.CaptureMouse(); FromMouse(e); e.Handled = true; };
        canvas.MouseMove += (_, e) => { if (canvas.IsMouseCaptured) FromMouse(e); };
        canvas.MouseLeftButtonUp += (_, e) => { canvas.ReleaseMouseCapture(); e.Handled = true; };
        canvas.KeyDown += (_, e) =>
        {
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            if (e.Key is Key.Left or Key.Down) { SetValue(v - step); e.Handled = true; }
            else if (e.Key is Key.Right or Key.Up) { SetValue(v + step); e.Handled = true; }
        };
        canvas.GotKeyboardFocus += (_, _) => thumb.BorderBrush = SpaceTheme.SunBrush;
        canvas.LostKeyboardFocus += (_, _) => thumb.BorderBrush = SpaceTheme.CometBlueBrush;
        void FromBox()
        {
            if (int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) SetValue(n);
            else box.Text = v.ToString(CultureInfo.InvariantCulture);
        }
        box.TextChanged += (_, _) =>
        {
            if (box.IsKeyboardFocused && int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max)
                SetValue(n, updateBox: false);
        };
        box.LostKeyboardFocus += (_, _) => FromBox();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { FromBox(); e.Handled = true; } };
        return grid;
    }

    /// <summary>A color: palette swatches, a hex box (any color, e.g. pasted from the color picker) and a full color wheel.</summary>
    private UIElement ColorRow(string title, string value, Action<string> set)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.Children.Add(new TextBlock { Text = title, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        var current = StyleColors.Normalize(value, "#FFFFFF");

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        var big = new Border
        {
            Width = 30, Height = 26, CornerRadius = new CornerRadius(6), BorderBrush = SpaceTheme.StarWhiteBrush, BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand, ToolTip = L.T("Any color"), Margin = new Thickness(0, 0, 8, 0),
        };
        var box = new TextBox
        {
            Width = 86, Height = 26, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 0, 4, 0),
            Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xC0), Foreground = SpaceTheme.StarWhiteBrush, CaretBrush = SpaceTheme.StarWhiteBrush,
            BorderBrush = SpaceTheme.NavyLightBrush, FontFamily = new FontFamily("Consolas, Segoe UI"), ToolTip = "#RRGGBB",
        };
        var more = new TextBlock { Text = "🎨 " + L.T("Any color"), FontSize = 12, Foreground = SpaceTheme.DustBrush, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        right.Children.Add(big);
        right.Children.Add(box);
        right.Children.Add(more);
        Grid.SetColumn(right, 1);
        top.Children.Add(right);
        panel.Children.Add(top);

        var swatches = new WrapPanel { Margin = new Thickness(112, 8, 0, 0) };
        var chips = new List<(Border Chip, string Hex)>();
        foreach (var hex in StyleColors.Palette)
        {
            var h = hex;
            var chip = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand,
                Background = new SolidColorBrush(ToColor(StyleColors.Parse(h, 0))), ToolTip = h,
            };
            chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; Apply(h); };
            chips.Add((chip, h));
            swatches.Children.Add(chip);
        }
        panel.Children.Add(swatches);

        void Show()
        {
            big.Background = new SolidColorBrush(ToColor(StyleColors.Parse(current, 0)));
            if (!box.IsKeyboardFocused) box.Text = current;
            foreach (var (chip, h) in chips)
            {
                var sel = string.Equals(h, current, StringComparison.OrdinalIgnoreCase);
                chip.BorderBrush = sel ? SpaceTheme.SunBrush : SpaceTheme.NavyLightBrush;
                chip.BorderThickness = new Thickness(sel ? 3 : 1);
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
        box.TextChanged += (_, _) => { if (box.IsKeyboardFocused && StyleColors.TryParse(box.Text) is not null && box.Text.Trim().TrimStart('#').Length == 6) Apply(box.Text); };
        box.LostKeyboardFocus += (_, _) => { if (StyleColors.TryParse(box.Text) is not null) Apply(box.Text); box.Text = current; };
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { if (StyleColors.TryParse(box.Text) is not null) Apply(box.Text); box.Text = current; e.Handled = true; } };
        void Wheel(object s, MouseButtonEventArgs e) { e.Handled = true; OpenWheel(big, current, Apply); }
        big.MouseLeftButtonUp += Wheel;
        more.MouseLeftButtonUp += Wheel;
        Show();
        return panel;
    }

    /// <summary>A popup with a saturation/brightness square and a hue strip: every color there is.</summary>
    private void OpenWheel(FrameworkElement anchor, string start, Action<string> apply)
    {
        var (hue, sat, val) = ToHsv(StyleColors.Parse(start, 0xFFFFFF));
        const double sw = 220, sh = 150;
        var hueColor = new SolidColorBrush();
        var square = new Grid { Width = sw, Height = sh, Cursor = Cursors.Cross, ClipToBounds = true };
        square.Children.Add(new Border { Background = hueColor, CornerRadius = new CornerRadius(6) });
        square.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0) });
        square.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90) });
        var marker = new Ellipse { Width = 14, Height = 14, Stroke = Brushes.White, StrokeThickness = 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        square.Children.Add(marker);

        var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (var i = 0; i <= 6; i++) rainbow.GradientStops.Add(new GradientStop(ToColor(FromHsv(i * 60 % 360, 1, 1)), i / 6.0));
        var strip = new Grid { Width = sw, Height = 16, Margin = new Thickness(0, 10, 0, 0), Cursor = Cursors.Hand };
        strip.Children.Add(new Border { Background = rainbow, CornerRadius = new CornerRadius(8) });
        var hueMarker = new Border { Width = 6, Height = 20, Margin = new Thickness(0, -2, 0, -2), BorderBrush = Brushes.White, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
        strip.Children.Add(hueMarker);

        void Paint()
        {
            hueColor.Color = ToColor(FromHsv(hue, 1, 1));
            marker.Margin = new Thickness(sat * sw - 7, (1 - val) * sh - 7, 0, 0);
            hueMarker.Margin = new Thickness(hue / 360 * (sw - 6), -2, 0, -2);
        }
        void Emit() => apply(StyleColors.Hex(FromHsv(hue, sat, val)));
        void FromSquare(MouseEventArgs e)
        {
            var p = e.GetPosition(square);
            sat = Math.Clamp(p.X / sw, 0, 1);
            val = Math.Clamp(1 - p.Y / sh, 0, 1);
            Paint(); Emit();
        }
        void FromStrip(MouseEventArgs e)
        {
            hue = Math.Clamp(e.GetPosition(strip).X / sw, 0, 1) * 359.9;
            Paint(); Emit();
        }
        square.MouseLeftButtonDown += (_, e) => { square.CaptureMouse(); FromSquare(e); e.Handled = true; };
        square.MouseMove += (_, e) => { if (square.IsMouseCaptured) FromSquare(e); };
        square.MouseLeftButtonUp += (_, _) => square.ReleaseMouseCapture();
        strip.MouseLeftButtonDown += (_, e) => { strip.CaptureMouse(); FromStrip(e); e.Handled = true; };
        strip.MouseMove += (_, e) => { if (strip.IsMouseCaptured) FromStrip(e); };
        strip.MouseLeftButtonUp += (_, _) => strip.ReleaseMouseCapture();
        Paint();

        var body = new StackPanel();
        body.Children.Add(square);
        body.Children.Add(strip);
        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = true, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade,
            Child = new Border
            {
                Background = SpaceTheme.NavyBrush, BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 6, 0, 0), Child = body,
            },
        };
        // StaysOpen + closing by hand: dragging inside takes the mouse, which would shut a self-closing popup.
        void Close(object? s, EventArgs e) => popup.IsOpen = false;
        MouseButtonEventHandler down = (_, _) => popup.IsOpen = false;
        PreviewMouseDown += down;
        Deactivated += Close;
        popup.Closed += (_, _) => { PreviewMouseDown -= down; Deactivated -= Close; };
        _wheel?.SetCurrentValue(Popup.IsOpenProperty, false);
        _wheel = popup;
        popup.IsOpen = true;
    }

    private Popup? _wheel;

    private static Border Chip(string text, Action click)
    {
        var b = new Border
        {
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, Child = new TextBlock { Text = text, FontSize = 13 },
        };
        b.MouseEnter += (_, _) => { if (b.Tag is not true) b.Background = SpaceTheme.NavyLightBrush; };
        b.MouseLeave += (_, _) => { if (b.Tag is not true) b.Background = SpaceTheme.NavyBrush; };
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    private static void Paint(Border chip, bool selected)
    {
        chip.Tag = selected;
        chip.Background = selected ? SpaceTheme.CometBlueBrush : SpaceTheme.NavyBrush;
        chip.BorderBrush = selected ? SpaceTheme.CometBlueBrush : SpaceTheme.NavyLightBrush;
        if (chip.Child is TextBlock t) t.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
    }

    // ------------------------------------------------------------------ colors

    private static Color ToColor(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static (double H, double S, double V) ToHsv(uint rgb)
    {
        double r = ((rgb >> 16) & 0xFF) / 255.0, g = ((rgb >> 8) & 0xFF) / 255.0, b = (rgb & 0xFF) / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
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

/// <summary>The pretend screenshot used by style previews, plus small helpers shared with Settings.</summary>
internal static class StyleSample
{
    private static BitmapSource? _sample;

    /// <summary>A little app window at real pixel size (frozen, so any thread can read it).</summary>
    public static BitmapSource Sample => _sample ??= Draw();

    /// <summary>A checkerboard, so see-through corners and shadows show as such.</summary>
    public static Brush Checker()
    {
        var checker = new DrawingBrush
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute,
            Drawing = new GeometryDrawing(SpaceTheme.WithAlpha(Colors.White, 0x14), null, Geometry.Parse("M0,0 H8 V8 H0 Z M8,8 H16 V16 H8 Z")),
        };
        checker.Freeze();
        return checker;
    }

    public static BitmapSource Shrink(BitmapSource image, double scale)
    {
        var small = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        var copy = new WriteableBitmap(small);
        copy.Freeze();
        return copy;
    }

    private static BitmapSource Draw()
    {
        const int w = 640, h = 380;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFC)), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xE6, 0xE9, 0xF3)), null, new Rect(0, 0, w, 44));
            var dots = new[] { Color.FromRgb(0xE2, 0x55, 0x3A), Color.FromRgb(0xFF, 0xD6, 0x0A), Color.FromRgb(0x3D, 0x6B, 0xFF) };
            for (var i = 0; i < 3; i++) dc.DrawEllipse(new SolidColorBrush(dots[i]), null, new Point(24 + i * 22, 22), 7, 7);
            var ink = new SolidColorBrush(Color.FromRgb(0x07, 0x11, 0x3A));
            dc.DrawRoundedRectangle(ink, null, new Rect(36, 76, 260, 26), 6, 6);
            var soft = new SolidColorBrush(Color.FromRgb(0xC9, 0xCF, 0xE3));
            for (var i = 0; i < 4; i++) dc.DrawRoundedRectangle(soft, null, new Rect(36, 126 + i * 30, 300 - i * 40, 14), 7, 7);
            dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(0x3D, 0x6B, 0xFF), Color.FromRgb(0xE2, 0x55, 0x3A), 45), null, new Rect(376, 76, 228, 166), 12, 12);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0x0A)), null, new Point(490, 159), 34, 34);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0xE2, 0x55, 0x3A)), null, new Rect(36, 280, 140, 44), 10, 10);
            dc.DrawRoundedRectangle(soft, null, new Rect(376, 270, 228, 14), 7, 7);
            dc.DrawRoundedRectangle(soft, null, new Rect(376, 300, 170, 14), 7, 7);
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }
}
