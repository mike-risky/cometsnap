using System.Globalization;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Media;

namespace Cometsnap.Linux.Editing;

/// <summary>
/// A vector annotation drawn over a capture. Coordinates are image pixels. Shapes stay objects (not
/// pixels) until the capture is exported, so they can be saved and edited again later. The JSON
/// format is the same as the Windows app's.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(EllipseAnnotation), "ellipse")]
[JsonDerivedType(typeof(RectangleAnnotation), "rectangle")]
[JsonDerivedType(typeof(ArrowAnnotation), "arrow")]
[JsonDerivedType(typeof(TextAnnotation), "text")]
[JsonDerivedType(typeof(LineAnnotation), "line")]
[JsonDerivedType(typeof(PenAnnotation), "pen")]
[JsonDerivedType(typeof(HighlighterAnnotation), "highlighter")]
[JsonDerivedType(typeof(StepAnnotation), "step")]
[JsonDerivedType(typeof(CalloutAnnotation), "callout")]
[JsonDerivedType(typeof(BlurAnnotation), "blur")]
[JsonDerivedType(typeof(SpotlightAnnotation), "spotlight")]
[JsonDerivedType(typeof(CropAnnotation), "crop")]
public abstract class Annotation
{
    private string _color = "#E5484D";
    private IImmutableSolidColorBrush? _brush;

    public string Color
    {
        get => _color;
        set { _color = value; _brush = null; }
    }

    public double Thickness { get; set; } = 4;

    [JsonIgnore]
    public IImmutableSolidColorBrush Brush => _brush ??= CreateBrush(_color);

    public abstract void Render(DrawingContext dc);

    public abstract void Offset(double dx, double dy);

    [JsonIgnore]
    public abstract Rect Bounds { get; }

    public virtual bool HitTest(Point p, double tolerance) => Bounds.Inflate(tolerance).Contains(p);

    protected static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        var len2 = ab.X * ab.X + ab.Y * ab.Y;
        if (len2 < 1e-9) return Length(p - a);
        var t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2, 0, 1);
        return Length(p - (a + ab * t));
    }

    protected static double Length(Vector v) => Math.Sqrt(v.X * v.X + v.Y * v.Y);
    protected static double Length(Point v) => Math.Sqrt(v.X * v.X + v.Y * v.Y);

    [JsonIgnore]
    public abstract bool IsDegenerate { get; }

    public static IImmutableSolidColorBrush CreateBrush(string hex) =>
        new ImmutableSolidColorBrush(Avalonia.Media.Color.TryParse(hex, out var c) ? c : Colors.Red);

    protected IPen StrokePen(PenLineJoin join = PenLineJoin.Round) =>
        new ImmutablePen(Brush, Thickness, lineCap: PenLineCap.Round, lineJoin: join);

    internal static readonly Typeface SemiBold = new(Theme.SpaceTheme.UiFont, FontStyle.Normal, FontWeight.SemiBold);
    internal static readonly Typeface Bold = new(Theme.SpaceTheme.UiFont, FontStyle.Normal, FontWeight.Bold);

    internal static IBrush ContrastBrush(Color c) =>
        0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 160 ? Brushes.Black : Brushes.White;

    internal static Rect R(double x1, double y1, double x2, double y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
}

/// <summary>A shape defined by two corner points (drag start and end).</summary>
public abstract class BoxAnnotation : Annotation
{
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }

    [JsonIgnore]
    public Rect Box => R(X1, Y1, X2, Y2);

    public override bool IsDegenerate => Math.Abs(X2 - X1) < 3 && Math.Abs(Y2 - Y1) < 3;

    public override void Offset(double dx, double dy)
    {
        X1 += dx; X2 += dx;
        Y1 += dy; Y2 += dy;
    }

    public override Rect Bounds => Box;

    /// <summary>Updates the moving corner. <paramref name="constrain"/> (Shift) makes it square / round.</summary>
    public void DragTo(double x, double y, bool constrain)
    {
        if (constrain)
        {
            var d = Math.Max(Math.Abs(x - X1), Math.Abs(y - Y1));
            x = X1 + Math.Sign(x - X1 == 0 ? 1 : x - X1) * d;
            y = Y1 + Math.Sign(y - Y1 == 0 ? 1 : y - Y1) * d;
        }
        X2 = x;
        Y2 = y;
    }
}

public sealed class EllipseAnnotation : BoxAnnotation
{
    public override void Render(DrawingContext dc)
    {
        var r = Box;
        if (r.Width <= Thickness || r.Height <= Thickness) return;
        r = r.Deflate(Thickness / 2);
        dc.DrawEllipse(null, StrokePen(), r.Center, r.Width / 2, r.Height / 2);
    }
}

public sealed class RectangleAnnotation : BoxAnnotation
{
    public override void Render(DrawingContext dc)
    {
        var r = Box;
        if (r.Width <= Thickness || r.Height <= Thickness) return;
        r = r.Deflate(Thickness / 2);
        dc.DrawRectangle(null, StrokePen(), r, Thickness / 2, Thickness / 2);
    }
}

public sealed class ArrowAnnotation : BoxAnnotation
{
    public override bool IsDegenerate => Length(new Point(X2, Y2) - new Point(X1, Y1)) < 6;

    public override bool HitTest(Point p, double tolerance) =>
        DistanceToSegment(p, new Point(X1, Y1), new Point(X2, Y2)) <= tolerance + Thickness * 2;

    /// <summary>Shift snaps the arrow to 45° steps.</summary>
    public void DragArrowTo(double x, double y, bool snapAngle)
    {
        if (snapAngle)
        {
            var vx = x - X1;
            var vy = y - Y1;
            var len = Math.Sqrt(vx * vx + vy * vy);
            var angle = Math.Round(Math.Atan2(vy, vx) / (Math.PI / 4)) * (Math.PI / 4);
            x = X1 + Math.Cos(angle) * len;
            y = Y1 + Math.Sin(angle) * len;
        }
        X2 = x;
        Y2 = y;
    }

    public override void Render(DrawingContext dc)
    {
        var start = new Point(X1, Y1);
        var tip = new Point(X2, Y2);
        var v = tip - start;
        var len = Length(v);
        if (len < 1) return;
        v /= len;

        var headLength = Math.Min(len * 0.6, Thickness * 4 + 6);
        var headWidth = headLength * 0.75;
        var baseCenter = tip - v * headLength;
        var normal = new Vector(-v.Y, v.X);

        dc.DrawLine(StrokePen(), start, baseCenter + v * (Thickness / 2));

        var head = new StreamGeometry();
        using (var g = head.Open())
        {
            g.BeginFigure(tip, true);
            g.LineTo(baseCenter + normal * headWidth / 2);
            g.LineTo(baseCenter - normal * headWidth / 2);
            g.EndFigure(true);
        }
        dc.DrawGeometry(Brush, new ImmutablePen(Brush, Math.Max(1, Thickness / 3), lineJoin: PenLineJoin.Round), head);
    }
}

public sealed class TextAnnotation : Annotation
{
    public double X { get; set; }
    public double Y { get; set; }
    public string Text { get; set; } = "";
    public double FontSize { get; set; } = 20;

    /// <summary>Draw the text inside a filled bubble instead of an outlined text.</summary>
    public bool Bubble { get; set; }

    public override bool IsDegenerate => string.IsNullOrWhiteSpace(Text);

    public override void Offset(double dx, double dy) { X += dx; Y += dy; }

    public override Rect Bounds
    {
        get
        {
            if (IsDegenerate) return new Rect(X, Y, 0, 0);
            var ft = Format(Brushes.White);
            var pad = Bubble ? FontSize * 0.35 : 0;
            return new Rect(X - pad, Y - pad / 2, ft.WidthIncludingTrailingWhitespace + 2 * pad, ft.Height + pad);
        }
    }

    public FormattedText Format(IBrush brush) =>
        new(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, SemiBold, FontSize, brush);

    public override void Render(DrawingContext dc)
    {
        if (IsDegenerate) return;
        if (Bubble)
        {
            var ft = Format(ContrastBrush(Brush.Color));
            var pad = FontSize * 0.35;
            var box = new Rect(X - pad, Y - pad / 2, ft.WidthIncludingTrailingWhitespace + 2 * pad, ft.Height + pad);
            dc.DrawRectangle(Brush, null, box, pad, pad);
            dc.DrawText(ft, new Point(X, Y));
            return;
        }
        // Colored text with a white halo: readable on any background.
        var text = Format(Brush);
        var geometry = text.BuildGeometry(new Point(X, Y));
        if (geometry is null) { dc.DrawText(text, new Point(X, Y)); return; }
        dc.DrawGeometry(null, new ImmutablePen(Brushes.White.ToImmutable(), Math.Max(2, FontSize / 6), lineJoin: PenLineJoin.Round), geometry);
        dc.DrawGeometry(Brush, null, geometry);
    }
}

/// <summary>A straight line (no arrow head).</summary>
public sealed class LineAnnotation : BoxAnnotation
{
    public override bool IsDegenerate => Length(new Point(X2, Y2) - new Point(X1, Y1)) < 4;

    public override bool HitTest(Point p, double tolerance) =>
        DistanceToSegment(p, new Point(X1, Y1), new Point(X2, Y2)) <= tolerance + Thickness;

    public override void Render(DrawingContext dc) => dc.DrawLine(StrokePen(), new Point(X1, Y1), new Point(X2, Y2));
}

/// <summary>Freehand stroke. Points are stored flat: x0, y0, x1, y1...</summary>
public class PenAnnotation : Annotation
{
    public List<double> Points { get; set; } = new();

    public void AddPoint(double x, double y)
    {
        if (Points.Count >= 2)
        {
            var dx = x - Points[^2];
            var dy = y - Points[^1];
            if (dx * dx + dy * dy < 1.5) return; // skip jitter
        }
        Points.Add(x);
        Points.Add(y);
    }

    protected IEnumerable<Point> Pts()
    {
        for (var i = 0; i + 1 < Points.Count; i += 2) yield return new Point(Points[i], Points[i + 1]);
    }

    public override bool IsDegenerate => Points.Count < 4;

    public override void Offset(double dx, double dy)
    {
        for (var i = 0; i + 1 < Points.Count; i += 2)
        {
            Points[i] += dx;
            Points[i + 1] += dy;
        }
    }

    public override Rect Bounds
    {
        get
        {
            if (Points.Count < 2) return default;
            double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, b = double.MinValue;
            foreach (var p in Pts())
            {
                l = Math.Min(l, p.X); t = Math.Min(t, p.Y);
                r = Math.Max(r, p.X); b = Math.Max(b, p.Y);
            }
            return new Rect(l, t, r - l, b - t);
        }
    }

    protected virtual double StrokeWidth => Thickness;

    public override bool HitTest(Point p, double tolerance)
    {
        var pts = Pts().ToList();
        for (var i = 1; i < pts.Count; i++)
            if (DistanceToSegment(p, pts[i - 1], pts[i]) <= tolerance + StrokeWidth / 2) return true;
        return false;
    }

    protected StreamGeometry PathGeometry()
    {
        var g = new StreamGeometry();
        using var ctx = g.Open();
        var first = true;
        foreach (var p in Pts())
        {
            if (first) { ctx.BeginFigure(p, false); first = false; }
            else ctx.LineTo(p);
        }
        if (!first) ctx.EndFigure(false);
        return g;
    }

    public override void Render(DrawingContext dc)
    {
        if (Points.Count < 4) return;
        dc.DrawGeometry(null, StrokePen(), PathGeometry());
    }
}

/// <summary>A wide see-through marker stroke.</summary>
public sealed class HighlighterAnnotation : PenAnnotation
{
    protected override double StrokeWidth => Thickness * 4;

    public override void Render(DrawingContext dc)
    {
        if (Points.Count < 4) return;
        // Opacity on the whole stroke, so overlapping parts of one stroke don't get darker.
        using (dc.PushOpacity(0.42))
            dc.DrawGeometry(null, new ImmutablePen(Brush, StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), PathGeometry());
    }
}

/// <summary>A numbered badge ①②③ for step-by-step docs.</summary>
public sealed class StepAnnotation : Annotation
{
    public double X { get; set; }
    public double Y { get; set; }
    public int Number { get; set; } = 1;

    [JsonIgnore]
    public double Radius => Math.Max(11, Thickness * 3.2 + 6);

    public override bool IsDegenerate => false;

    public override void Offset(double dx, double dy) { X += dx; Y += dy; }

    public override Rect Bounds => new(X - Radius, Y - Radius, Radius * 2, Radius * 2);

    public override bool HitTest(Point p, double tolerance) => Length(p - new Point(X, Y)) <= Radius + tolerance;

    public override void Render(DrawingContext dc)
    {
        var r = Radius;
        dc.DrawEllipse(Brush, new ImmutablePen(Brushes.White.ToImmutable(), Math.Max(1.5, r / 7)), new Point(X, Y), r, r);
        var text = new FormattedText(Number.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Bold, r * 1.15, ContrastBrush(Brush.Color));
        dc.DrawText(text, new Point(X - text.Width / 2, Y - text.Height / 2));
    }
}

/// <summary>A speech bubble with a tail pointing at something.</summary>
public sealed class CalloutAnnotation : Annotation
{
    public double TargetX { get; set; }
    public double TargetY { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public string Text { get; set; } = "";
    public double FontSize { get; set; } = 20;

    public override bool IsDegenerate => string.IsNullOrWhiteSpace(Text);

    public override void Offset(double dx, double dy) { X += dx; Y += dy; TargetX += dx; TargetY += dy; }

    private FormattedText Format(IBrush brush) => new(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, SemiBold, FontSize, brush);

    private Rect BubbleRect()
    {
        var ft = Format(Brushes.White);
        var pad = FontSize * 0.45;
        return new Rect(X - pad, Y - pad * 0.6, ft.WidthIncludingTrailingWhitespace + 2 * pad, ft.Height + pad * 1.2);
    }

    public override Rect Bounds
    {
        get
        {
            var r = IsDegenerate ? new Rect(X, Y, 1, 1) : BubbleRect();
            return r.Union(new Rect(TargetX, TargetY, 0, 0));
        }
    }

    public override bool HitTest(Point p, double tolerance)
    {
        var r = IsDegenerate ? new Rect(X, Y, 1, 1) : BubbleRect();
        return r.Inflate(tolerance).Contains(p) || DistanceToSegment(p, new Point(TargetX, TargetY), r.Center) <= tolerance;
    }

    public override void Render(DrawingContext dc)
    {
        if (IsDegenerate) return;
        var box = BubbleRect();
        var radius = Math.Min(box.Height / 2, FontSize * 0.6);
        var center = box.Center;
        var target = new Point(TargetX, TargetY);
        Geometry shape = new RectangleGeometry(box, radius, radius);

        if (!box.Contains(target))
        {
            var dir = target - center;
            dir /= Length(dir);
            var normal = new Vector(-dir.Y, dir.X);
            var baseHalf = Math.Min(box.Width, box.Height) * 0.22;
            var tail = new StreamGeometry();
            using (var g = tail.Open())
            {
                g.BeginFigure(center + normal * baseHalf, true);
                g.LineTo(target);
                g.LineTo(center - normal * baseHalf);
                g.EndFigure(true);
            }
            shape = new CombinedGeometry(GeometryCombineMode.Union, shape, tail);
        }

        dc.DrawGeometry(Brush, new ImmutablePen(Brushes.White.ToImmutable(), Math.Max(1.5, FontSize / 12), lineJoin: PenLineJoin.Round), shape);
        dc.DrawText(Format(ContrastBrush(Brush.Color)), new Point(X, Y));
    }
}

/// <summary>Pixelates an area (passwords, e-mails, faces). Drawn by the document, which has the pixels.</summary>
public sealed class BlurAnnotation : BoxAnnotation
{
    public double BlockSize { get; set; } = 10;

    public override void Render(DrawingContext dc) =>
        dc.DrawRectangle(new ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(0x80, 0x80, 0x80)), null, Box);
}

/// <summary>Greys out everything except this area. Drawn by the document (all spotlights at once).</summary>
public sealed class SpotlightAnnotation : BoxAnnotation
{
    public double Dim { get; set; } = 0.55;

    public override bool HitTest(Point p, double tolerance)
    {
        // Selectable on its edge only: clicking inside the bright area should reach the shapes there.
        var outer = Box.Inflate(tolerance);
        var inner = Box.Width > 2 * tolerance && Box.Height > 2 * tolerance ? Box.Deflate(tolerance) : default;
        return outer.Contains(p) && (inner.Width <= 0 || !inner.Contains(p));
    }

    public override void Render(DrawingContext dc) { }
}

/// <summary>Crops the exported image to this area. Only the last crop counts.</summary>
public sealed class CropAnnotation : BoxAnnotation
{
    public override bool HitTest(Point p, double tolerance) => false;

    public override void Render(DrawingContext dc) { }
}
