using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Cometsnap.Linux.Theme;

namespace Cometsnap.Linux.Trimming;

/// <summary>
/// The trimmer's timeline: yellow handles trim the start and end, dragging across the track
/// selects a part to cut out, cut parts show striped red (click one to put it back), and the
/// white playhead follows the preview. Click anywhere to jump there.
/// </summary>
public sealed class TrimTimeline : Control
{
    private const double Side = 16;        // room for the handles at both ends
    private const double TrackTop = 22;
    private const double TrackHeight = 40;
    private const double HandleWidth = 10;
    private const double Grab = 9;         // how close counts as "on the handle"

    private enum Drag { None, Start, End, Playhead, Select }

    private static readonly IBrush KeptBrush = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromRgb(0x3D, 0x6B, 0xFF), 0), new GradientStop(Color.FromRgb(0x2B, 0x52, 0xE0), 1) },
    }.ToImmutable();
    private static readonly IBrush TrimmedBrush = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xC8);
    private static readonly IBrush CutBrush = SpaceTheme.WithAlpha(SpaceTheme.RecordRed, 0xB0);
    private static readonly IBrush CutHoverBrush = SpaceTheme.WithAlpha(SpaceTheme.FlameOrange, 0xB0);
    private static readonly IPen StripePen = new ImmutablePen(SpaceTheme.WithAlpha(Colors.Black, 0x70), 2.5);
    private static readonly IBrush SelectionBrush = SpaceTheme.WithAlpha(SpaceTheme.Sun, 0x55);
    private static readonly IPen SelectionPen = new ImmutablePen(SpaceTheme.SunBrush, 1.5);
    private static readonly IPen PlayheadPen = new ImmutablePen(SpaceTheme.StarWhiteBrush, 2, lineCap: PenLineCap.Round);
    private static readonly IPen TickPen = new ImmutablePen(SpaceTheme.WithAlpha(SpaceTheme.Dust, 0x60), 1);
    private static readonly IPen GripPen = new ImmutablePen(SpaceTheme.WithAlpha(SpaceTheme.Void, 0xB0), 1.4, lineCap: PenLineCap.Round);
    private static readonly Typeface LabelFace = new(SpaceTheme.UiFont);
    private static readonly Typeface BoldFace = new(SpaceTheme.UiFont, FontStyle.Normal, FontWeight.SemiBold);

    private CutList _cuts = new(TimeSpan.FromSeconds(1));
    private TimeSpan _position;
    private TimeRange? _selection;
    private Drag _drag;
    private Point _pressAt;
    private TimeSpan _pressTime;
    private bool _moved;
    private CutList? _before;
    private TimeSpan? _hover;

    /// <summary>The user moved the playhead (or a handle): show this moment.</summary>
    public event Action<TimeSpan>? SeekRequested;

    /// <summary>The trim or cuts changed. The argument is the list as it was before (for undo).</summary>
    public event Action<CutList>? CutsChanged;

    /// <summary>The selected part changed (null: nothing selected).</summary>
    public event Action<TimeRange?>? SelectionChanged;

    public TrimTimeline()
    {
        Height = TrackTop + TrackHeight + 22;
        Focusable = false;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        ToolTip.SetTip(this, T("Drag the yellow handles to trim · Drag across to select a part to cut · Click a cut to put it back"));
        ToolTip.SetShowDelay(this, 900);
    }

    public CutList Cuts
    {
        get => _cuts;
        set { _cuts = value; InvalidateVisual(); }
    }

    public TimeSpan Position
    {
        get => _position;
        set { _position = value; InvalidateVisual(); }
    }

    public TimeRange? Selection
    {
        get => _selection;
        set { _selection = value; InvalidateVisual(); SelectionChanged?.Invoke(value); }
    }

    // ------------------------------------------------------------------ geometry

    private double TrackLeft => Side;
    private double TrackWidth => Math.Max(1, Bounds.Width - 2 * Side);

    private double X(TimeSpan t) =>
        TrackLeft + (_cuts.Duration.Ticks <= 0 ? 0 : TrackWidth * t.Ticks / (double)_cuts.Duration.Ticks);

    private TimeSpan TimeAt(double x)
    {
        var f = Math.Clamp((x - TrackLeft) / TrackWidth, 0, 1);
        return TimeSpan.FromTicks((long)(f * _cuts.Duration.Ticks));
    }

    // ------------------------------------------------------------------ drawing

    public override void Render(DrawingContext dc)
    {
        var w = Bounds.Width;
        if (w <= 2 * Side) return;
        var track = new Rect(TrackLeft, TrackTop, TrackWidth, TrackHeight);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, Bounds.Height)); // hit-testable everywhere
        dc.DrawRectangle(SpaceTheme.NavyBrush, null, track, 6, 6);

        using (dc.PushClip(new RoundedRect(track, 6)))
        {
            // Kept parts
            foreach (var p in _cuts.Kept())
                dc.DrawRectangle(KeptBrush, null, new Rect(X(p.SourceStart), TrackTop, Math.Max(1, X(p.SourceEnd) - X(p.SourceStart)), TrackHeight));

            // Cut parts: striped red, with a small "put back" mark when hovered
            foreach (var c in _cuts.Cuts)
            {
                var hovered = _hover is { } h && c.Contains(h) && _drag == Drag.None;
                var r = new Rect(X(c.Start), TrackTop, Math.Max(2, X(c.End) - X(c.Start)), TrackHeight);
                dc.DrawRectangle(hovered ? CutHoverBrush : CutBrush, null, r);
                using (dc.PushClip(r))
                    for (var x = r.X - TrackHeight; x < r.Right; x += 10)
                        dc.DrawLine(StripePen, new Point(x, r.Bottom), new Point(x + TrackHeight, r.Y));
                if (r.Width >= 22)
                {
                    var mark = Text(hovered ? "↺" : "✂", 15, SpaceTheme.StarWhiteBrush, BoldFace);
                    dc.DrawText(mark, new Point(r.X + (r.Width - mark.Width) / 2, TrackTop + (TrackHeight - mark.Height) / 2));
                }
            }

            // Trimmed-off ends are dimmed
            var ta = X(_cuts.TrimStart);
            var tb = X(_cuts.TrimEnd);
            if (ta > TrackLeft) dc.DrawRectangle(TrimmedBrush, null, new Rect(TrackLeft, TrackTop, ta - TrackLeft, TrackHeight));
            if (tb < TrackLeft + TrackWidth) dc.DrawRectangle(TrimmedBrush, null, new Rect(tb, TrackTop, TrackLeft + TrackWidth - tb, TrackHeight));
        }

        // Time ticks: about one every 90 px, on a round step
        var step = TickStep(_cuts.Duration, TrackWidth);
        for (var t = TimeSpan.Zero; t <= _cuts.Duration; t += step)
        {
            var x = X(t);
            dc.DrawLine(TickPen, new Point(x, TrackTop + TrackHeight + 2), new Point(x, TrackTop + TrackHeight + 6));
            var label = Text(CutList.Label(t, tenths: step < TimeSpan.FromSeconds(1)), 10.5, SpaceTheme.DustBrush, LabelFace);
            var lx = Math.Clamp(x - label.Width / 2, 0, Math.Max(0, w - label.Width));
            dc.DrawText(label, new Point(lx, TrackTop + TrackHeight + 6));
        }

        // Selection
        if (_selection is { } s && s.Length > TimeSpan.Zero)
        {
            var r = new Rect(X(s.Start), TrackTop - 3, Math.Max(2, X(s.End) - X(s.Start)), TrackHeight + 6);
            dc.DrawRectangle(SelectionBrush, SelectionPen, r, 3, 3);
            var len = Text(CutList.Label(s.Length), 11, SpaceTheme.SunBrush, BoldFace);
            dc.DrawText(len, new Point(Math.Clamp(r.X + (r.Width - len.Width) / 2, 0, Math.Max(0, w - len.Width)), 2));
        }

        // Handles
        DrawHandle(dc, X(_cuts.TrimStart), left: true);
        DrawHandle(dc, X(_cuts.TrimEnd), left: false);

        // Playhead
        var px = X(_position);
        dc.DrawLine(PlayheadPen, new Point(px, TrackTop - 4), new Point(px, TrackTop + TrackHeight + 4));
        var knob = new StreamGeometry();
        using (var g = knob.Open())
        {
            g.BeginFigure(new Point(px - 6, TrackTop - 11), true);
            g.LineTo(new Point(px + 6, TrackTop - 11));
            g.LineTo(new Point(px, TrackTop - 3));
            g.EndFigure(true);
        }
        dc.DrawGeometry(SpaceTheme.StarWhiteBrush, null, knob);
        if (_selection is null)
        {
            var now = Text(CutList.Label(_position), 11, SpaceTheme.StarWhiteBrush, BoldFace);
            var nx = px + 9;
            if (nx + now.Width > w) nx = px - 9 - now.Width;
            dc.DrawText(now, new Point(nx, 4));
        }
    }

    private static void DrawHandle(DrawingContext dc, double x, bool left)
    {
        var r = new Rect(left ? x - HandleWidth : x, TrackTop - 2, HandleWidth, TrackHeight + 4);
        dc.DrawRectangle(SpaceTheme.SunBrush, null, r, 3, 3);
        var cx = r.X + r.Width / 2;
        for (var i = -1; i <= 1; i++)
            dc.DrawLine(GripPen, new Point(cx + i * 2.4, r.Y + r.Height / 2 - 6), new Point(cx + i * 2.4, r.Y + r.Height / 2 + 6));
    }

    private static TimeSpan TickStep(TimeSpan duration, double width)
    {
        var target = duration.TotalSeconds * 90 / Math.Max(90, width);
        foreach (var s in new[] { 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600 })
            if (s >= target) return TimeSpan.FromSeconds(s);
        return TimeSpan.FromSeconds(1200);
    }

    private static FormattedText Text(string s, double size, IBrush brush, Typeface face) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush);

    // ------------------------------------------------------------------ mouse

    private Drag HitHandle(Point p)
    {
        if (p.Y < TrackTop - 14 || p.Y > TrackTop + TrackHeight + 6) return Drag.None;
        var a = X(_cuts.TrimStart) - HandleWidth / 2;
        var b = X(_cuts.TrimEnd) + HandleWidth / 2;
        var da = Math.Abs(p.X - a);
        var db = Math.Abs(p.X - b);
        if (da <= Grab && da <= db) return Drag.Start;
        if (db <= Grab) return Drag.End;
        if (Math.Abs(p.X - X(_position)) <= 5 && p.Y < TrackTop) return Drag.Playhead;
        return Drag.None;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        _pressAt = p;
        _pressTime = TimeAt(p.X);
        _moved = false;
        _drag = HitHandle(p);
        if (_drag == Drag.None) _drag = p.Y < TrackTop ? Drag.Playhead : Drag.Select;
        if (_drag is Drag.Start or Drag.End) _before = _cuts.Clone();
        if (_drag == Drag.Playhead) SeekRequested?.Invoke(_pressTime);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        var t = TimeAt(p.X);
        if (_drag == Drag.None)
        {
            _hover = p.Y >= TrackTop && p.Y <= TrackTop + TrackHeight ? t : null;
            Cursor = new Cursor(HitHandle(p) is Drag.Start or Drag.End ? StandardCursorType.SizeWestEast
                : _hover is { } h && _cuts.CutAt(h) is not null ? StandardCursorType.Hand
                : StandardCursorType.Ibeam);
            InvalidateVisual();
            return;
        }
        if (Math.Abs(p.X - _pressAt.X) >= 3) _moved = true;
        switch (_drag)
        {
            case Drag.Start:
                _cuts.TrimStart = t;
                SeekRequested?.Invoke(_cuts.TrimStart);
                break;
            case Drag.End:
                _cuts.TrimEnd = t;
                SeekRequested?.Invoke(_cuts.TrimEnd);
                break;
            case Drag.Playhead:
                SeekRequested?.Invoke(t);
                break;
            case Drag.Select when _moved:
                var a = _pressTime < t ? _pressTime : t;
                var b = _pressTime < t ? t : _pressTime;
                if (a < _cuts.TrimStart) a = _cuts.TrimStart;
                if (b > _cuts.TrimEnd) b = _cuts.TrimEnd;
                Selection = b > a ? new TimeRange(a, b) : null;
                SeekRequested?.Invoke(t);
                break;
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        var drag = _drag;
        _drag = Drag.None;
        e.Pointer.Capture(null);
        if (drag is Drag.Start or Drag.End && _before is { } before && !before.SameAs(_cuts))
            CutsChanged?.Invoke(before);
        _before = null;
        if (drag == Drag.Select && !_moved)
        {
            // A click: put back the cut under it, or jump there.
            var before2 = _cuts.Clone();
            if (_cuts.RestoreCutAt(_pressTime)) CutsChanged?.Invoke(before2);
            else
            {
                Selection = null;
                SeekRequested?.Invoke(_pressTime);
            }
        }
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }
}
