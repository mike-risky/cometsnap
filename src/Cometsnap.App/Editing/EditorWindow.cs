using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Cometsnap.App.Services;
using Cometsnap.App.Theme;
using Cometsnap.Core.Diagnostics;
using Cometsnap.Core.Native;
using Cometsnap.Core.Storage;
using Cometsnap.Editor;
using Cometsnap.Core.Localization;

namespace Cometsnap.App.Editing;

/// <summary>
/// The editor: a capture with its shapes, still editable. A big labeled tool rail on the left,
/// colors, sizes and actions on top. Copy and Save finish the job and close the window.
/// The surface works in image pixels; zoom is a LayoutTransform on top.
/// </summary>
public sealed class EditorWindow : Window
{
    private static readonly double[] Sizes = { 2, 4, 7 };          // stroke, in screen pixels at 100%
    private static readonly double[] TextSizes = { 14, 20, 30 };
    private static readonly string[] SizeNames = { "S", "M", "L" };

    private readonly CometApp _app;
    private readonly BitmapSource _base;
    private readonly AnnotationDocument _doc;
    private readonly Canvas _surface;
    private readonly AnnotationLayer _layer;
    private readonly ScaleTransform _zoomTransform = new(1, 1);
    private readonly ScrollViewer _scroller;
    private readonly TextBlock _status;
    private readonly TextBlock _hint;
    private readonly DispatcherTimer _hintTimer;
    private readonly Dictionary<EditorTool, Border> _toolButtons = new();
    private readonly Dictionary<string, Border> _swatches = new();
    private readonly Border[] _sizeButtons = new Border[3];
    private readonly Border _deleteButton;
    private readonly Border _constellationButton;
    private readonly Grid _root;

    private string? _path;
    private bool _dirty;
    private double _zoom = 1;       // 1 = one image pixel per screen pixel
    private double _dpi = 1;
    private EditorTool _tool = EditorTool.Arrow;
    private string _color = EditorUi.Palette[0];
    private int _size = 1;

    private Annotation? _drawing;    // shape being dragged out
    private Annotation? _selected;
    private Point _dragFrom;
    private bool _moved;
    private Point _calloutTarget;
    private TextBox? _textBox;
    private Point _textAt;
    private bool _textIsCallout;
    private bool _closingAfterSave;

    /// <param name="path">The capture file, or null for a new capture that isn't saved yet.</param>
    public EditorWindow(CometApp app, BitmapSource image, AnnotationDocument doc, string? path)
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
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/cometsnap.ico")); }
        catch { /* no icon is fine */ }

        // ---- surface
        var pw = image.PixelWidth;
        var ph = image.PixelHeight;
        var picture = new Image { Source = image, Width = pw, Height = ph, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        _layer = new AnnotationLayer { Document = _doc, Width = pw, Height = ph, Editing = true };
        _surface = new Canvas
        {
            Width = pw, Height = ph, Background = Brushes.Transparent, ClipToBounds = true,
            Cursor = Cursors.Cross, LayoutTransform = _zoomTransform,
        };
        _surface.Children.Add(picture);
        _surface.Children.Add(_layer);
        _surface.MouseLeftButtonDown += OnSurfaceDown;
        _surface.MouseMove += OnSurfaceMove;
        _surface.MouseLeftButtonUp += OnSurfaceUp;

        var sheet = new Border
        {
            Child = _surface, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(28), BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 28, ShadowDepth = 0, Opacity = 0.65, Color = Colors.Black },
        };
        _scroller = new ScrollViewer
        {
            Content = sheet, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = SpaceTheme.NebulaBrush, Focusable = false,
        };
        _scroller.PreviewMouseWheel += OnWheel;

        // ---- left tool rail
        var rail = new StackPanel { Margin = new Thickness(6, 8, 6, 8) };
        foreach (var t in EditorUi.Tools)
        {
            if (t.Tool is EditorTool.Arrow or EditorTool.Text or EditorTool.Highlighter or EditorTool.Blur)
                rail.Children.Add(EditorUi.Divider(vertical: false));
            var tool = t.Tool;
            var b = EditorUi.LabeledButton(EditorUi.Icon(t.Icon, t.Glyph), t.Label, $"{t.Tip}", () => SetTool(tool));
            _toolButtons[tool] = b;
            rail.Children.Add(b);
        }
        var railScroll = new ScrollViewer { Content = rail, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        var railBorder = new Border { Child = railScroll, Background = SpaceTheme.VoidBrush, BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(0, 0, 1, 0) };

        // ---- top bar
        var top = new DockPanel { Margin = new Thickness(12, 8, 12, 8), LastChildFill = false };
        var wordmark = new TextBlock
        {
            Text = "COMETSNAP", FontFamily = SpaceTheme.HeadingFont, FontSize = 16, Foreground = SpaceTheme.SunBrush,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 14, 0),
        };
        top.Children.Add(wordmark);

        var colors = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < EditorUi.Palette.Length; i++) colors.Children.Add(Swatch(EditorUi.Palette[i], $"{EditorUi.PaletteNames[i]} ({i + 1})"));
        top.Children.Add(colors);
        top.Children.Add(EditorUi.Divider(vertical: true));

        var sizes = new Border
        {
            CornerRadius = new CornerRadius(9), BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(2),
        };
        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            var dot = new System.Windows.Shapes.Ellipse { Width = 4 + i * 4, Height = 4 + i * 4, Fill = SpaceTheme.StarWhiteBrush, VerticalAlignment = VerticalAlignment.Center };
            var cell = new StackPanel { Orientation = Orientation.Horizontal };
            cell.Children.Add(dot);
            cell.Children.Add(new TextBlock { Text = SizeNames[i], Margin = new Thickness(5, 0, 0, 0), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = SpaceTheme.StarWhiteBrush });
            var b = new Border
            {
                Child = cell, Height = 30, Padding = new Thickness(9, 0, 10, 0), CornerRadius = new CornerRadius(7), Cursor = Cursors.Hand,
                ToolTip = L.F("Size {0}  ([ and ] to change)", SizeNames[i]), Background = new SolidColorBrush(Colors.Transparent),
            };
            b.MouseLeftButtonDown += (_, e) => { e.Handled = true; EditorUi.Press(b); };
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; SetSize(index); };
            _sizeButtons[i] = b;
            sizeRow.Children.Add(b);
        }
        sizes.Child = sizeRow;
        top.Children.Add(sizes);
        top.Children.Add(EditorUi.Divider(vertical: true));

        top.Children.Add(EditorUi.PillButton(EditorUi.Icon("M5.5,4 L2.5,7 L5.5,10 M2.5,7 H10 A3.5,3.5 0 0 1 10,14 H7", null, 16), L.T("Undo"), L.T("Undo (Ctrl+Z)"), Undo));
        top.Children.Add(EditorUi.PillButton(EditorUi.Icon("M10.5,4 L13.5,7 L10.5,10 M13.5,7 H6 A3.5,3.5 0 0 0 6,14 H9", null, 16), L.T("Redo"), L.T("Redo (Ctrl+Y)"), () => { CommitText(); Select(null); _doc.Redo(); }));
        _deleteButton = EditorUi.PillButton(EditorUi.Icon("M3,4.5 H13 M6,4.5 V2.5 H10 V4.5 M4.5,4.5 L5.2,14 H10.8 L11.5,4.5", null, 16), L.T("Delete"), L.T("Delete the selected shape (Del)"), DeleteSelected);
        _deleteButton.Visibility = Visibility.Collapsed;
        top.Children.Add(_deleteButton);

        // Constellation mode: shown once there are two steps to connect.
        _constellationButton = EditorUi.PillButton(EditorUi.Icon("M3,12 L7,6 L12,9 L14,3 M3,12 m-1.2,0 a1.2,1.2 0 1 0 2.4,0 a1.2,1.2 0 1 0 -2.4,0 M7,6 m-1.2,0 a1.2,1.2 0 1 0 2.4,0 a1.2,1.2 0 1 0 -2.4,0 M12,9 m-1.2,0 a1.2,1.2 0 1 0 2.4,0 a1.2,1.2 0 1 0 -2.4,0", null, 16),
            L.T("Constellation"), L.T("Join the numbered steps with faint star lines"), ToggleConstellation);
        _constellationButton.Visibility = Visibility.Collapsed;
        if (_doc.ConnectSteps) EditorUi.SetActive(_constellationButton, true);
        top.Children.Add(_constellationButton);

        var copyText = EditorUi.PillButton(EditorUi.Icon("M3,3.5 H13 M8,3.5 V13.5 M6,13.5 H10", null, 16), L.T("Copy text"), L.T("Copy the text in this image (Ctrl+Shift+C)"), CopyText);
        DockPanel.SetDock(copyText, Dock.Right);
        var copy = EditorUi.PillButton(EditorUi.Icon("M5.5,5.5 H13.5 V13.5 H5.5 Z M2.5,10.5 V2.5 H10.5", null, 16), L.T("Copy & close"), L.T("Copy to the clipboard, save, and close (Ctrl+C or Ctrl+Enter)"), CopyAndClose, SpaceTheme.TerracottaBrush, bold: true);
        var save = EditorUi.PillButton(EditorUi.Icon("M8,2.5 V10 M4.5,6.5 L8,10 L11.5,6.5 M2.5,13.5 H13.5", null, 16), L.T("Save"), L.T("Save and close (Ctrl+S)"), SaveAndClose, SpaceTheme.NavyBrush);
        var saveAs = EditorUi.PillButton(EditorUi.Icon("M2.5,2.5 H11 L13.5,5 V13.5 H2.5 Z M5,2.5 V6 H10 V2.5 M5,13.5 V9.5 H11 V13.5", null, 16), L.T("Save as…"), L.T("Save a copy somewhere else (Ctrl+Shift+S)"), SaveAsAndClose);
        DockPanel.SetDock(copy, Dock.Right);
        DockPanel.SetDock(save, Dock.Right);
        DockPanel.SetDock(saveAs, Dock.Right);
        top.Children.Add(copy);
        top.Children.Add(save);
        top.Children.Add(saveAs);
        top.Children.Add(copyText);

        // ---- footer
        _status = new TextBlock { Foreground = SpaceTheme.DustBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        _hint = new TextBlock { Foreground = SpaceTheme.SunBrush, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var footer = new Grid { Margin = new Thickness(14, 6, 14, 8) };
        footer.Children.Add(_status);
        footer.Children.Add(_hint);
        _hintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _hintTimer.Tick += (_, _) =>
        {
            _hintTimer.Stop();
            _hint.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
        };

        var topBorder = new Border { Background = SpaceTheme.VoidBrush, BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(0, 0, 0, 1), Child = top };
        var bottom = new Border { Background = SpaceTheme.VoidBrush, BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
        var dock = new DockPanel();
        DockPanel.SetDock(topBorder, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(railBorder, Dock.Left);
        dock.Children.Add(topBorder);
        dock.Children.Add(bottom);
        dock.Children.Add(railBorder);
        dock.Children.Add(_scroller);
        _root = new Grid();
        _root.Children.Add(dock);
        Content = _root;

        _doc.Changed += () => { _dirty = true; Title = TitleText(); UpdateStatus(); UpdateConstellationButton(); };
        UpdateConstellationButton();
        PreviewKeyDown += OnKeyDown;
        SourceInitialized += (_, _) => ApplyDarkFrame();
        Loaded += (_, _) => OnFirstLayout();
        Closing += OnClosing;

        SizeToImage(pw, ph);
        SetTool(_tool);
        SetColor(_color);
        SetSize(_size);
    }

    /// <summary>Opens a saved capture, with its shapes if it was edited before.</summary>
    public static EditorWindow? OpenFile(CometApp app, string path)
    {
        try
        {
            var original = EditSidecar.OriginalFor(path);
            var annotations = EditSidecar.AnnotationsFor(path);
            BitmapSource image;
            AnnotationDocument doc;
            if (File.Exists(original) && File.Exists(annotations)
                && File.GetLastWriteTimeUtc(annotations) >= File.GetLastWriteTimeUtc(path).AddSeconds(-5))
            {
                image = LoadImage(original);
                doc = AnnotationDocument.FromJson(File.ReadAllText(annotations));
            }
            else
            {
                // Never edited, or changed by another program since: start from the file itself.
                image = LoadImage(path);
                doc = new AnnotationDocument();
            }
            return new EditorWindow(app, image, doc, path);
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open {path} in the editor", ex);
            MessageBox.Show(L.T("Could not open this capture:") + "\n" + ex.Message, "Cometsnap", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    private static BitmapSource LoadImage(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    // ------------------------------------------------------------------ layout & zoom

    private void SizeToImage(int pw, int ph)
    {
        var work = SystemParameters.WorkArea;
        Width = Math.Clamp(pw + 160.0, 900, work.Width * 0.92);
        Height = Math.Clamp(ph + 190.0, 640, work.Height * 0.92);
    }

    private void OnFirstLayout()
    {
        _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        SetZoom(Math.Min(1, FitZoomValue()));
        SetSize(_size);
        EditorUi.Entrance(_root, lift: 10, ms: 180);
        Activate();
        Keyboard.Focus(this);
    }

    private double FitZoomValue()
    {
        var availW = Math.Max(100, _scroller.ViewportWidth - 60) * _dpi;
        var availH = Math.Max(100, _scroller.ViewportHeight - 60) * _dpi;
        return Math.Min(availW / _base.PixelWidth, availH / _base.PixelHeight);
    }

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 0.05, 16);
        var scale = _zoom / _dpi;
        // A short ease makes zooming feel smooth without slowing anything down.
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _zoomTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, TimeSpan.FromMilliseconds(110)) { EasingFunction = ease });
        _zoomTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, TimeSpan.FromMilliseconds(110)) { EasingFunction = ease });
        _layer.PixelSize = 1 / _zoom;
        _layer.InvalidateVisual();
        UpdateStatus();
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        e.Handled = true;
        SetZoom(_zoom * (e.Delta > 0 ? 1.25 : 0.8));
    }

    private void UpdateStatus()
    {
        var name = _path is null ? L.T("New capture") : Path.GetFileName(_path);
        var tool = EditorUi.Tools.First(t => t.Tool == _tool);
        _status.Text = $"{name}   ·   {_base.PixelWidth} × {_base.PixelHeight}   ·   {Math.Round(_zoom * 100)}%   ·   {tool.Tip}";
    }

    private string TitleText() =>
        $"{(_dirty ? "• " : "")}{(_path is null ? L.T("New capture") : Path.GetFileName(_path))} - Cometsnap";

    private void Flash(string text)
    {
        _hint.Text = text;
        _hint.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        _hintTimer.Stop();
        _hintTimer.Start();
    }

    // ------------------------------------------------------------------ tools, colors, sizes

    private double Dpi => _dpi <= 0 ? 1 : _dpi;
    private double StrokeFor(int size) => Math.Max(1.5, Math.Round(Sizes[size] * Dpi));
    private double TextSize(int size) => Math.Round(TextSizes[size] * Dpi);

    private void SetTool(EditorTool tool)
    {
        CommitText();
        if (tool != EditorTool.Select) Select(null);
        _tool = tool;
        foreach (var (t, b) in _toolButtons) EditorUi.SetActive(b, t == tool);
        _surface.Cursor = tool switch
        {
            EditorTool.Select => Cursors.Arrow,
            EditorTool.Text or EditorTool.Callout => Cursors.IBeam,
            EditorTool.Step => Cursors.Hand,
            _ => Cursors.Cross,
        };
        // The marker looks best in yellow: suggest it once, when the color is still the default red.
        if (tool == EditorTool.Highlighter && _color == EditorUi.Palette[0]) SetColor("#FFD60A");
        UpdateStatus();
    }

    private Border Swatch(string color, string tip)
    {
        var dot = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
            BorderBrush = SpaceTheme.DustBrush, BorderThickness = new Thickness(color == "#111111" ? 1 : 0),
        };
        var ring = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent, Child = dot, Margin = new Thickness(1, 0, 1, 0), Cursor = Cursors.Hand,
            ToolTip = tip, Background = Brushes.Transparent, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1),
        };
        ring.MouseEnter += (_, _) => Grow(ring, 1.12);
        ring.MouseLeave += (_, _) => Grow(ring, 1);
        ring.MouseLeftButtonDown += (_, e) => e.Handled = true;
        ring.MouseLeftButtonUp += (_, e) => { e.Handled = true; SetColor(color); };
        _swatches[color] = ring;
        return ring;
    }

    private static void Grow(FrameworkElement e, double to)
    {
        if (e.RenderTransform is not ScaleTransform st) return;
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(110));
        st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    private void SetColor(string color)
    {
        _color = color;
        foreach (var (c, b) in _swatches)
            b.BorderBrush = string.Equals(c, color, StringComparison.OrdinalIgnoreCase) ? SpaceTheme.StarWhiteBrush : Brushes.Transparent;
        if (_textBox is not null) _textBox.Foreground = Annotation.CreateBrush(color);
        if (_selected is not null && !string.Equals(_selected.Color, color, StringComparison.OrdinalIgnoreCase))
        {
            _doc.Checkpoint();
            _selected.Color = color;
            _doc.Touch();
            _layer.InvalidateVisual();
        }
    }

    private void SetSize(int size)
    {
        _size = Math.Clamp(size, 0, 2);
        for (var i = 0; i < 3; i++)
            EditorUi.AnimateBackground(_sizeButtons[i], i == _size ? EditorUi.ActiveBrush.Color : Colors.Transparent, hover: false);
        if (_textBox is not null) _textBox.FontSize = TextSize(_size);
        if (_selected is not null)
        {
            _doc.Checkpoint();
            ApplySize(_selected, _size);
            _doc.Touch();
            _layer.InvalidateVisual();
        }
    }

    private void ApplySize(Annotation a, int size)
    {
        switch (a)
        {
            case TextAnnotation t: t.FontSize = TextSize(size); break;
            case CalloutAnnotation c: c.FontSize = TextSize(size); break;
            case BlurAnnotation b: b.BlockSize = BlockSize(size); break;
            default: a.Thickness = StrokeFor(size); break;
        }
    }

    private double BlockSize(int size) => Math.Round(new[] { 6.0, 10.0, 16.0 }[size] * Dpi);

    private void Select(Annotation? a)
    {
        _selected = a;
        _layer.Selected = a;
        _deleteButton.Visibility = a is null ? Visibility.Collapsed : Visibility.Visible;
        if (a is not null) EditorUi.Entrance(_deleteButton, lift: 0, ms: 120);
    }

    private void DeleteSelected()
    {
        if (_selected is null) return;
        var a = _selected;
        Select(null);
        _doc.Remove(a);
    }

    private void Undo()
    {
        CommitText();
        Select(null);
        _doc.Undo();
    }

    // ------------------------------------------------------------------ mouse

    private double Tolerance => 6 / Math.Max(0.05, _zoom);

    private void OnSurfaceDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var p = e.GetPosition(_surface);
        CommitText();
        _dragFrom = p;
        _moved = false;

        switch (_tool)
        {
            case EditorTool.Select:
                Select(_doc.HitTest(p, Tolerance));
                if (_selected is not null) _surface.CaptureMouse();
                return;

            case EditorTool.Text:
                StartText(p, callout: false);
                return;

            case EditorTool.Step:
                var step = new StepAnnotation { X = p.X, Y = p.Y, Number = _doc.NextStepNumber(), Color = _color, Thickness = StrokeFor(_size) };
                _doc.Add(step);
                return;

            case EditorTool.Callout:
                _calloutTarget = p;
                _drawing = new LineAnnotation { X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y, Color = _color, Thickness = Math.Max(1.5, StrokeFor(0)) };
                _layer.InProgress = _drawing;
                _surface.CaptureMouse();
                return;

            case EditorTool.Pen:
            case EditorTool.Highlighter:
                var pen = _tool == EditorTool.Pen ? new PenAnnotation() : new HighlighterAnnotation();
                pen.Color = _color;
                pen.Thickness = StrokeFor(_size);
                pen.AddPoint(p.X, p.Y);
                _drawing = pen;
                _layer.InProgress = pen;
                _surface.CaptureMouse();
                return;
        }

        BoxAnnotation box = _tool switch
        {
            EditorTool.Ellipse => new EllipseAnnotation(),
            EditorTool.Rectangle => new RectangleAnnotation(),
            EditorTool.Line => new LineAnnotation(),
            EditorTool.Blur => new BlurAnnotation { BlockSize = BlockSize(_size) },
            EditorTool.Spotlight => new SpotlightAnnotation(),
            EditorTool.Crop => new CropAnnotation(),
            _ => new ArrowAnnotation(),
        };
        box.Color = _color;
        box.Thickness = StrokeFor(_size);
        box.X1 = box.X2 = p.X;
        box.Y1 = box.Y2 = p.Y;
        _drawing = box;
        _layer.InProgress = box;
        _surface.CaptureMouse();
    }

    private void OnSurfaceMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(_surface);
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (_tool == EditorTool.Select)
        {
            if (_selected is null || e.LeftButton != MouseButtonState.Pressed)
            {
                _surface.Cursor = _doc.HitTest(p, Tolerance) is null ? Cursors.Arrow : Cursors.SizeAll;
                return;
            }
            var d = p - _dragFrom;
            if (!_moved && d.Length < 2) return;
            if (!_moved) _doc.Checkpoint();
            _moved = true;
            _selected.Offset(d.X, d.Y);
            _dragFrom = p;
            _layer.InvalidateVisual();
            return;
        }

        switch (_drawing)
        {
            case null:
                return;
            case PenAnnotation pen:
                pen.AddPoint(p.X, p.Y);
                break;
            case ArrowAnnotation arrow:
                arrow.DragArrowTo(p.X, p.Y, shift);
                break;
            case LineAnnotation line when shift:
                // Lines snap to 45° like arrows.
                var v = p - new Point(line.X1, line.Y1);
                var angle = Math.Round(Math.Atan2(v.Y, v.X) / (Math.PI / 4)) * (Math.PI / 4);
                line.X2 = line.X1 + Math.Cos(angle) * v.Length;
                line.Y2 = line.Y1 + Math.Sin(angle) * v.Length;
                break;
            case BoxAnnotation box:
                box.DragTo(p.X, p.Y, shift && box is not (BlurAnnotation or SpotlightAnnotation or CropAnnotation or LineAnnotation));
                break;
        }
        _layer.InProgress = _drawing;
    }

    private void OnSurfaceUp(object sender, MouseButtonEventArgs e)
    {
        _surface.ReleaseMouseCapture();
        if (_tool == EditorTool.Select)
        {
            if (_moved) _doc.Touch();
            _moved = false;
            return;
        }
        if (_drawing is null) return;
        e.Handled = true;
        var done = _drawing;
        _drawing = null;
        _layer.InProgress = null;

        if (_tool == EditorTool.Callout)
        {
            var p = e.GetPosition(_surface);
            var bubbleAt = (p - _calloutTarget).Length < 12 ? _calloutTarget + new Vector(30 * Dpi, -50 * Dpi) : p;
            StartText(bubbleAt, callout: true);
            return;
        }
        if (done.IsDegenerate) return;
        _doc.Add(done);
        if (done is CropAnnotation) Flash(L.T("Cropped. Undo (Ctrl+Z) to bring it back"));
    }

    // ------------------------------------------------------------------ text & callouts

    private void StartText(Point at, bool callout)
    {
        RemoveTextBox();
        _textAt = at;
        _textIsCallout = callout;
        _textBox = new TextBox
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            FontSize = TextSize(_size),
            Foreground = callout ? Brushes.White : Annotation.CreateBrush(_color),
            Background = callout ? Annotation.CreateBrush(_color) : SpaceTheme.WithAlpha(SpaceTheme.Void, 0x66),
            CaretBrush = SpaceTheme.StarWhiteBrush,
            BorderBrush = SpaceTheme.CometBlueBrush,
            BorderThickness = new Thickness(Math.Max(1, Math.Round(Dpi))),
            Padding = new Thickness(0),
            MinWidth = 40 * Dpi,
            AcceptsReturn = true,
        };
        _textBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape || (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0))
            {
                e.Handled = true;
                CommitText();
            }
        };
        var inset = _textBox.BorderThickness.Left + 2;
        Canvas.SetLeft(_textBox, at.X - inset);
        Canvas.SetTop(_textBox, at.Y - inset);
        _surface.Children.Add(_textBox);
        _textBox.Focus();
        Keyboard.Focus(_textBox);
    }

    private void CommitText()
    {
        if (_textBox is null) return;
        var text = _textBox.Text;
        var size = _textBox.FontSize;
        RemoveTextBox();
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (_textIsCallout)
                _doc.Add(new CalloutAnnotation { TargetX = _calloutTarget.X, TargetY = _calloutTarget.Y, X = _textAt.X, Y = _textAt.Y, Text = text, FontSize = size, Color = _color });
            else
                _doc.Add(new TextAnnotation { X = _textAt.X, Y = _textAt.Y, Text = text, FontSize = size, Color = _color });
        }
        Keyboard.Focus(this);
    }

    private void RemoveTextBox()
    {
        if (_textBox is null) return;
        _surface.Children.Remove(_textBox);
        _textBox = null;
    }

    /// <summary>Reads the text in the image (Windows OCR) and puts it on the clipboard. The editor stays open.</summary>
    private async void CopyText()
    {
        CommitText();
        Flash(L.T("Reading the text…"));
        var outcome = await _app.CopyTextAsync(Flattened(), null);
        if (outcome is null) return;
        Flash(outcome.EngineMissing ? L.T("Text recognition is not installed")
            : outcome.Found ? L.T("Text copied") : L.T("No text found"));
    }

    // ------------------------------------------------------------------ keyboard

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_textBox is not null && _textBox.IsKeyboardFocusWithin) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        e.Handled = true;

        if (ctrl)
        {
            switch (key)
            {
                case Key.Z: Undo(); return;
                case Key.Y: CommitText(); Select(null); _doc.Redo(); return;
                case Key.C when shift: CopyText(); return;
                case Key.C or Key.Enter: CopyAndClose(); return;
                case Key.S when shift: SaveAsAndClose(); return;
                case Key.S: SaveAndClose(); return;
                case Key.D0: SetZoom(FitZoomValue()); return;
                case Key.D1: SetZoom(1); return;
                case Key.OemPlus or Key.Add: SetZoom(_zoom * 1.25); return;
                case Key.OemMinus or Key.Subtract: SetZoom(_zoom * 0.8); return;
            }
            e.Handled = false;
            return;
        }

        switch (key)
        {
            case Key.Delete or Key.Back when _selected is not null: DeleteSelected(); return;
            case Key.Escape:
                if (_selected is not null) Select(null);
                else Close();
                return;
            case Key.OemOpenBrackets: SetSize(_size - 1); return;
            case Key.OemCloseBrackets: SetSize(_size + 1); return;
            case >= Key.D1 and <= Key.D8: SetColor(EditorUi.Palette[key - Key.D1]); return;
            case >= Key.NumPad1 and <= Key.NumPad8: SetColor(EditorUi.Palette[key - Key.NumPad1]); return;
            case Key.Left or Key.Right or Key.Up or Key.Down when _selected is not null:
                var step = shift ? 10 : 1;
                _doc.Checkpoint();
                _selected.Offset(key == Key.Left ? -step : key == Key.Right ? step : 0, key == Key.Up ? -step : key == Key.Down ? step : 0);
                _doc.Touch();
                _layer.InvalidateVisual();
                return;
        }

        var tool = EditorUi.Tools.FirstOrDefault(t => t.Key == key);
        if (tool is not null)
        {
            SetTool(tool.Tool);
            if (_toolButtons.TryGetValue(tool.Tool, out var b)) EditorUi.Press(b);
            return;
        }
        e.Handled = false;
    }

    // ------------------------------------------------------------------ output

    private void UpdateConstellationButton()
    {
        var show = _doc.Items.OfType<StepAnnotation>().Skip(1).Any();
        var was = _constellationButton.Visibility == Visibility.Visible;
        _constellationButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show && !was) EditorUi.Entrance(_constellationButton, lift: 0, ms: 140);
    }

    private void ToggleConstellation()
    {
        _doc.ConnectSteps = !_doc.ConnectSteps;
        EditorUi.SetActive(_constellationButton, _doc.ConnectSteps);
        _app.Settings.ConstellationSteps = _doc.ConnectSteps;
        _app.TrySaveSettings();
        _doc.Touch();
        Flash(_doc.ConnectSteps ? L.T("Constellation on: steps are joined by star lines") : L.T("Constellation off"));
    }

    private BitmapSource Flattened()
    {
        CommitText();
        return _doc.Flatten(_base, new Point(0, 0));
    }

    /// <summary>Copy is the end of the job: copy, keep the edit on disk, close.</summary>
    private void CopyAndClose()
    {
        var image = Flattened();
        if (!ClipboardService.SetImage(image))
        {
            Flash(L.T("Could not copy, try again"));
            return;
        }
        if (_dirty && !Save(image)) return;
        FlyHome();
        CloseNow();
    }

    private void SaveAndClose()
    {
        if (!Save(Flattened())) return;
        FlyHome();
        CloseNow();
    }

    private void SaveAsAndClose()
    {
        if (SaveAs()) CloseNow();
    }

    /// <summary>The little comet flies from the editor to the tray: done, discreetly.</summary>
    private void FlyHome()
    {
        try
        {
            var center = _scroller.PointToScreen(new Point(_scroller.ActualWidth / 2, _scroller.ActualHeight / 2));
            _app.LaunchComet(new Point(center.X, center.Y));
        }
        catch (InvalidOperationException) { /* not on screen */ }
    }

    private void CloseNow()
    {
        _closingAfterSave = true;
        Close();
    }

    /// <summary>Saves over the capture (keeping the shapes editable), or as a new capture.</summary>
    private bool Save(BitmapSource image)
    {
        try
        {
            var path = _path ?? SnapNaming.NextFreePath(_app.CapturesFolder, DateTime.Now);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (_doc.IsEmpty)
            {
                EditSidecar.DeleteFor(path);
            }
            else
            {
                EditSidecar.EnsureFolder(path);
                CaptureStore.SavePng(_base, EditSidecar.OriginalFor(path));
            }
            CaptureStore.SavePng(image, path);
            if (!_doc.IsEmpty) File.WriteAllText(EditSidecar.AnnotationsFor(path), _doc.ToJson());

            _path = path;
            _dirty = false;
            Title = TitleText();
            _app.Thumbnails.Forget(path);
            _app.Thumbnails.Prime(path, image);
            _app.Recent.Add(path);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Editor save failed", ex);
            MessageBox.Show(this, L.T("Could not save the capture:") + "\n" + ex.Message, "Cometsnap", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private bool SaveAs()
    {
        var image = Flattened();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = L.T("Save a copy"),
            FileName = _path is null ? SnapNaming.BaseName(DateTime.Now) : Path.GetFileNameWithoutExtension(_path) + "-edited",
            DefaultExt = ".png",
            Filter = L.T("PNG image") + " (*.png)|*.png|" + L.T("JPEG image") + " (*.jpg)|*.jpg",
            InitialDirectory = _path is null ? _app.CapturesFolder : Path.GetDirectoryName(_path),
        };
        if (dialog.ShowDialog(this) != true) return false;
        try
        {
            if (dialog.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || dialog.FileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var fs = File.Create(dialog.FileName);
                encoder.Save(fs);
            }
            else
            {
                CaptureStore.SavePng(image, dialog.FileName);
            }
            // Keep the capture itself in sync too, so the tray shows the edit.
            if (_dirty) Save(image);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Editor save as failed", ex);
            MessageBox.Show(this, L.T("Could not save:") + "\n" + ex.Message, "Cometsnap", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        CommitText();
        if (_closingAfterSave || !_dirty) return;
        var answer = MessageBox.Show(this,
            _path is null ? L.T("Save this capture?") : L.T("Save your changes to this capture?"),
            "Cometsnap", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !Save(Flattened())))
            e.Cancel = true;
    }

    private void ApplyDarkFrame()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var dark = 1;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }
}
