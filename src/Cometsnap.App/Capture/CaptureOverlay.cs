using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using Cometsnap.App.Theme;
using Cometsnap.Core.Capture;
using Cometsnap.Core.Geometry;
using Cometsnap.Core.Native;
using Cometsnap.Core.Settings;
using Cometsnap.Editor;
using Cometsnap.Core.Localization;

namespace Cometsnap.App.Capture;

/// <summary>
/// The heart of Cometsnap: a full virtual-screen window showing the frozen screen.
/// Hover detects windows, click grabs one, drag draws a free region (edges snap to windows),
/// the loupe gives pixel precision. Created once at startup and re-used, so it opens instantly.
///
/// Everything inside works in physical pixels: the root canvas is scaled by 1/DPI, so one
/// unit = one screen pixel on every monitor, whatever the scaling.
/// </summary>
public sealed class CaptureOverlay : Window
{
    private enum Mode { Idle, Hover, Pressed, Dragging, Selected, Moving, Resizing, Drawing }

    [Flags]
    private enum Edges { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

    private const int DragThreshold = 4;
    private const int SnapDistance = 8;

    private static readonly SolidColorBrush DimBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x8C, 0x07, 0x11, 0x3A)));

    private readonly Canvas _root;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Image _image;
    private readonly RectangleGeometry _outer = new();
    private readonly RectangleGeometry _hole = new();
    private readonly Rectangle _glow;
    private readonly Rectangle _border;
    private readonly Line _guideH;
    private readonly Line _guideV;
    private readonly Border _label;
    private readonly TextBlock _labelText;
    private readonly Loupe _loupe;
    private readonly AnnotationLayer _layer;
    private readonly RectangleGeometry _layerClip = new();
    private readonly QuickBar _bar;
    private readonly AnnotationDocument _doc = new();

    private AnnotationTool _tool = AnnotationTool.None;
    private string _color = QuickBar.Palette[0];
    private BoxAnnotation? _drawing;
    private TextBox? _textBox;
    private Point _textAt;
    private double _px, _py;         // precise cursor position (local), for smooth shapes

    private IntPtr _hwnd;
    private FrozenScreen? _screen;
    private PixelRect _full;
    private IReadOnlyList<WindowInfo> _windows = Array.Empty<WindowInfo>();
    private IReadOnlyList<PixelRect> _monitors = Array.Empty<PixelRect>();
    private EdgeSnapper? _snapper;
    private AppSettings _settings = new();

    private Mode _mode = Mode.Idle;
    private bool _detectWindows = true;
    private bool _instant = true;    // capture as soon as the mouse is released, no quick bar
    private PixelRect? _hover;
    private PixelRect _selection;
    private int _cx, _cy;            // cursor pixel (local)
    private int _ax, _ay;            // drag anchor pixel (local)
    private int _pressX, _pressY;
    private PixelRect _startSelection;
    private Edges _resizeEdges;
    private bool _spaceHeld;
    private int _spaceX, _spaceY;
    private bool _skipSnapOnce;
    private LoupeCorner _loupeCorner = LoupeCorner.BottomLeft;
    private double _dpi = 1;
    private int _framesUntilVisible;

    /// <summary>Raised with the captured image and its virtual-screen rectangle.</summary>
    public event Action<BitmapSource, PixelRect>? Confirmed;
    public event Action? Cancelled;

    /// <summary>Raised with the (annotated) capture when the user picks "Save as…".</summary>
    public event Action<BitmapSource>? SaveRequested;

    /// <summary>
    /// Raised with the plain selected pixels and the annotations (in image coordinates)
    /// when the user wants the full editor.
    /// </summary>
    public event Action<BitmapSource, AnnotationDocument>? EditRequested;

    public bool IsCapturing => _mode != Mode.Idle;

    /// <summary>Whether turning the wheel with the button held may start a scrolling capture now.</summary>
    public Func<bool>? CanScroll { get; set; }

    /// <summary>
    /// The wheel turned while the button was held over an area: the overlay has closed and a
    /// scrolling capture takes over. Gives the area (virtual screen) and that first wheel delta.
    /// </summary>
    public event Action<PixelRect, int>? ScrollStarted;

    private bool _scrollHint;
    private bool _pickColor;
    private int _lookVersion = -1;

    /// <summary>Color picker mode: a click copied this color (#RRGGBB or rgb(...)) to the clipboard.</summary>
    public event Action<Color, string>? ColorPicked;

    public CaptureOverlay()
    {
        Title = "Cometsnap capture";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;
        AllowsTransparency = false;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        Focusable = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        Width = 1;
        Height = 1;

        _image = new Image { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);

        var dimGeometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        dimGeometry.Children.Add(_outer);
        dimGeometry.Children.Add(_hole);
        var dim = new Path { Data = dimGeometry, Fill = DimBrush, IsHitTestVisible = false };

        _glow = new Rectangle { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _border = new Rectangle { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _guideH = new Line { StrokeThickness = 1, IsHitTestVisible = false };
        _guideV = new Line { StrokeThickness = 1, IsHitTestVisible = false };

        _labelText = new TextBlock { FontFamily = SpaceTheme.UiFont, FontWeight = FontWeights.SemiBold };
        _label = new Border { Child = _labelText, IsHitTestVisible = false, Visibility = Visibility.Collapsed };

        _loupe = new Loupe();
        _layer = new AnnotationLayer { Document = _doc, Clip = _layerClip };

        _bar = new QuickBar { Visibility = Visibility.Collapsed };
        _bar.ToolSelected += SetTool;
        _bar.ColorSelected += SetColor;
        _bar.UndoClicked += () => { CommitText(); _doc.Undo(); };
        _bar.CopyClicked += Confirm;
        _bar.SaveClicked += Save;
        _bar.EditClicked += Edit;
        _bar.CloseClicked += Cancel;

        _root = new Canvas { RenderTransform = _scale, ClipToBounds = false };
        _root.Children.Add(_image);
        _root.Children.Add(dim);
        _root.Children.Add(_layer);
        _root.Children.Add(_guideH);
        _root.Children.Add(_guideV);
        _root.Children.Add(_glow);
        _root.Children.Add(_border);
        _root.Children.Add(_label);
        _root.Children.Add(_loupe);
        _root.Children.Add(_bar);
        Content = _root;

        MouseMove += OnMouseMove;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseRightButtonUp += (_, e) => { e.Handled = true; Back(); };
        MouseWheel += OnMouseWheel;
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
    }

    /// <summary>Creates the native window and runs WPF's first layout/render once, off-screen and cloaked.</summary>
    public void Prewarm()
    {
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        SetDwm(Win32.DWMWA_TRANSITIONS_FORCEDISABLED, 1);
        SetDwm(Win32.DWMWA_CLOAK, 1);
        Show();
        Hide();
    }

    public void Begin(FrozenScreen screen, IReadOnlyList<WindowInfo> windows, IReadOnlyList<PixelRect> monitors, AppSettings settings, bool scrollHint = false, bool pickColor = false)
    {
        if (_hwnd == IntPtr.Zero) Prewarm();
        _scrollHint = scrollHint;
        _pickColor = pickColor;

        _screen = screen;
        _settings = settings;
        ApplyLook();
        _full = new PixelRect(0, 0, screen.Bounds.Width, screen.Bounds.Height);
        _windows = windows
            .Select(w => w with { Bounds = screen.ToLocal(w.Bounds).Intersect(_full) })
            .Where(w => !w.Bounds.IsEmpty)
            .ToList();
        _monitors = monitors.Select(screen.ToLocal).ToList();
        _snapper = new EdgeSnapper(_windows.Select(w => w.Bounds).Concat(_monitors), SnapDistance);
        _detectWindows = settings.WindowDetection && !pickColor;
        _instant = settings.InstantCapture;
        _loupeCorner = settings.LoupeCorner;
        _hover = null;
        _selection = default;
        _spaceHeld = false;
        _doc.Clear();
        _drawing = null;
        _layer.InProgress = null;
        RemoveTextBox();
        _tool = AnnotationTool.None;
        _bar.SetActive(_tool, _color);

        _image.Source = screen.Image;
        _image.Width = _full.Width;
        _image.Height = _full.Height;
        _outer.Rect = new Rect(0, 0, _full.Width, _full.Height);
        _guideH.X2 = _full.Width;
        _guideV.Y2 = _full.Height;

        // Show cloaked, place over the whole virtual screen, and uncloak once WPF has rendered
        // the new frame: no flash of an old screenshot, no window animation.
        SetDwm(Win32.DWMWA_CLOAK, 1);
        Show();
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, screen.Bounds.X, screen.Bounds.Y,
            screen.Bounds.Width, screen.Bounds.Height, Win32.SWP_SHOWWINDOW);

        _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        _scale.ScaleX = 1 / _dpi;
        _scale.ScaleY = 1 / VisualTreeHelper.GetDpi(this).DpiScaleY;
        ApplyDpiSizes();
        _loupe.Configure(screen.Image, _dpi);
        _loupe.SetPickMode(pickColor);
        _bar.Configure(_dpi);

        Win32.GetCursorPos(out var p);
        _cx = p.X - screen.Bounds.X;
        _cy = p.Y - screen.Bounds.Y;
        _mode = Mode.Hover;
        UpdateHover();
        Refresh();

        Activate();
        Win32.SetForegroundWindow(_hwnd);
        Keyboard.Focus(this);

        _framesUntilVisible = 2;
        CompositionTarget.Rendering -= OnRendering;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (--_framesUntilVisible > 0) return;
        CompositionTarget.Rendering -= OnRendering;
        if (_mode != Mode.Idle) SetDwm(Win32.DWMWA_CLOAK, 0);
    }

    private void End()
    {
        CompositionTarget.Rendering -= OnRendering;
        _mode = Mode.Idle;
        RemoveTextBox();
        SetDwm(Win32.DWMWA_CLOAK, 1);
        if (IsMouseCaptured) ReleaseMouseCapture();
        Hide();
        _image.Source = null;
        _loupe.Release();
        _screen = null;
        _windows = Array.Empty<WindowInfo>();
    }

    public void Cancel()
    {
        if (_mode == Mode.Idle) return;
        End();
        Cancelled?.Invoke();
    }

    private void Confirm()
    {
        if (Render() is not { } image) return;
        var rect = _screen!.ToVirtual(_selection);
        End();
        Confirmed?.Invoke(image, rect);
    }

    private void Save()
    {
        if (Render() is not { } image) return;
        End();
        SaveRequested?.Invoke(image);
    }

    private void Edit()
    {
        if (_screen is null || _selection.IsEmpty) return;
        CommitText();
        var image = _screen.Crop(_selection);
        var doc = _doc.CopyOffset(-_selection.X, -_selection.Y);
        End();
        EditRequested?.Invoke(image, doc);
    }

    /// <summary>The selected pixels with the annotations burnt in.</summary>
    private BitmapSource? Render()
    {
        if (_screen is null || _selection.IsEmpty) return null;
        CommitText();
        var image = _screen.Crop(_selection);
        return _doc.Flatten(image, new Point(_selection.X, _selection.Y));
    }

    /// <summary>Esc / right click: drop the selection first, close on the second press.</summary>
    private void Back()
    {
        if (_textBox is not null)
        {
            CommitText();
            return;
        }
        if (_tool != AnnotationTool.None && _mode == Mode.Selected)
        {
            SetTool(AnnotationTool.None);
            return;
        }
        if (_mode is Mode.Selected or Mode.Moving or Mode.Resizing or Mode.Dragging)
        {
            if (IsMouseCaptured) ReleaseMouseCapture();
            _selection = default;
            _mode = Mode.Hover;
            UpdateHover();
            Refresh();
        }
        else
        {
            Cancel();
        }
    }

    // ------------------------------------------------------------------ mouse

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_mode == Mode.Idle) return;
        var pos = e.GetPosition(_root);
        _cx = Math.Clamp((int)Math.Floor(pos.X), 0, _full.Width - 1);
        _cy = Math.Clamp((int)Math.Floor(pos.Y), 0, _full.Height - 1);
        _px = pos.X;
        _py = pos.Y;
        var snap = !_skipSnapOnce && (Keyboard.Modifiers & ModifierKeys.Alt) == 0;
        _skipSnapOnce = false;

        switch (_mode)
        {
            case Mode.Hover:
                UpdateHover();
                break;

            case Mode.Pressed:
                if (Math.Abs(_cx - _pressX) > DragThreshold || Math.Abs(_cy - _pressY) > DragThreshold)
                {
                    _ax = snap ? SnapX(_pressX) : _pressX;
                    _ay = snap ? SnapY(_pressY) : _pressY;
                    _mode = Mode.Dragging;
                    goto case Mode.Dragging;
                }
                break;

            case Mode.Dragging:
                if (_spaceHeld)
                {
                    // Space held: move the whole rectangle instead of resizing it.
                    _ax += _cx - _spaceX;
                    _ay += _cy - _spaceY;
                }
                _spaceX = _cx;
                _spaceY = _cy;
                var ex = snap && !_spaceHeld ? SnapX(_cx) : _cx;
                var ey = snap && !_spaceHeld ? SnapY(_cy) : _cy;
                _selection = PixelRect.FromPixels(_ax, _ay, ex, ey).Intersect(_full);
                break;

            case Mode.Moving:
                _selection = _startSelection.Offset(_cx - _pressX, _cy - _pressY).KeepInside(_full);
                break;

            case Mode.Resizing:
                Resize(snap);
                break;

            case Mode.Drawing when _drawing is not null:
                var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                if (_drawing is ArrowAnnotation arrow) arrow.DragArrowTo(_px, _py, shift);
                else _drawing.DragTo(_px, _py, shift);
                _layer.InProgress = _drawing;
                break;

            case Mode.Selected:
                Cursor = SelectedCursor();
                break;
        }

        Refresh();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_mode == Mode.Idle) return;
        e.Handled = true;
        if (_pickColor)
        {
            PickColor(rgb: (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            return;
        }
        _pressX = _cx;
        _pressY = _cy;

        if (_mode == Mode.Selected)
        {
            CommitText();
            if (_tool != AnnotationTool.None && _selection.Contains(_cx, _cy))
            {
                StartAnnotation();
                return;
            }

            if (e.ClickCount >= 2 && _selection.Contains(_cx, _cy))
            {
                Confirm();
                return;
            }

            var edges = EdgesAt(_cx, _cy);
            if (edges != Edges.None)
            {
                _resizeEdges = edges;
                _startSelection = _selection;
                _mode = Mode.Resizing;
            }
            else if (_selection.Contains(_cx, _cy))
            {
                _startSelection = _selection;
                _mode = Mode.Moving;
            }
            else
            {
                _selection = default;
                _mode = Mode.Pressed;
                UpdateHover();
            }
        }
        else
        {
            _mode = Mode.Pressed;
        }

        CaptureMouse();
        Refresh();
    }

    /// <summary>
    /// The wheel with the button still held (dragging an area, or pressing on a window):
    /// hand over to the scrolling capture, which scrolls the real page under that area.
    /// </summary>
    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_pickColor) return;
        if (_mode is not (Mode.Pressed or Mode.Dragging) || _screen is null) return;
        e.Handled = true;
        if (CanScroll?.Invoke() != true) return;
        var area = _mode == Mode.Dragging ? _selection : _hover ?? default;
        if (area.Width < ScrollingCapture.MinWidth || area.Height < ScrollingCapture.MinHeight) return;
        var rect = _screen.ToVirtual(area);
        End();
        ScrollStarted?.Invoke(rect, e.Delta);
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_mode == Mode.Idle || _pickColor) return;
        e.Handled = true;
        if (IsMouseCaptured) ReleaseMouseCapture();

        switch (_mode)
        {
            case Mode.Pressed:
                // A click without dragging grabs the detected window.
                if (_hover is { } hover)
                {
                    _selection = hover;
                    _mode = Mode.Selected;
                    if (_instant)
                    {
                        Confirm();
                        return;
                    }
                }
                else
                {
                    _mode = Mode.Hover;
                }
                break;

            case Mode.Drawing:
                if (_drawing is { IsDegenerate: false } done) _doc.Add(done);
                _drawing = null;
                _layer.InProgress = null;
                _mode = Mode.Selected;
                break;

            case Mode.Dragging:
            case Mode.Moving:
            case Mode.Resizing:
                _spaceHeld = false;
                _mode = _selection.IsEmpty ? Mode.Hover : Mode.Selected;
                if (_instant && _mode == Mode.Selected)
                {
                    Confirm();
                    return;
                }
                break;
        }

        Cursor = _mode == Mode.Selected ? SelectedCursor() : Cursors.Cross;
        Refresh();
    }

    private void Resize(bool snap)
    {
        var l = _startSelection.X;
        var t = _startSelection.Y;
        var r = _startSelection.Right - 1;
        var b = _startSelection.Bottom - 1;
        var dx = _cx - _pressX;
        var dy = _cy - _pressY;

        if (_resizeEdges.HasFlag(Edges.Left)) l = SnapIf(snap, l + dx, SnapX);
        if (_resizeEdges.HasFlag(Edges.Right)) r = SnapIf(snap, r + dx, SnapX);
        if (_resizeEdges.HasFlag(Edges.Top)) t = SnapIf(snap, t + dy, SnapY);
        if (_resizeEdges.HasFlag(Edges.Bottom)) b = SnapIf(snap, b + dy, SnapY);

        _selection = PixelRect.FromPixels(l, t, r, b).Intersect(_full);
    }

    private static int SnapIf(bool snap, int v, Func<int, int> snapper) => snap ? snapper(v) : v;
    private int SnapX(int x) => _snapper?.SnapX(x) ?? x;
    private int SnapY(int y) => _snapper?.SnapY(y) ?? y;

    private Edges EdgesAt(int x, int y)
    {
        if (_selection.IsEmpty) return Edges.None;
        var grab = Math.Max(4, (int)Math.Round(6 * _dpi));
        var s = _selection;
        if (x < s.X - grab || x > s.Right - 1 + grab || y < s.Y - grab || y > s.Bottom - 1 + grab) return Edges.None;

        var edges = Edges.None;
        if (Math.Abs(x - s.X) <= grab) edges |= Edges.Left;
        else if (Math.Abs(x - (s.Right - 1)) <= grab) edges |= Edges.Right;
        if (Math.Abs(y - s.Y) <= grab) edges |= Edges.Top;
        else if (Math.Abs(y - (s.Bottom - 1)) <= grab) edges |= Edges.Bottom;
        return edges;
    }

    private Cursor SelectedCursor() =>
        _tool != AnnotationTool.None && _selection.Contains(_cx, _cy)
            ? (_tool == AnnotationTool.Text ? Cursors.IBeam : Cursors.Cross)
            : CursorFor(EdgesAt(_cx, _cy), _selection.Contains(_cx, _cy));

    private static Cursor CursorFor(Edges edges, bool inside) => edges switch
    {
        Edges.Left or Edges.Right => Cursors.SizeWE,
        Edges.Top or Edges.Bottom => Cursors.SizeNS,
        Edges.Left | Edges.Top or Edges.Right | Edges.Bottom => Cursors.SizeNWSE,
        Edges.Right | Edges.Top or Edges.Left | Edges.Bottom => Cursors.SizeNESW,
        _ => inside ? Cursors.SizeAll : Cursors.Cross,
    };

    private void UpdateHover()
    {
        if (!_detectWindows)
        {
            _hover = null;
            return;
        }
        _hover = WindowFinder.HitTest(_windows, _cx, _cy)?.Bounds
                 ?? Monitors.Containing(_monitors, _cx, _cy).Intersect(_full);
    }

    // ------------------------------------------------------------------ keyboard

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_mode == Mode.Idle) return;
        if (_textBox is not null && _textBox.IsKeyboardFocusWithin) return; // typing text
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var step = shift ? 10 : 1;
        e.Handled = true;

        switch (key)
        {
            case Key.Escape:
                Back();
                return;

            case Key.Z when ctrl:
                _doc.Undo();
                return;

            case Key.Y when ctrl:
                _doc.Redo();
                return;

            case Key.S when ctrl && _mode == Mode.Selected:
                Save();
                return;

            case Key.E when ctrl && _mode == Mode.Selected:
                Edit();
                return;

            case Key.E when _mode == Mode.Selected: ToggleTool(AnnotationTool.Ellipse); return;
            case Key.A when _mode == Mode.Selected: ToggleTool(AnnotationTool.Arrow); return;
            case Key.T when _mode == Mode.Selected: ToggleTool(AnnotationTool.Text); return;
            case Key.R when _mode == Mode.Selected: ToggleTool(AnnotationTool.Rectangle); return;

            case >= Key.D1 and <= Key.D6 when _mode == Mode.Selected:
                SetColor(QuickBar.Palette[key - Key.D1]);
                return;

            case >= Key.NumPad1 and <= Key.NumPad6 when _mode == Mode.Selected:
                SetColor(QuickBar.Palette[key - Key.NumPad1]);
                return;

            case Key.Enter or Key.Space or Key.C when _pickColor:
                PickColor(rgb: shift);
                return;

            case Key.Enter:
                if (_mode == Mode.Hover && _hover is { } hover) _selection = hover;
                Confirm();
                return;

            case Key.C when ctrl && _mode == Mode.Selected:
                Confirm();
                return;

            case Key.C:
                // The color under the cursor, any time the loupe is up: C = #RRGGBB, Shift+C = rgb(...).
                if (_screen is not null)
                {
                    var c = _screen.ColorAt(_cx, _cy);
                    var text = shift ? ColorText.Rgb(c.R, c.G, c.B) : ColorText.Hex(c.R, c.G, c.B);
                    if (Services.ClipboardService.SetText(text)) _loupe.FlashHint(L.T("Copied!"));
                }
                return;

            case Key.F when !_pickColor:
                _selection = Monitors.Containing(_monitors, _cx, _cy).Intersect(_full);
                _mode = Mode.Selected;
                if (_instant)
                {
                    Confirm();
                    return;
                }
                Refresh();
                return;

            case Key.W when !_pickColor:
                _detectWindows = !_detectWindows;
                if (_mode == Mode.Hover) UpdateHover();
                Refresh();
                return;

            case Key.Space:
                if (_mode == Mode.Dragging && !e.IsRepeat)
                {
                    _spaceHeld = true;
                    _spaceX = _cx;
                    _spaceY = _cy;
                }
                return;

            case Key.Left: Arrow(-step, 0, ctrl); return;
            case Key.Right: Arrow(step, 0, ctrl); return;
            case Key.Up: Arrow(0, -step, ctrl); return;
            case Key.Down: Arrow(0, step, ctrl); return;
        }

        e.Handled = false;
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space) _spaceHeld = false;
    }

    private void Arrow(int dx, int dy, bool resize)
    {
        if (_mode == Mode.Selected)
        {
            // Arrows move the selection; Ctrl + arrows grow/shrink it from the bottom-right.
            _selection = resize
                ? new PixelRect(_selection.X, _selection.Y, Math.Max(1, _selection.Width + dx), Math.Max(1, _selection.Height + dy)).Intersect(_full)
                : _selection.Offset(dx, dy).KeepInside(_full);
            Refresh();
            return;
        }

        // Otherwise the arrows move the mouse cursor itself, pixel by pixel.
        if (_screen is null) return;
        var x = Math.Clamp(_cx + dx, 0, _full.Width - 1);
        var y = Math.Clamp(_cy + dy, 0, _full.Height - 1);
        _skipSnapOnce = true;
        Win32.SetCursorPos(x + _screen.Bounds.X, y + _screen.Bounds.Y);
    }

    // ------------------------------------------------------------------ drawing

    private void ApplyDpiSizes()
    {
        var bt = Math.Max(1, Math.Round(_dpi));
        _border.StrokeThickness = bt;
        _glow.StrokeThickness = bt * 4;
        _labelText.FontSize = 12 * _dpi;
        _label.Padding = new Thickness(6 * _dpi, 2 * _dpi, 6 * _dpi, 2 * _dpi);
        _label.CornerRadius = new CornerRadius(4 * _dpi);
    }

    private void Refresh()
    {
        if (_screen is null) return;

        PixelRect? target = _pickColor ? null : _mode is Mode.Hover or Mode.Pressed ? _hover : _selection.IsEmpty ? null : _selection;

        if (target is { } r)
        {
            _hole.Rect = new Rect(r.X, r.Y, r.Width, r.Height);
            PlaceFrame(_border, r, _border.StrokeThickness);
            PlaceFrame(_glow, r, _border.StrokeThickness + _glow.StrokeThickness / 2);
            _border.Visibility = _glow.Visibility = Visibility.Visible;

            // Once selected, the tag also tells what the keys do: drawing starts with a tool.
            _labelText.Text = _mode == Mode.Selected && _tool == AnnotationTool.None
                ? $"{r.Width} × {r.Height}   ·   " + L.T("E circle  A arrow  T text  R box  ·  Enter copy")
                : _scrollHint && _mode is Mode.Hover or Mode.Pressed or Mode.Dragging
                    ? $"{r.Width} × {r.Height}   ·   " + L.T("Hold the button and scroll to capture a long page")
                    : $"{r.Width} × {r.Height}";
            _label.Visibility = Visibility.Visible;
            _label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var lh = _label.DesiredSize.Height;
            var ly = r.Y - lh - 4 * _dpi;
            if (ly < 0) ly = r.Y + 4 * _dpi;
            Canvas.SetLeft(_label, Math.Max(0, r.X));
            Canvas.SetTop(_label, ly);
        }
        else if (_pickColor)
        {
            // Color picker: the tag follows the cursor and says what a click does.
            _hole.Rect = Rect.Empty;
            _border.Visibility = _glow.Visibility = Visibility.Collapsed;
            _labelText.Text = L.T("Click to copy the color  ·  Shift+click: RGB  ·  Esc cancels");
            _label.Visibility = Visibility.Visible;
            _label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var monitor = Monitors.Containing(_monitors, _cx, _cy);
            var lx = _cx + 16 * _dpi;
            var ly = _cy + 20 * _dpi;
            if (lx + _label.DesiredSize.Width > monitor.Right) lx = _cx - 16 * _dpi - _label.DesiredSize.Width;
            if (ly + _label.DesiredSize.Height > monitor.Bottom) ly = _cy - 20 * _dpi - _label.DesiredSize.Height;
            Canvas.SetLeft(_label, Math.Max(monitor.X, lx));
            Canvas.SetTop(_label, Math.Max(monitor.Y, ly));
        }
        else
        {
            _hole.Rect = Rect.Empty;
            _border.Visibility = _glow.Visibility = _label.Visibility = Visibility.Collapsed;
        }

        var showGuides = _pickColor || _mode is Mode.Hover or Mode.Pressed or Mode.Dragging;
        _guideH.Visibility = _guideV.Visibility = showGuides ? Visibility.Visible : Visibility.Collapsed;
        if (showGuides)
        {
            _guideH.Y1 = _guideH.Y2 = _cy + 0.5;
            _guideV.X1 = _guideV.X2 = _cx + 0.5;
        }

        _layerClip.Rect = _selection.IsEmpty
            ? new Rect(0, 0, _full.Width, _full.Height)
            : new Rect(_selection.X, _selection.Y, _selection.Width, _selection.Height);

        if (_mode is Mode.Selected or Mode.Drawing && target is { } sel) PlaceBar(sel);
        else _bar.Visibility = Visibility.Collapsed;

        UpdateLoupe(target);
    }

    /// <summary>Quick bar under the selection (right-aligned), or above it, or inside it.</summary>
    private void PlaceBar(PixelRect sel)
    {
        _bar.Visibility = Visibility.Visible;
        _bar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var bw = _bar.DesiredSize.Width;
        var bh = _bar.DesiredSize.Height;
        var gap = 8 * _dpi;
        var monitor = Monitors.Containing(_monitors, sel.Right - 1, sel.Bottom - 1);

        var y = sel.Bottom + gap;
        if (y + bh > monitor.Bottom) y = sel.Y - gap - bh;
        if (y < monitor.Y) y = sel.Bottom - gap - bh;
        var x = Math.Clamp(sel.Right - bw, monitor.X, Math.Max(monitor.X, monitor.Right - bw));

        Canvas.SetLeft(_bar, x);
        Canvas.SetTop(_bar, y);
    }

    // ------------------------------------------------------------------ annotations

    private double ShapeThickness => Math.Max(2, Math.Round(3 * _dpi));
    private double TextSize => Math.Round(18 * _dpi);

    private void ToggleTool(AnnotationTool tool) => SetTool(_tool == tool ? AnnotationTool.None : tool);

    private void SetTool(AnnotationTool tool)
    {
        CommitText();
        _tool = tool;
        _bar.SetActive(_tool, _color);
        if (_mode == Mode.Selected) Cursor = SelectedCursor();
        Refresh();
    }

    private void SetColor(string color)
    {
        _color = color;
        _bar.SetActive(_tool, _color);
        if (_textBox is not null) _textBox.Foreground = Annotation.CreateBrush(color);
    }

    private void StartAnnotation()
    {
        if (_tool == AnnotationTool.Text)
        {
            StartText(_px, _py);
            return;
        }

        _drawing = _tool switch
        {
            AnnotationTool.Ellipse => new EllipseAnnotation(),
            AnnotationTool.Arrow => new ArrowAnnotation(),
            _ => new RectangleAnnotation(),
        };
        _drawing.Color = _color;
        _drawing.Thickness = ShapeThickness;
        _drawing.X1 = _drawing.X2 = _px;
        _drawing.Y1 = _drawing.Y2 = _py;
        _mode = Mode.Drawing;
        CaptureMouse();
    }

    private void StartText(double x, double y)
    {
        RemoveTextBox();
        _textAt = new Point(x, y);
        _textBox = new TextBox
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            FontSize = TextSize,
            Foreground = Annotation.CreateBrush(_color),
            Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0x66),
            CaretBrush = SpaceTheme.StarWhiteBrush,
            BorderBrush = CaptureLook.AccentBrush,
            BorderThickness = new Thickness(Math.Max(1, Math.Round(_dpi))),
            Padding = new Thickness(0),
            MinWidth = 40 * _dpi,
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
        // TextBox has a 1-2 px internal inset; shift it so the committed text lands where it was typed.
        var inset = _textBox.BorderThickness.Left + 2;
        Canvas.SetLeft(_textBox, x - inset);
        Canvas.SetTop(_textBox, y - inset);
        _root.Children.Add(_textBox);
        _textBox.Focus();
        Keyboard.Focus(_textBox);
    }

    private void CommitText()
    {
        if (_textBox is null) return;
        var text = _textBox.Text;
        RemoveTextBox();
        if (string.IsNullOrWhiteSpace(text)) return;
        _doc.Add(new TextAnnotation
        {
            X = _textAt.X,
            Y = _textAt.Y,
            Text = text,
            FontSize = TextSize,
            Color = _color,
        });
        Keyboard.Focus(this);
    }

    private void RemoveTextBox()
    {
        if (_textBox is null) return;
        _root.Children.Remove(_textBox);
        _textBox = null;
    }

    /// <summary>Paints the overlay in the capture theme picked in Settings (only when it changed).</summary>
    private void ApplyLook()
    {
        CaptureLook.Use(_settings.CaptureTheme);
        if (_lookVersion == CaptureLook.Version) return;
        _lookVersion = CaptureLook.Version;
        _border.Stroke = CaptureLook.AccentBrush;
        _glow.Stroke = CaptureLook.AccentWithAlpha(0x55);
        _guideH.Stroke = _guideV.Stroke = CaptureLook.AccentWithAlpha(0x70);
        _label.Background = CaptureLook.TagBrush;
        _labelText.Foreground = CaptureLook.TagTextBrush;
        _loupe.SetAccent(CaptureLook.AccentBrush);
    }

    /// <summary>Color picker: copy the pixel under the cursor and close.</summary>
    private void PickColor(bool rgb)
    {
        if (_screen is null) return;
        var c = _screen.ColorAt(_cx, _cy);
        var text = rgb ? ColorText.Rgb(c.R, c.G, c.B) : ColorText.Hex(c.R, c.G, c.B);
        if (!Services.ClipboardService.SetText(text)) return;
        End();
        ColorPicked?.Invoke(c, text);
    }

    private static void PlaceFrame(Rectangle frame, PixelRect r, double outset)
    {
        // The frame sits just outside the selection so it never ends up in the capture.
        Canvas.SetLeft(frame, r.X - outset);
        Canvas.SetTop(frame, r.Y - outset);
        frame.Width = r.Width + 2 * outset;
        frame.Height = r.Height + 2 * outset;
    }

    private void UpdateLoupe(PixelRect? target)
    {
        if (_screen is null) return;
        var w = _loupe.PixelWidth;
        var h = _loupe.PixelHeight;
        var monitor = Monitors.Containing(_monitors, _cx, _cy);

        PixelRect place;
        if (_settings.LoupeCorner == LoupeCorner.FollowCursor)
        {
            place = LoupePlacement.FollowRect(monitor, w, h, _cx, _cy);
        }
        else
        {
            _loupeCorner = LoupePlacement.ResolveCorner(_settings.LoupeCorner, _loupeCorner, monitor, w, h, _cx, _cy);
            place = LoupePlacement.RectFor(_loupeCorner, monitor, w, h);
        }
        Canvas.SetLeft(_loupe, place.X);
        Canvas.SetTop(_loupe, place.Y);

        var size = target is { } t ? $"{t.Width} × {t.Height}" : null;
        _loupe.Update(_cx, _cy, _cx + _screen.Bounds.X, _cy + _screen.Bounds.Y, _screen.ColorAt(_cx, _cy), size);
    }

    // ------------------------------------------------------------------ native

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_DPICHANGED)
        {
            // We span every monitor and draw in physical pixels ourselves: ignore DPI-driven
            // resizing, otherwise Windows would shrink/grow the overlay between monitors.
            handled = true;
            if (_screen is { } s)
                Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height, Win32.SWP_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    private void SetDwm(int attribute, int value)
    {
        if (_hwnd != IntPtr.Zero) Win32.DwmSetWindowAttribute(_hwnd, attribute, ref value, sizeof(int));
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
