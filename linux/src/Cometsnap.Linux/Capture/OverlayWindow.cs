using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Platform;
using Cometsnap.Linux.Theme;
using PixelRect = Cometsnap.Linux.Core.PixelRect;

namespace Cometsnap.Linux.Capture;

/// <summary>
/// One borderless full-screen window per monitor, showing the frozen desktop, dimmed, with the
/// window under the cursor (or the dragged area) lit up, its size, and the magnifier loupe.
/// The windows are created once and reused, so the overlay appears instantly.
/// </summary>
public sealed class OverlayWindow : Window
{
    private readonly OverlaySurface _surface;
    public PixelRect Monitor { get; private set; }

    public OverlayWindow(PixelRect monitor)
    {
        Monitor = monitor;
        Title = "Cometsnap capture";
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        Background = Brushes.Black;
        Cursor = new Cursor(StandardCursorType.Cross);
        ExtendClientAreaToDecorationsHint = false;
        _surface = new OverlaySurface(this);
        Content = _surface;
        Place(monitor);
    }

    public OverlaySession? Session => _surface.Session;

    public void Place(PixelRect monitor)
    {
        Monitor = monitor;
        Position = new PixelPoint(monitor.X, monitor.Y);
        var scale = Screens.ScreenFromPoint(new PixelPoint(monitor.CenterX, monitor.CenterY))?.Scaling ?? 1;
        Width = monitor.Width / scale;
        Height = monitor.Height / scale;
    }

    public void Begin(OverlaySession session)
    {
        _surface.Session = session;
        WindowState = WindowState.Normal;
        Place(Monitor);
        Show();
        WindowState = WindowState.FullScreen;
        _surface.InvalidateVisual();
    }

    public void End()
    {
        _surface.Session = null;
        Hide();
    }

    /// <summary>Takes the keyboard (Esc, Enter, arrows...).</summary>
    public void Focus()
    {
        Activate();
        if (TryGetPlatformHandle()?.Handle is { } h) X11.ForceFocus(h);
        _surface.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        _surface.HandleKey(e, down: true);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        _surface.HandleKey(e, down: false);
    }
}

internal sealed class OverlaySurface : Control
{
    private const double LoupeCells = 15;
    private const double LoupeZoom = 12;
    private const double LoupeInfoHeight = 48;
    private static readonly double LoupeSize = LoupeCells * LoupeZoom;

    private static readonly IBrush DimBrush = SpaceTheme.WithAlpha(SpaceTheme.Void, 0x8C);
    private static readonly IPen BorderPen = new ImmutablePen(SpaceTheme.CometBlueBrush, 2);
    private static readonly IPen GlowPen = new ImmutablePen(SpaceTheme.WithAlpha(SpaceTheme.CometBlue, 0x55), 8);
    private static readonly IPen GuidePen = new ImmutablePen(SpaceTheme.WithAlpha(SpaceTheme.CometBlue, 0x70), 1);
    private static readonly IPen LoupeFramePen = new ImmutablePen(SpaceTheme.CometBlueBrush, 2);
    private static readonly IPen GridPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), 1);
    private static readonly IPen CenterPen = new ImmutablePen(SpaceTheme.SunBrush, 1.5);
    private static readonly Typeface Ui = new(SpaceTheme.UiFont, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface Mono = new(new FontFamily("DejaVu Sans Mono, Liberation Mono, Noto Sans Mono, monospace"));

    private readonly OverlayWindow _owner;
    private OverlaySession? _session;

    public OverlaySurface(OverlayWindow owner)
    {
        _owner = owner;
        Focusable = true;
        ClipToBounds = true;
    }

    public OverlaySession? Session
    {
        get => _session;
        set
        {
            if (_session is not null) _session.Changed -= OnChanged;
            _session = value;
            if (_session is not null)
            {
                _session.Changed += OnChanged;
                _session.LoupeSizePixels = m => ((int)Math.Ceiling((LoupeSize + 4) * ScaleFor(m)), (int)Math.Ceiling((LoupeSize + LoupeInfoHeight + 4) * ScaleFor(m)));
            }
        }
    }

    private double ScaleFor(PixelRect monitor) => monitor == _owner.Monitor ? Scale : _owner.Screens.ScreenFromPoint(new PixelPoint(monitor.CenterX, monitor.CenterY))?.Scaling ?? 1;

    private double Scale => _owner.RenderScaling;

    private void OnChanged() => InvalidateVisual();

    // ------------------------------------------------------------------ coordinates

    private (int X, int Y) ToDesktop(Point p) =>
        ((int)Math.Floor(_owner.Monitor.X + p.X * Scale), (int)Math.Floor(_owner.Monitor.Y + p.Y * Scale));

    private Rect ToLocal(PixelRect r) =>
        new((r.X - _owner.Monitor.X) / Scale, (r.Y - _owner.Monitor.Y) / Scale, r.Width / Scale, r.Height / Scale);

    // ------------------------------------------------------------------ input

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_session is null) return;
        var (x, y) = ToDesktop(e.GetPosition(this));
        _session.MoveTo(x, y);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_session is null) return;
        var point = e.GetCurrentPoint(this);
        var (x, y) = ToDesktop(point.Position);
        if (point.Properties.IsRightButtonPressed)
        {
            _session.Back();
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        e.Pointer.Capture(this);
        _session.Press(x, y);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_session is null || e.InitialPressMouseButton != MouseButton.Left) return;
        e.Pointer.Capture(null);
        var (x, y) = ToDesktop(e.GetPosition(this));
        _session.Release(x, y);
    }

    public void HandleKey(KeyEventArgs e, bool down)
    {
        var s = _session;
        if (s is null) return;
        if (!down)
        {
            if (e.Key == Key.Space) s.SetMoveMode(false);
            return;
        }
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var step = shift ? 10 : 1;
        switch (e.Key)
        {
            case Key.Escape: s.Back(); break;
            case Key.Enter: s.Confirm(); break;
            case Key.F: s.CaptureMonitor(); break;
            case Key.W: s.ToggleWindowDetection(); break;
            case Key.Space: s.SetMoveMode(true); break;
            case Key.C:
                if (s.ColorUnderCursor() is { } hex)
                {
                    _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(hex);
                    s.ShowFlash(F("Copied {0}", hex));
                }
                break;
            case Key.Left: X11.MovePointer(-step, 0); break;
            case Key.Right: X11.MovePointer(step, 0); break;
            case Key.Up: X11.MovePointer(0, -step); break;
            case Key.Down: X11.MovePointer(0, step); break;
            default: return;
        }
        e.Handled = true;
    }

    // ------------------------------------------------------------------ drawing

    public override void Render(DrawingContext dc)
    {
        var s = _session;
        var bounds = new Rect(Bounds.Size);
        if (s is null)
        {
            dc.FillRectangle(Brushes.Black, bounds);
            return;
        }
        var monitor = _owner.Monitor;

        // Frozen desktop, 1:1 pixels.
        var src = s.Screen.ToImage(monitor);
        using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            dc.DrawImage(s.Screen.Bitmap, new Rect(src.X, src.Y, src.Width, src.Height), ToLocal(monitor.Intersect(s.Screen.Bounds) is { IsEmpty: false } vis ? vis : monitor));

        // Dim everything except the highlight.
        var hl = s.Highlight is { } h ? ToLocal(h) : (Rect?)null;
        if (hl is { } r)
        {
            var c = r.Intersect(bounds);
            dc.FillRectangle(DimBrush, new Rect(0, 0, bounds.Width, Math.Max(0, c.Top)));
            dc.FillRectangle(DimBrush, new Rect(0, c.Bottom, bounds.Width, Math.Max(0, bounds.Height - c.Bottom)));
            dc.FillRectangle(DimBrush, new Rect(0, c.Top, Math.Max(0, c.Left), c.Height));
            dc.FillRectangle(DimBrush, new Rect(c.Right, c.Top, Math.Max(0, bounds.Width - c.Right), c.Height));
            if (c.Width > 0 && c.Height > 0)
            {
                dc.DrawRectangle(GlowPen, r.Inflate(-1));
                dc.DrawRectangle(BorderPen, r.Inflate(-1));
            }
        }
        else
        {
            dc.FillRectangle(DimBrush, bounds);
        }

        var cursorHere = s.HasCursor && monitor.Contains(s.CursorX, s.CursorY);
        var cursor = new Point((s.CursorX - monitor.X + 0.5) / Scale, (s.CursorY - monitor.Y + 0.5) / Scale);

        // Crosshair guides while aiming (not over a highlighted window, which would be noisy).
        if (cursorHere && !s.Dragging && !(s.WindowDetection && s.HoverWindow is not null))
        {
            dc.DrawLine(GuidePen, new Point(0, cursor.Y), new Point(bounds.Width, cursor.Y));
            dc.DrawLine(GuidePen, new Point(cursor.X, 0), new Point(cursor.X, bounds.Height));
        }

        // Size tag.
        if (s.Highlight is { } sel && hl is { } local && local.Intersects(bounds) && (s.Dragging || cursorHere))
            DrawSizeTag(dc, sel, local, bounds);

        if (cursorHere)
        {
            DrawHint(dc, s, bounds, cursor);
            DrawLoupe(dc, s, monitor);
        }
    }

    private static FormattedText Text(string text, double size, IBrush brush, Typeface? face = null) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face ?? Ui, size, brush);

    private static void DrawSizeTag(DrawingContext dc, PixelRect sel, Rect local, Rect bounds)
    {
        var t = Text($"{sel.Width} × {sel.Height}", 13, SpaceTheme.VoidBrush);
        var w = t.Width + 14;
        var h = t.Height + 6;
        var x = Math.Clamp(local.X, 4, bounds.Width - w - 4);
        var y = local.Y - h - 6;
        if (y < 4) y = local.Y + 6;
        if (y + h > bounds.Height - 4) y = bounds.Height - h - 4;
        var box = new Rect(x, y, w, h);
        dc.DrawRectangle(SpaceTheme.SunBrush, null, box, 5, 5);
        dc.DrawText(t, new Point(x + 7, y + 3));
    }

    private static void DrawHint(DrawingContext dc, OverlaySession s, Rect bounds, Point cursor)
    {
        var text = s.Flash ?? (s.Purpose == OverlayPurpose.Record
            ? T("RECORD: click a window or drag an area · F full screen · Esc cancel")
            : s.Purpose == OverlayPurpose.Text
            ? T("Drag over the text to copy, or click a window · Esc cancel")
            : s.Purpose == OverlayPurpose.Scroll
            ? T("Click the window to scroll, or drag over the part that scrolls · Esc cancel")
            : s.WindowDetection
                ? T("Click a window or drag an area · F full screen · C copy color · Esc cancel")
                : T("Drag an area or click for the whole screen · C copy color · Esc cancel"));
        var t = Text(text, 13, SpaceTheme.StarWhiteBrush);
        var w = t.Width + 28;
        var h = t.Height + 14;
        var box = new Rect((bounds.Width - w) / 2, 18, w, h);
        if (s.Flash is null && box.Inflate(40).Contains(cursor)) return; // out of the way
        dc.DrawRectangle(SpaceTheme.WithAlpha(SpaceTheme.Void, 0xE6), new ImmutablePen(s.Purpose == OverlayPurpose.Record ? SpaceTheme.RecordRedBrush : SpaceTheme.NavyLightBrush, 1), box, h / 2, h / 2);
        dc.DrawText(t, new Point(box.X + 14, box.Y + 7));
    }

    private void DrawLoupe(DrawingContext dc, OverlaySession s, PixelRect monitor)
    {
        var (lw, lh) = s.LoupeSizePixels(monitor);
        var placed = s.LoupeCorner == LoupeCorner.FollowCursor
            ? LoupePlacement.FollowRect(monitor, lw, lh, s.CursorX, s.CursorY)
            : LoupePlacement.RectFor(s.LoupeCorner, monitor, lw, lh);
        var area = ToLocal(placed);
        var panel = new Rect(area.X + 2, area.Y + 2, LoupeSize, LoupeSize + LoupeInfoHeight);
        var zoomRect = new Rect(panel.X, panel.Y, LoupeSize, LoupeSize);

        dc.DrawRectangle(SpaceTheme.WithAlpha(SpaceTheme.Void, 0xF0), null, panel.Inflate(2), 8, 8);

        // Real pixels around the cursor, nearest-neighbour.
        var (ix, iy) = s.Screen.ToImage(s.CursorX + 0.5, s.CursorY + 0.5);
        var half = (int)(LoupeCells / 2);
        var srcRect = new Rect(ix - half, iy - half, LoupeCells, LoupeCells);
        using (dc.PushClip(zoomRect))
        {
            dc.FillRectangle(Brushes.Black, zoomRect);
            using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
                dc.DrawImage(s.Screen.Bitmap, srcRect, zoomRect);
            for (var i = 1; i < LoupeCells; i++)
            {
                var o = i * LoupeZoom;
                dc.DrawLine(GridPen, new Point(zoomRect.X + o, zoomRect.Y), new Point(zoomRect.X + o, zoomRect.Bottom));
                dc.DrawLine(GridPen, new Point(zoomRect.X, zoomRect.Y + o), new Point(zoomRect.Right, zoomRect.Y + o));
            }
            var c = half * LoupeZoom;
            dc.DrawRectangle(CenterPen, new Rect(zoomRect.X + c, zoomRect.Y + c, LoupeZoom, LoupeZoom));
        }
        dc.DrawRectangle(null, LoupeFramePen, panel.Inflate(1), 8, 8);

        // Position, size and color.
        var hex = s.ColorUnderCursor() ?? "#------";
        var size = s.Highlight is { } h ? $"{h.Width} × {h.Height}" : "";
        var line1 = Text($"{s.CursorX - monitor.X}, {s.CursorY - monitor.Y}   {size}", 12, SpaceTheme.StarWhiteBrush, Mono);
        dc.DrawText(line1, new Point(panel.X + 8, zoomRect.Bottom + 6));
        if (Color.TryParse(hex, out var color))
            dc.DrawRectangle(new ImmutableSolidColorBrush(color), new ImmutablePen(SpaceTheme.StarWhiteBrush, 1), new Rect(panel.X + 8, zoomRect.Bottom + 27, 14, 14), 3, 3);
        var line2 = Text($"{hex}  (C copies)", 12, SpaceTheme.DustBrush, Mono);
        dc.DrawText(line2, new Point(panel.X + 28, zoomRect.Bottom + 26));
    }
}
