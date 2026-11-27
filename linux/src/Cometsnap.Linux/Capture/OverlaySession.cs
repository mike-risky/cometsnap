using Cometsnap.Linux.Core;
using Cometsnap.Linux.Platform;

namespace Cometsnap.Linux.Capture;

public enum OverlayPurpose
{
    /// <summary>Screenshot: copy + save on release.</summary>
    Capture,
    /// <summary>Pick the area to record.</summary>
    Record,
    /// <summary>Pick the text to copy (OCR).</summary>
    Text,
    /// <summary>A screenshot, with a reminder that holding the button and scrolling makes a long one.</summary>
    Scroll,
    /// <summary>Color picker: a click copies the color under the cursor.</summary>
    Color,
}

/// <summary>
/// Everything the capture overlay knows, shared by the one overlay window per monitor.
/// All coordinates are desktop pixels (X11 root coordinates).
/// </summary>
public sealed class OverlaySession
{
    public const int DragThreshold = 4;
    public const int SnapDistance = 8;

    public FrozenScreen Screen { get; }
    public IReadOnlyList<WindowInfo> Windows { get; }
    public IReadOnlyList<PixelRect> Monitors { get; }
    public AppSettings Settings { get; }
    public OverlayPurpose Purpose { get; }
    private readonly EdgeSnapper _snapper;

    public bool WindowDetection { get; private set; }
    public int CursorX { get; private set; }
    public int CursorY { get; private set; }
    public bool HasCursor { get; private set; }
    public bool Pressed { get; private set; }
    public bool Dragging { get; private set; }
    public PixelRect? Selection { get; private set; }
    public WindowInfo? HoverWindow { get; private set; }
    public LoupeCorner LoupeCorner { get; private set; }
    public string? Flash { get; private set; }

    /// <summary>Set when the wheel turned with the button held: a scrolling capture of the result takes over (1 = up, -1 = down).</summary>
    public int ScrollDelta { get; private set; }

    /// <summary>Whether holding the button and scrolling may start a scrolling capture here.</summary>
    public bool CanScroll { get; set; }

    private int _startX, _startY;
    private bool _moveMode;
    private int _moveFromX, _moveFromY;
    private bool _finished;

    /// <summary>Raised once: the chosen rectangle, or null when cancelled.</summary>
    public event Action<PixelRect?>? Finished;

    /// <summary>The overlay windows redraw on this.</summary>
    public event Action? Changed;

    public OverlaySession(FrozenScreen screen, IReadOnlyList<WindowInfo> windows, IReadOnlyList<PixelRect> monitors,
        AppSettings settings, OverlayPurpose purpose)
    {
        Screen = screen;
        Windows = windows;
        Monitors = monitors;
        Settings = settings;
        Purpose = purpose;
        WindowDetection = settings.WindowDetection && windows.Count > 0 && purpose != OverlayPurpose.Color;
        LoupeCorner = settings.LoupeCorner;
        _snapper = new EdgeSnapper(windows.Select(w => w.Bounds).Concat(monitors), SnapDistance);
    }

    /// <summary>What a click (no drag) would grab right now: the window under the cursor, or its monitor.</summary>
    public PixelRect? HoverRect =>
        !HasCursor || Purpose == OverlayPurpose.Color ? null
        : WindowDetection && HoverWindow is not null ? HoverWindow.Bounds
        : MonitorAt(CursorX, CursorY);

    /// <summary>The rectangle to highlight: the drag, or the hover target.</summary>
    public PixelRect? Highlight => Dragging ? Selection : HoverRect;

    public PixelRect MonitorAt(int x, int y)
    {
        foreach (var m in Monitors)
            if (m.Contains(x, y)) return m;
        // Between monitors: the closest one.
        return Monitors.OrderBy(m => Math.Abs(m.CenterX - x) + Math.Abs(m.CenterY - y)).FirstOrDefault(Screen.Bounds);
    }

    public void MoveTo(int x, int y)
    {
        if (_finished) return;
        CursorX = x;
        CursorY = y;
        HasCursor = true;
        Flash = null;

        if (Pressed)
        {
            if (!Dragging && (Math.Abs(x - _startX) > DragThreshold || Math.Abs(y - _startY) > DragThreshold))
                Dragging = true;
            if (Dragging)
            {
                if (_moveMode && Selection is { } sel)
                {
                    // Space held: move the whole selection.
                    var dx = x - _moveFromX;
                    var dy = y - _moveFromY;
                    _startX += dx;
                    _startY += dy;
                    _moveFromX = x;
                    _moveFromY = y;
                    Selection = sel.Offset(dx, dy);
                }
                else
                {
                    var ex = _snapper.SnapX(x);
                    var ey = _snapper.SnapY(y);
                    Selection = PixelRect.FromPixels(_startX, _startY, ex, ey).Intersect(Screen.Bounds);
                }
            }
        }
        else
        {
            HoverWindow = WindowDetection ? WindowFinder.At(Windows, x, y) : null;
        }

        UpdateLoupeCorner();
        Changed?.Invoke();
    }

    private void UpdateLoupeCorner()
    {
        var monitor = MonitorAt(CursorX, CursorY);
        var (w, h) = LoupeSizePixels(monitor);
        LoupeCorner = LoupePlacement.ResolveCorner(Settings.LoupeCorner, LoupeCorner, monitor, w, h, CursorX, CursorY);
    }

    /// <summary>The loupe's size in desktop pixels on a monitor (it is drawn at a fixed size in DIPs).</summary>
    public Func<PixelRect, (int W, int H)> LoupeSizePixels { get; set; } = _ => (150, 200);

    /// <summary>Color picker: the text that was picked (#RRGGBB or rgb(...)), set just before it finishes.</summary>
    public string? PickedColor { get; private set; }

    /// <summary>Color picker: picks the color under the cursor and closes. Shift = rgb(...).</summary>
    public void PickColor(int x, int y, bool rgb)
    {
        if (_finished) return;
        MoveTo(x, y);
        if (ColorTextUnderCursor(rgb) is not { } text) return;
        PickedColor = text;
        Finish(new PixelRect(x, y, 1, 1));
    }

    public void Press(int x, int y)
    {
        if (_finished) return;
        MoveTo(x, y);
        Pressed = true;
        Dragging = false;
        _startX = _snapper.SnapX(x);
        _startY = _snapper.SnapY(y);
        Selection = null;
    }

    public void Release(int x, int y)
    {
        if (_finished || !Pressed) return;
        MoveTo(x, y);
        Pressed = false;
        if (Dragging && Selection is { IsEmpty: false } sel) Finish(sel);
        else if (HoverRect is { } hover) Finish(hover);
    }

    /// <summary>The wheel with the button still held (dragging an area, or pressing on a window).</summary>
    public void Wheel(int direction)
    {
        if (_finished || !Pressed || !CanScroll || direction == 0) return;
        var rect = Dragging ? Selection : HoverRect;
        if (rect is not { } r || r.Width < ScrollingCapture.MinWidth || r.Height < ScrollingCapture.MinHeight) return;
        ScrollDelta = direction;
        Pressed = false;
        Finish(r);
    }

    /// <summary>Right click / Esc: drop the drag first, close on the second press.</summary>
    public void Back()
    {
        if (_finished) return;
        if (Pressed || Dragging)
        {
            Pressed = false;
            Dragging = false;
            Selection = null;
            Changed?.Invoke();
            return;
        }
        Finish(null);
    }

    public void Cancel() => Finish(null);

    /// <summary>Enter: grab what is highlighted.</summary>
    public void Confirm()
    {
        if (Purpose == OverlayPurpose.Color) { PickColor(CursorX, CursorY, rgb: false); return; }
        if (Highlight is { IsEmpty: false } r) Finish(r);
    }

    public void CaptureMonitor()
    {
        if (Purpose != OverlayPurpose.Color) Finish(MonitorAt(CursorX, CursorY));
    }

    public void ToggleWindowDetection()
    {
        if (Windows.Count == 0) return;
        WindowDetection = !WindowDetection;
        HoverWindow = WindowDetection ? WindowFinder.At(Windows, CursorX, CursorY) : null;
        Flash = WindowDetection ? T("Window detection on") : T("Window detection off");
        Changed?.Invoke();
    }

    public void SetMoveMode(bool on)
    {
        _moveMode = on && Dragging;
        _moveFromX = CursorX;
        _moveFromY = CursorY;
    }

    public void ShowFlash(string text)
    {
        Flash = text;
        Changed?.Invoke();
    }

    /// <summary>Hex color under the cursor, e.g. "#3D6BFF".</summary>
    public string? ColorUnderCursor()
    {
        var (ix, iy) = Screen.ToImage(CursorX + 0.5, CursorY + 0.5);
        return Screen.ColorAtImage(ix, iy) is { } c ? $"#{c:X6}" : null;
    }

    /// <summary>The color under the cursor as #RRGGBB, or rgb(r, g, b).</summary>
    public string? ColorTextUnderCursor(bool rgb)
    {
        var (ix, iy) = Screen.ToImage(CursorX + 0.5, CursorY + 0.5);
        if (Screen.ColorAtImage(ix, iy) is not { } c) return null;
        byte r = (byte)(c >> 16), g = (byte)(c >> 8), b = (byte)c;
        return rgb ? Cometsnap.Core.Settings.ColorText.Rgb(r, g, b) : Cometsnap.Core.Settings.ColorText.Hex(r, g, b);
    }

    private void Finish(PixelRect? rect)
    {
        if (_finished) return;
        _finished = true;
        Finished?.Invoke(rect);
    }
}
