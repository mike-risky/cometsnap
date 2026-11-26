using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Services;
using Cometsnap.Linux.Theme;

namespace Cometsnap.Linux.Editing;

/// <summary>
/// The editor: a capture with its shapes, still editable. A big labeled tool rail on the left,
/// colors, sizes and actions on top. Copy and Save finish the job and close the window.
/// </summary>
public sealed class EditorWindow : Window
{
    private static readonly double[] Sizes = { 2, 4, 7 };
    private static readonly double[] TextSizes = { 14, 20, 30 };
    private static readonly string[] SizeNames = { "S", "M", "L" };

    private readonly CometApp _app;
    private readonly Bitmap _base;
    private readonly AnnotationDocument _doc;
    private readonly EditorSurface _surface;
    private readonly Canvas _textLayer;
    private readonly ScrollViewer _scroller;
    private readonly TextBlock _status;
    private readonly TextBlock _hint;
    private readonly Border _confirm;
    private readonly Dictionary<EditorTool, Border> _toolButtons = new();
    private readonly Dictionary<string, Border> _swatches = new();
    private readonly Border[] _sizeButtons = new Border[3];
    private readonly Border _constellationButton;

    private string? _path;
    private bool _dirty;
    private double _zoom = 1;
    private EditorTool _tool = EditorTool.Arrow;
    private string _color = EditorTools.Palette[0];
    private int _size = 1;

    private Annotation? _drawing;
    private Annotation? _selected;
    private Point _dragFrom;
    private bool _moved;
    private TextBox? _textBox;
    private Point _textAt;
    private Point _calloutTarget;
    private bool _textIsCallout;
    private bool _closingAfterSave;

    /// <param name="path">The capture file, or null for a capture that isn't saved yet.</param>
    public EditorWindow(CometApp app, Bitmap image, AnnotationDocument doc, string? path)
    {
        _app = app;
        _base = image;
        _doc = doc;
        _doc.Source = image;
        _doc.ConnectSteps = app.Settings.ConstellationSteps;
        _path = path;
        _dirty = path is null;

        Title = TitleText();
        Background = SpaceTheme.VoidBrush;
        Foreground = SpaceTheme.StarWhiteBrush;
        FontFamily = SpaceTheme.UiFont;
        Icon = new WindowIcon(SpaceTheme.Logo);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;

        // ---- surface
        _surface = new EditorSurface(this);
        _textLayer = new Canvas();
        var stack = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(28),
        };
        stack.Children.Add(_surface);
        stack.Children.Add(_textLayer);
        var sheet = new Border
        {
            Child = stack,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 28, Color = Color.FromArgb(0xA0, 0, 0, 0) }),
        };
        _scroller = new ScrollViewer
        {
            Content = sheet,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = SpaceTheme.NebulaBrush,
        };
        _scroller.AddHandler(PointerWheelChangedEvent, OnWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // ---- left tool rail
        var rail = new StackPanel { Margin = new Thickness(6, 8, 6, 8) };
        foreach (var t in EditorTools.All)
        {
            if (t.Tool is EditorTool.Arrow or EditorTool.Text or EditorTool.Highlighter or EditorTool.Blur)
                rail.Children.Add(Ui.Divider(vertical: false));
            var tool = t.Tool;
            Control icon = t.Glyph == "1" ? Ui.StepGlyph() : t.Glyph is not null ? Ui.Glyph(t.Glyph) : Ui.Icon(t.Icon!);
            var b = Ui.LabeledButton(icon, t.Label, t.Tip, () => SetTool(tool), height: 46);
            b.Margin = new Thickness(0, 1);
            _toolButtons[tool] = b;
            rail.Children.Add(b);
        }
        var railBorder = new Border
        {
            Child = new ScrollViewer { Content = rail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            Background = SpaceTheme.VoidBrush,
            BorderBrush = SpaceTheme.NavyLightBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
        };

        // ---- top bar
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = "COMETSNAP", FontFamily = SpaceTheme.HeadingFont, FontSize = 16, Foreground = SpaceTheme.SunBrush,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 14, 0),
        });
        for (var i = 0; i < EditorTools.Palette.Length; i++) left.Children.Add(Swatch(EditorTools.Palette[i], $"{EditorTools.PaletteNames[i]} ({i + 1})"));
        left.Children.Add(Ui.Divider(vertical: true));
        for (var i = 0; i < 3; i++)
        {
            var s = i;
            var b = Ui.Button(new TextBlock { Text = SizeNames[i], FontWeight = FontWeight.Bold, FontSize = 13, Width = 16, TextAlignment = TextAlignment.Center },
                $"Size {SizeNames[i]} ([ and ] change it)", () => SetSize(s), padding: new Thickness(7, 5));
            b.Margin = new Thickness(1, 0);
            _sizeButtons[i] = b;
            left.Children.Add(b);
        }
        left.Children.Add(Ui.Divider(vertical: true));
        left.Children.Add(IconButton("M5,6 H10.5 A3,3 0 0 1 10.5,12 H6 M5,6 L7.5,3.5 M5,6 L7.5,8.5", "Undo (Ctrl+Z)", Undo));
        left.Children.Add(IconButton("M11,6 H5.5 A3,3 0 0 0 5.5,12 H10 M11,6 L8.5,3.5 M11,6 L8.5,8.5", "Redo (Ctrl+Y)", () => { CommitText(); Select(null); _doc.Redo(); }));
        left.Children.Add(IconButton("M3,4.5 H13 M6,4.5 V2.5 H10 V4.5 M4.5,4.5 L5.2,14 H10.8 L11.5,4.5", "Delete the selected shape (Del)", DeleteSelected));

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        // Constellation mode: shown once there are two steps to connect.
        _constellationButton = Ui.ActionButton("Constellation",
            "M3,12 L7,6 L12,9 L14,3 M3,12 m-1.2,0 a1.2,1.2 0 1 0 2.4,0 a1.2,1.2 0 1 0 -2.4,0 M7,6 m-1.2,0 a1.2,1.2 0 1 0 2.4,0 a1.2,1.2 0 1 0 -2.4,0 M12,9 m-1.2,0 a1.2,1.2 0 1 0 2.4,0 a1.2,1.2 0 1 0 -2.4,0",
            "Join the numbered steps with faint star lines", ToggleConstellation);
        _constellationButton.IsVisible = false;
        if (_doc.ConnectSteps) Ui.SetActive(_constellationButton, true);
        right.Children.Add(_constellationButton);
        right.Children.Add(Ui.ActionButton("Save as…", null, "Save a copy as PNG or JPEG (Ctrl+Shift+S)", () => _ = SaveAsAndCloseAsync()));
        right.Children.Add(Ui.ActionButton("Save", "M3,2.5 H11 L13.5,5 V13.5 H3 Z M5.5,2.5 V6 H10 V2.5 M5.5,13.5 V9.5 H10.5 V13.5", "Save and close (Ctrl+S)", SaveAndClose));
        right.Children.Add(Ui.ActionButton("Copy", "M5.5,5.5 H13.5 V13.5 H5.5 Z M2.5,10.5 V2.5 H10.5", "Copy, save and close (Ctrl+C / Ctrl+Enter)", () => _ = CopyAndCloseAsync(), primary: true));

        var top = new DockPanel { Margin = new Thickness(12, 8, 12, 8) };
        DockPanel.SetDock(right, Dock.Right);
        top.Children.Add(right);
        top.Children.Add(new ScrollViewer { Content = left, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var topBorder = new Border { Child = top, Background = SpaceTheme.VoidBrush, BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(0, 0, 0, 1) };

        // ---- status bar
        _status = new TextBlock { Foreground = SpaceTheme.DustBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        _hint = new TextBlock { Foreground = SpaceTheme.DustBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var bottom = new Grid { Margin = new Thickness(12, 5), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        bottom.Children.Add(_status);
        Grid.SetColumn(_hint, 1);
        bottom.Children.Add(_hint);
        var bottomBorder = new Border { Child = bottom, Background = SpaceTheme.VoidBrush, BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(0, 1, 0, 0) };

        // ---- unsaved changes banner
        _confirm = BuildConfirm();

        var root = new DockPanel();
        DockPanel.SetDock(topBorder, Dock.Top);
        DockPanel.SetDock(bottomBorder, Dock.Bottom);
        DockPanel.SetDock(railBorder, Dock.Left);
        root.Children.Add(topBorder);
        root.Children.Add(bottomBorder);
        root.Children.Add(railBorder);
        var centerGrid = new Grid();
        centerGrid.Children.Add(_scroller);
        centerGrid.Children.Add(_confirm);
        root.Children.Add(centerGrid);
        Content = root;

        _doc.Changed += () => { _surface.InvalidateVisual(); UpdateStatus(); UpdateConstellationButton(); };
        UpdateConstellationButton();
        KeyDown += OnKeyDown;
        Closing += OnClosing;

        SizeToImage(image.PixelSize.Width, image.PixelSize.Height);
        SetTool(EditorTool.Arrow);
        SetColor(_color);
        SetSize(_size);
        Opened += (_, _) =>
        {
            SetZoom(FitZoomValue());
            Ui.Entrance((Control)Content!, lift: 10, ms: 200);
            Activate();
        };
    }

    public Bitmap BaseImage => _base;
    public AnnotationDocument Document => _doc;
    public double Zoom => _zoom;
    public Annotation? Drawing => _drawing;
    public Annotation? Selected => _selected;
    public bool IsCropTool => _tool == EditorTool.Crop;

    private string TitleText() => _path is null ? "Cometsnap editor" : $"{System.IO.Path.GetFileName(_path)} · Cometsnap";

    // ------------------------------------------------------------------ layout

    private void SizeToImage(int pw, int ph)
    {
        var screen = Screens.Primary?.WorkingArea ?? new Avalonia.PixelRect(0, 0, 1600, 900);
        var scale = Screens.Primary?.Scaling ?? 1;
        var maxW = screen.Width / scale * 0.9;
        var maxH = screen.Height / scale * 0.9;
        Width = Math.Clamp(pw + 76 + 56 + 2, 900, maxW);
        Height = Math.Clamp(ph + 52 + 30 + 56 + 2, 640, maxH);
    }

    private double FitZoomValue()
    {
        var vw = Math.Max(100, _scroller.Bounds.Width - 60);
        var vh = Math.Max(100, _scroller.Bounds.Height - 60);
        if (vw < 120 || vh < 120) { vw = Width - 140; vh = Height - 140; }
        return Math.Min(1, Math.Min(vw / _base.PixelSize.Width, vh / _base.PixelSize.Height));
    }

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 0.1, 8);
        _surface.Width = _base.PixelSize.Width * _zoom;
        _surface.Height = _base.PixelSize.Height * _zoom;
        _textLayer.Width = _surface.Width;
        _textLayer.Height = _surface.Height;
        if (_textBox is not null) PlaceTextBox();
        _surface.InvalidateVisual();
        UpdateStatus();
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        SetZoom(_zoom * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15));
        e.Handled = true;
    }

    private void UpdateStatus()
    {
        var size = _doc.CropRect is { } c ? $"{(int)c.Width} × {(int)c.Height} (cropped)" : $"{_base.PixelSize.Width} × {_base.PixelSize.Height}";
        _status.Text = $"{size} · {Math.Round(_zoom * 100)}% · {_doc.Items.Count} shape{(_doc.Items.Count == 1 ? "" : "s")}";
    }

    private void UpdateConstellationButton()
    {
        var show = _doc.Items.OfType<StepAnnotation>().Skip(1).Any();
        var was = _constellationButton.IsVisible;
        _constellationButton.IsVisible = show;
        if (show && !was) Ui.Entrance(_constellationButton, lift: 0, ms: 140);
    }

    private void ToggleConstellation()
    {
        _doc.ConnectSteps = !_doc.ConnectSteps;
        Ui.SetActive(_constellationButton, _doc.ConnectSteps);
        _app.Settings.ConstellationSteps = _doc.ConnectSteps;
        _app.SaveSettings();
        _dirty = true;
        _doc.Touch();
        Flash(_doc.ConnectSteps ? "Constellation on: steps are joined by star lines" : "Constellation off");
    }

    private void Flash(string text)
    {
        _hint.Text = text;
        DispatcherTimer.RunOnce(() => _hint.Text = ToolHint(), TimeSpan.FromSeconds(3));
    }

    private string ToolHint() => EditorTools.All.First(t => t.Tool == _tool).Tip + "   ·   Ctrl+C copy · Ctrl+S save";

    private Border Swatch(string color, string tip)
    {
        var dot = new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(9),
            Background = Annotation.CreateBrush(color), BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
        };
        var b = Ui.Button(dot, tip, () => SetColor(color), padding: new Thickness(4), radius: 14);
        b.BorderThickness = new Thickness(2);
        b.BorderBrush = Brushes.Transparent;
        _swatches[color] = b;
        return b;
    }

    private static Border IconButton(string icon, string tip, Action click)
    {
        var b = Ui.Button(Ui.Icon(icon, 18), tip, click, padding: new Thickness(6));
        b.Margin = new Thickness(1, 0);
        return b;
    }

    private Border BuildConfirm()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock { Text = "Save your changes?", FontSize = 14, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        row.Children.Add(Ui.ActionButton("Save", null, "Save and close", () => { HideConfirm(); SaveAndClose(); }, primary: true));
        row.Children.Add(Ui.ActionButton("Don't save", null, "Close without saving", () => { _closingAfterSave = true; Close(); }));
        row.Children.Add(Ui.ActionButton("Cancel", null, "Keep editing", HideConfirm));
        return new Border
        {
            Child = row,
            IsVisible = false,
            Background = SpaceTheme.NavyBrush,
            BorderBrush = SpaceTheme.CometBlueBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 16, 0, 0),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 18, Color = Color.FromArgb(0x90, 0, 0, 0) }),
        };
    }

    private void HideConfirm() => _confirm.IsVisible = false;

    // ------------------------------------------------------------------ tools, colors, sizes

    private void SetTool(EditorTool tool)
    {
        CommitText();
        _tool = tool;
        foreach (var (t, b) in _toolButtons) Ui.SetActive(b, t == tool);
        if (tool != EditorTool.Select) Select(null);
        _surface.Cursor = new Cursor(tool switch
        {
            EditorTool.Select => StandardCursorType.Arrow,
            EditorTool.Text => StandardCursorType.Ibeam,
            EditorTool.Step => StandardCursorType.Hand,
            _ => StandardCursorType.Cross,
        });
        _hint.Text = ToolHint();
    }

    private void SetColor(string color)
    {
        _color = color;
        foreach (var (c, b) in _swatches) b.BorderBrush = c == color ? SpaceTheme.StarWhiteBrush : Brushes.Transparent;
        if (_textBox is not null) _textBox.Foreground = Annotation.CreateBrush(color);
        if (_selected is not null)
        {
            _doc.Checkpoint();
            _selected.Color = color;
            _dirty = true;
            _doc.Touch();
        }
    }

    private void SetSize(int size)
    {
        _size = Math.Clamp(size, 0, 2);
        for (var i = 0; i < 3; i++) Ui.SetActive(_sizeButtons[i], i == _size);
        if (_selected is not null)
        {
            _doc.Checkpoint();
            ApplySize(_selected, _size);
            _dirty = true;
            _doc.Touch();
        }
        if (_textBox is not null) _textBox.FontSize = TextSizes[_size] * _zoom;
    }

    /// <summary>Stroke widths are in screen pixels at 100%: a big capture gets proportionally thicker lines only when zoomed out a lot.</summary>
    private double Stroke(int size) => Sizes[size] * Math.Max(1, Math.Min(_base.PixelSize.Width, _base.PixelSize.Height) / 1200.0);

    private void ApplySize(Annotation a, int size)
    {
        a.Thickness = Stroke(size);
        switch (a)
        {
            case TextAnnotation t: t.FontSize = TextSizes[size] * (Stroke(size) / Sizes[size]); break;
            case CalloutAnnotation c: c.FontSize = TextSizes[size] * (Stroke(size) / Sizes[size]); break;
            case BlurAnnotation b: b.BlockSize = 6 + size * 5; break;
        }
    }

    private void Select(Annotation? a)
    {
        _selected = a;
        _surface.InvalidateVisual();
    }

    private void DeleteSelected()
    {
        if (_selected is null) return;
        _doc.Remove(_selected);
        _selected = null;
        _dirty = true;
    }

    private void Undo()
    {
        if (_textBox is not null) { RemoveTextBox(); return; }
        Select(null);
        _doc.Undo();
        _dirty = true;
    }

    // ------------------------------------------------------------------ mouse (image coordinates)

    public void OnSurfaceDown(Point p, PointerPressedEventArgs e)
    {
        CommitText();
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _dragFrom = p;
        _moved = false;

        if (_tool == EditorTool.Select)
        {
            Select(_doc.HitTest(p, 6 / _zoom));
            if (_selected is not null) _doc.Checkpoint();
            return;
        }

        Annotation? a = _tool switch
        {
            EditorTool.Arrow => new ArrowAnnotation { X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y },
            EditorTool.Line => new LineAnnotation { X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y },
            EditorTool.Rectangle => new RectangleAnnotation { X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y },
            EditorTool.Ellipse => new EllipseAnnotation { X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y },
            EditorTool.Blur => new BlurAnnotation { X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y },
            EditorTool.Spotlight => new SpotlightAnnotation { X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y },
            EditorTool.Crop => new CropAnnotation { X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y },
            EditorTool.Highlighter => new HighlighterAnnotation(),
            EditorTool.Pen => new PenAnnotation(),
            EditorTool.Callout => new LineAnnotation { X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y }, // the tail, while dragging
            _ => null,
        };

        if (_tool == EditorTool.Text)
        {
            StartText(p, callout: false);
            return;
        }
        if (_tool == EditorTool.Step)
        {
            var step = new StepAnnotation { X = p.X, Y = p.Y, Number = _doc.NextStepNumber(), Color = _color };
            ApplySize(step, _size);
            _doc.Add(step);
            _dirty = true;
            return;
        }
        if (a is null) return;
        a.Color = _tool == EditorTool.Highlighter && _color is "#FFFFFF" or "#111111" ? "#FFD60A" : _color;
        ApplySize(a, _size);
        if (a is PenAnnotation pen) pen.AddPoint(p.X, p.Y);
        if (a is CropAnnotation) a.Thickness = 2;
        _drawing = a;
        _ = shift;
    }

    public void OnSurfaceMove(Point p, PointerEventArgs e)
    {
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (_tool == EditorTool.Select && _selected is not null && e.GetCurrentPoint(_surface).Properties.IsLeftButtonPressed)
        {
            var d = p - _dragFrom;
            if (!_moved && Math.Abs(d.X) + Math.Abs(d.Y) < 2) return;
            _moved = true;
            _selected.Offset(d.X, d.Y);
            _dragFrom = p;
            _dirty = true;
            _surface.InvalidateVisual();
            return;
        }
        if (_drawing is null) return;
        switch (_drawing)
        {
            case ArrowAnnotation arrow: arrow.DragArrowTo(p.X, p.Y, shift); break;
            case LineAnnotation line when shift:
                var vx = p.X - line.X1; var vy = p.Y - line.Y1;
                var len = Math.Sqrt(vx * vx + vy * vy);
                var ang = Math.Round(Math.Atan2(vy, vx) / (Math.PI / 4)) * (Math.PI / 4);
                line.X2 = line.X1 + Math.Cos(ang) * len; line.Y2 = line.Y1 + Math.Sin(ang) * len;
                break;
            case BoxAnnotation box: box.DragTo(p.X, p.Y, shift && box is RectangleAnnotation or EllipseAnnotation); break;
            case PenAnnotation pen: pen.AddPoint(p.X, p.Y); break;
        }
        _surface.InvalidateVisual();
    }

    public void OnSurfaceUp(Point p)
    {
        if (_tool == EditorTool.Select)
        {
            if (_selected is not null && !_moved) _doc.Undo(); // nothing changed: drop the checkpoint
            else if (_moved) _doc.Touch();
            if (_selected is not null && !_moved) Select(_doc.HitTest(p, 6 / _zoom));
            return;
        }
        var a = _drawing;
        _drawing = null;
        if (a is null) return;

        if (_tool == EditorTool.Callout && a is LineAnnotation tail)
        {
            // Drag from the target to where the bubble goes (a click puts the bubble just above).
            _calloutTarget = new Point(tail.X1, tail.Y1);
            var at = tail.IsDegenerate ? new Point(tail.X1 + 30, tail.Y1 - 60) : new Point(tail.X2, tail.Y2);
            StartText(at, callout: true);
            return;
        }
        if (a.IsDegenerate) { _surface.InvalidateVisual(); return; }
        if (a is CropAnnotation)
        {
            foreach (var old in _doc.Items.OfType<CropAnnotation>().ToList()) _doc.Remove(old);
        }
        _doc.Add(a);
        _dirty = true;
        if (a is CropAnnotation) Flash("Cropped. Ctrl+Z to undo, or drag again to change it.");
    }

    // ------------------------------------------------------------------ text

    private void StartText(Point at, bool callout)
    {
        RemoveTextBox();
        _textAt = at;
        _textIsCallout = callout;
        _textBox = new TextBox
        {
            MinWidth = 60,
            AcceptsReturn = true,
            Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xC0),
            Foreground = Annotation.CreateBrush(_color),
            BorderBrush = SpaceTheme.CometBlueBrush,
            BorderThickness = new Thickness(1),
            FontFamily = SpaceTheme.UiFont,
            FontWeight = FontWeight.SemiBold,
            FontSize = TextSizes[_size] * (Stroke(_size) / Sizes[_size]) * _zoom,
            Padding = new Thickness(2),
            Watermark = callout ? "Bubble text" : "Type…",
        };
        _textBox.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) { RemoveTextBox(); e.Handled = true; }
            else if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { CommitText(); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _textLayer.Children.Add(_textBox);
        PlaceTextBox();
        Dispatcher.UIThread.Post(() => _textBox?.Focus(), DispatcherPriority.Input);
    }

    private void PlaceTextBox()
    {
        if (_textBox is null) return;
        Canvas.SetLeft(_textBox, _textAt.X * _zoom - 3);
        Canvas.SetTop(_textBox, _textAt.Y * _zoom - 3);
    }

    private void CommitText()
    {
        if (_textBox is null) return;
        var text = _textBox.Text ?? "";
        RemoveTextBox();
        if (string.IsNullOrWhiteSpace(text)) return;
        Annotation a = _textIsCallout
            ? new CalloutAnnotation { X = _textAt.X, Y = _textAt.Y, TargetX = _calloutTarget.X, TargetY = _calloutTarget.Y, Text = text, Color = _color }
            : new TextAnnotation { X = _textAt.X, Y = _textAt.Y, Text = text, Color = _color };
        ApplySize(a, _size);
        _doc.Add(a);
        _dirty = true;
    }

    private void RemoveTextBox()
    {
        if (_textBox is null) return;
        _textLayer.Children.Remove(_textBox);
        _textBox = null;
        Focus();
    }

    // ------------------------------------------------------------------ keyboard

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_textBox is not null && _textBox.IsFocused) return;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        e.Handled = true;
        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.Z when shift: CommitText(); Select(null); _doc.Redo(); return;
                case Key.Z: Undo(); return;
                case Key.Y: CommitText(); Select(null); _doc.Redo(); return;
                case Key.C or Key.Enter: _ = CopyAndCloseAsync(); return;
                case Key.S when shift: _ = SaveAsAndCloseAsync(); return;
                case Key.S: SaveAndClose(); return;
                case Key.D0 or Key.NumPad0: SetZoom(FitZoomValue()); return;
                case Key.D1 or Key.NumPad1: SetZoom(1); return;
                case Key.OemPlus or Key.Add: SetZoom(_zoom * 1.25); return;
                case Key.OemMinus or Key.Subtract: SetZoom(_zoom * 0.8); return;
            }
            e.Handled = false;
            return;
        }
        switch (e.Key)
        {
            case Key.Delete or Key.Back when _selected is not null: DeleteSelected(); return;
            case Key.Escape:
                if (_selected is not null) Select(null);
                else Close();
                return;
            case Key.OemOpenBrackets: SetSize(_size - 1); return;
            case Key.OemCloseBrackets: SetSize(_size + 1); return;
            case >= Key.D1 and <= Key.D8: SetColor(EditorTools.Palette[e.Key - Key.D1]); return;
            case >= Key.NumPad1 and <= Key.NumPad8: SetColor(EditorTools.Palette[e.Key - Key.NumPad1]); return;
            case Key.Left or Key.Right or Key.Up or Key.Down when _selected is not null:
                var step = shift ? 10 : 1;
                _doc.Checkpoint();
                _selected.Offset(e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0, e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0);
                _dirty = true;
                _doc.Touch();
                return;
        }
        var tool = EditorTools.All.FirstOrDefault(t => t.Key == e.Key);
        if (tool is not null && e.KeyModifiers == KeyModifiers.None) { SetTool(tool.Tool); return; }
        e.Handled = false;
    }

    // ------------------------------------------------------------------ output

    private Bitmap Flattened()
    {
        CommitText();
        return _doc.Flatten(_base);
    }

    /// <summary>Copy is the end of the job: copy, keep the edit on disk, close.</summary>
    private async Task CopyAndCloseAsync()
    {
        var image = Flattened();
        var png = CaptureStore.EncodePng(image);
        if (!await _app.Clipboard.SetPngAsync(png))
        {
            Flash("Could not copy, try again");
            return;
        }
        if (_dirty && !Save(image, png)) return;
        _app.Toast("Copied to clipboard ✓", 1500);
        CloseNow();
    }

    private void SaveAndClose()
    {
        var image = Flattened();
        if (!Save(image, CaptureStore.EncodePng(image))) return;
        _app.Toast("Saved ✓", 1500);
        CloseNow();
    }

    private async Task SaveAsAndCloseAsync()
    {
        var image = Flattened();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save a copy",
            SuggestedFileName = (_path is null ? SnapNaming.BaseName(DateTime.Now) : System.IO.Path.GetFileNameWithoutExtension(_path) + "-edited") + ".png",
            DefaultExtension = "png",
            FileTypeChoices = new[] { new FilePickerFileType("PNG image") { Patterns = new[] { "*.png" } } },
        });
        if (file is null) return;
        try
        {
            var png = CaptureStore.EncodePng(image);
            await using (var s = await file.OpenWriteAsync())
                await s.WriteAsync(png);
            if (_dirty) Save(image, png);
            CloseNow();
        }
        catch (Exception ex)
        {
            Log.Error("Editor save as failed", ex);
            Flash($"Could not save: {ex.Message}");
        }
    }

    private void CloseNow()
    {
        _closingAfterSave = true;
        Close();
    }

    /// <summary>Saves over the capture (keeping the shapes editable), or as a new capture.</summary>
    private bool Save(Bitmap image, byte[] png)
    {
        try
        {
            var path = _path ?? SnapNaming.NextFreePath(_app.CapturesFolder, DateTime.Now);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            if (_doc.IsEmpty)
            {
                EditSidecar.DeleteFor(path);
            }
            else
            {
                EditSidecar.EnsureFolder(path);
                var original = EditSidecar.OriginalFor(path);
                if (!File.Exists(original)) CaptureStore.WriteAtomic(original, CaptureStore.EncodePng(_base));
            }
            CaptureStore.WriteAtomic(path, png);
            if (!_doc.IsEmpty) File.WriteAllText(EditSidecar.AnnotationsFor(path), _doc.ToJson());
            _path = path;
            _dirty = false;
            Title = TitleText();
            _app.Thumbs.Forget(path);
            _app.Recent.Add(path);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Editor save failed", ex);
            Flash($"Could not save: {ex.Message}");
            return false;
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        CommitText();
        if (_closingAfterSave || !_dirty || (_doc.IsEmpty && _path is not null)) return;
        e.Cancel = true;
        _confirm.IsVisible = true;
    }
}

/// <summary>The drawing surface: the capture and its shapes, scaled by the zoom.</summary>
internal sealed class EditorSurface : Control
{
    private static readonly IPen SelectionPen = new ImmutablePen(SpaceTheme.CometBlueBrush, 1.5, new ImmutableDashStyle(new double[] { 4, 3 }, 0));
    private readonly EditorWindow _editor;

    public EditorSurface(EditorWindow editor)
    {
        _editor = editor;
        ClipToBounds = true;
        Focusable = true;
    }

    private Point ToImage(Point p) => new(p.X / _editor.Zoom, p.Y / _editor.Zoom);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Pointer.Capture(this);
        _editor.OnSurfaceDown(ToImage(e.GetPosition(this)), e);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _editor.OnSurfaceMove(ToImage(e.GetPosition(this)), e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        e.Pointer.Capture(null);
        _editor.OnSurfaceUp(ToImage(e.GetPosition(this)));
        InvalidateVisual();
    }

    public override void Render(DrawingContext dc)
    {
        var img = _editor.BaseImage;
        var z = _editor.Zoom;
        using (dc.PushTransform(Matrix.CreateScale(z, z)))
        {
            using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = z >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality }))
                dc.DrawImage(img, new Rect(0, 0, img.PixelSize.Width, img.PixelSize.Height));
            _editor.Document.Render(dc, _editor.Drawing, editing: true);
        }
        if (_editor.Selected is { } s)
        {
            var b = s.Bounds;
            dc.DrawRectangle(null, SelectionPen, new Rect(b.X * z - 4, b.Y * z - 4, b.Width * z + 8, b.Height * z + 8), 3, 3);
        }
    }
}
