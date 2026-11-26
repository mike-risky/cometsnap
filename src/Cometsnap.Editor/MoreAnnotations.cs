using System.Globalization;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;

namespace Cometsnap.Editor;

/// <summary>A straight line (no arrow head).</summary>
public sealed class LineAnnotation : BoxAnnotation
{
    public override bool IsDegenerate => (new Point(X2, Y2) - new Point(X1, Y1)).Length < 4;

    public override bool HitTest(Point p, double tolerance) =>
        DistanceToSegment(p, new Point(X1, Y1), new Point(X2, Y2)) <= tolerance + Thickness;

    public override void Render(DrawingContext dc) =>
        dc.DrawLine(StrokePen(), new Point(X1, Y1), new Point(X2, Y2));
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
            var r = Rect.Empty;
            foreach (var p in Pts()) r.Union(p);
            return r.IsEmpty ? new Rect() : r;
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

    protected StreamGeometry Path()
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            var first = true;
            foreach (var p in Pts())
            {
                if (first) { ctx.BeginFigure(p, false, false); first = false; }
                else ctx.LineTo(p, true, true);
            }
        }
        g.Freeze();
        return g;
    }

    public override void Render(DrawingContext dc)
    {
        if (Points.Count < 4) return;
        dc.DrawGeometry(null, StrokePen(), Path());
    }
}

/// <summary>A wide see-through marker stroke.</summary>
public sealed class HighlighterAnnotation : PenAnnotation
{
    protected override double StrokeWidth => Thickness * 4;

    public override void Render(DrawingContext dc)
    {
        if (Points.Count < 4) return;
        var pen = new Pen(Brush, StrokeWidth) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        // Opacity on the whole stroke, so overlapping parts of one stroke don't get darker.
        dc.PushOpacity(0.42);
        dc.DrawGeometry(null, pen, Path());
        dc.Pop();
    }
}

/// <summary>A numbered badge ①②③ for step-by-step docs.</summary>
public sealed class StepAnnotation : Annotation
{
    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public double X { get; set; }
    public double Y { get; set; }
    public int Number { get; set; } = 1;

    [JsonIgnore]
    public double Radius => Math.Max(11, Thickness * 3.2 + 6);

    public override bool IsDegenerate => false;

    public override void Offset(double dx, double dy) { X += dx; Y += dy; }

    public override Rect Bounds => new(X - Radius, Y - Radius, Radius * 2, Radius * 2);

    public override bool HitTest(Point p, double tolerance) => (p - new Point(X, Y)).Length <= Radius + tolerance;

    public override void Render(DrawingContext dc)
    {
        var r = Radius;
        var ring = new Pen(Brushes.White, Math.Max(1.5, r / 7));
        ring.Freeze();
        dc.DrawEllipse(Brush, ring, new Point(X, Y), r, r);
        var text = new FormattedText(Number.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Face, r * 1.15, ContrastBrush(Brush.Color), 1.0);
        dc.DrawText(text, new Point(X - text.Width / 2, Y - text.Height / 2));
    }

    internal static Brush ContrastBrush(System.Windows.Media.Color c) =>
        0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 160 ? Brushes.Black : Brushes.White;
}

/// <summary>A speech bubble with a tail pointing at something.</summary>
public sealed class CalloutAnnotation : Annotation
{
    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public double TargetX { get; set; }
    public double TargetY { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public string Text { get; set; } = "";
    public double FontSize { get; set; } = 20;

    public override bool IsDegenerate => string.IsNullOrWhiteSpace(Text);

    public override void Offset(double dx, double dy) { X += dx; Y += dy; TargetX += dx; TargetY += dy; }

    private FormattedText Format(Brush brush) => new(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, FontSize, brush, 1.0);

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
            r.Union(new Point(TargetX, TargetY));
            return r;
        }
    }

    public override bool HitTest(Point p, double tolerance)
    {
        var r = IsDegenerate ? new Rect(X, Y, 1, 1) : BubbleRect();
        r.Inflate(tolerance, tolerance);
        return r.Contains(p) || DistanceToSegment(p, new Point(TargetX, TargetY), new Point(r.X + r.Width / 2, r.Y + r.Height / 2)) <= tolerance;
    }

    public override void Render(DrawingContext dc)
    {
        if (IsDegenerate) return;
        var box = BubbleRect();
        var radius = Math.Min(box.Height / 2, FontSize * 0.6);
        var center = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
        var target = new Point(TargetX, TargetY);
        Geometry shape = new RectangleGeometry(box, radius, radius);

        if (!box.Contains(target))
        {
            var dir = target - center;
            dir.Normalize();
            var normal = new Vector(-dir.Y, dir.X);
            var baseHalf = Math.Min(box.Width, box.Height) * 0.22;
            var tail = new StreamGeometry();
            using (var g = tail.Open())
            {
                g.BeginFigure(center + normal * baseHalf, true, true);
                g.LineTo(target, true, true);
                g.LineTo(center - normal * baseHalf, true, true);
            }
            shape = Geometry.Combine(shape, tail, GeometryCombineMode.Union, null);
        }

        var outline = new Pen(Brushes.White, Math.Max(1.5, FontSize / 12)) { LineJoin = PenLineJoin.Round };
        outline.Freeze();
        dc.DrawGeometry(Brush, outline, shape);
        dc.DrawText(Format(StepAnnotation.ContrastBrush(Brush.Color)), new Point(X, Y));
    }
}

/// <summary>Pixelates an area (passwords, e-mails, faces). Drawn by the document, which has the pixels.</summary>
public sealed class BlurAnnotation : BoxAnnotation
{
    public double BlockSize { get; set; } = 10;

    public override void Render(DrawingContext dc)
    {
        // Without the image (shouldn't happen) cover the area instead.
        var r = Box;
        dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80)), null, r);
    }
}

/// <summary>Greys out everything except this area. Drawn by the document (all spotlights at once).</summary>
public sealed class SpotlightAnnotation : BoxAnnotation
{
    public double Dim { get; set; } = 0.55;

    public override bool HitTest(Point p, double tolerance)
    {
        // Selectable on its edge only: clicking inside the bright area should reach the shapes there.
        var outer = Box; outer.Inflate(tolerance, tolerance);
        var inner = Box; inner.Inflate(-tolerance, -tolerance);
        return outer.Contains(p) && (inner.IsEmpty || !inner.Contains(p));
    }

    public override void Render(DrawingContext dc) { }
}

/// <summary>Crops the exported image to this area. Only the last crop counts.</summary>
public sealed class CropAnnotation : BoxAnnotation
{
    public override bool HitTest(Point p, double tolerance) => false;

    public override void Render(DrawingContext dc) { }
}
