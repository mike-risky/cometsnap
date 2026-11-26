using System.Globalization;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;

namespace Cometsnap.Editor;

/// <summary>
/// A vector annotation drawn over a capture. Coordinates are image pixels.
/// Shapes stay objects (not pixels) until the capture is exported, so they can be
/// serialized and edited again later.
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
    private SolidColorBrush? _brush;

    public string Color
    {
        get => _color;
        set { _color = value; _brush = null; }
    }

    public double Thickness { get; set; } = 4;

    [JsonIgnore]
    public SolidColorBrush Brush => _brush ??= CreateBrush(_color);

    public abstract void Render(DrawingContext dc);

    /// <summary>Moves the shape by the given amount of pixels.</summary>
    public abstract void Offset(double dx, double dy);

    /// <summary>The area the shape covers, for selection.</summary>
    [JsonIgnore]
    public abstract Rect Bounds { get; }

    /// <summary>True when <paramref name="p"/> is on the shape (within <paramref name="tolerance"/> pixels).</summary>
    public virtual bool HitTest(Point p, double tolerance)
    {
        var b = Bounds;
        b.Inflate(tolerance, tolerance);
        return b.Contains(p);
    }

    /// <summary>Distance from a point to a segment, for line-like hit tests.</summary>
    protected static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared;
        if (len2 < 1e-9) return (p - a).Length;
        var t = Math.Clamp(Vector.Multiply(p - a, ab) / len2, 0, 1);
        return (p - (a + t * ab)).Length;
    }

    /// <summary>True when the shape is too small to be intentional (a stray click).</summary>
    [JsonIgnore]
    public abstract bool IsDegenerate { get; }

    public static SolidColorBrush CreateBrush(string hex)
    {
        Color c;
        try { c = (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { c = Colors.Red; }
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    protected Pen StrokePen(PenLineJoin join = PenLineJoin.Round)
    {
        var pen = new Pen(Brush, Thickness) { LineJoin = join, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }
}

/// <summary>A shape defined by two corner points (drag start and end).</summary>
public abstract class BoxAnnotation : Annotation
{
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }

    [JsonIgnore]
    public Rect Box => new(new Point(X1, Y1), new Point(X2, Y2));

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
        var inset = Thickness / 2;
        if (r.Width <= Thickness || r.Height <= Thickness) return;
        r.Inflate(-inset, -inset);
        dc.DrawEllipse(null, StrokePen(), new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
    }
}

public sealed class RectangleAnnotation : BoxAnnotation
{
    public override void Render(DrawingContext dc)
    {
        var r = Box;
        var inset = Thickness / 2;
        if (r.Width <= Thickness || r.Height <= Thickness) return;
        r.Inflate(-inset, -inset);
        dc.DrawRoundedRectangle(null, StrokePen(), r, Thickness / 2, Thickness / 2);
    }
}

public sealed class ArrowAnnotation : BoxAnnotation
{
    public override bool IsDegenerate => (new Point(X2, Y2) - new Point(X1, Y1)).Length < 6;

    public override bool HitTest(Point p, double tolerance) =>
        DistanceToSegment(p, new Point(X1, Y1), new Point(X2, Y2)) <= tolerance + Thickness * 2;

    /// <summary>Shift snaps the arrow to 45° steps.</summary>
    public void DragArrowTo(double x, double y, bool snapAngle)
    {
        if (snapAngle)
        {
            var v = new Vector(x - X1, y - Y1);
            var len = v.Length;
            var angle = Math.Round(Math.Atan2(v.Y, v.X) / (Math.PI / 4)) * (Math.PI / 4);
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
        var len = v.Length;
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
            g.BeginFigure(tip, isFilled: true, isClosed: true);
            g.LineTo(baseCenter + normal * headWidth / 2, true, true);
            g.LineTo(baseCenter - normal * headWidth / 2, true, true);
        }
        head.Freeze();
        var joinPen = new Pen(Brush, Math.Max(1, Thickness / 3)) { LineJoin = PenLineJoin.Round };
        joinPen.Freeze();
        dc.DrawGeometry(Brush, joinPen, head);
    }
}

public sealed class TextAnnotation : Annotation
{
    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public double X { get; set; }
    public double Y { get; set; }
    public string Text { get; set; } = "";
    public double FontSize { get; set; } = 20;

    /// <summary>Draw the text inside a filled bubble instead of an outlined text.</summary>
    public bool Bubble { get; set; }

    public override bool IsDegenerate => string.IsNullOrWhiteSpace(Text);

    public override void Offset(double dx, double dy)
    {
        X += dx;
        Y += dy;
    }

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

    public FormattedText Format(Brush brush) => new(
        Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, FontSize, brush, pixelsPerDip: 1.0);

    public override void Render(DrawingContext dc)
    {
        if (IsDegenerate) return;

        if (Bubble)
        {
            var ft = Format(Brushes.White);
            var pad = FontSize * 0.35;
            var box = new Rect(X - pad, Y - pad / 2, ft.WidthIncludingTrailingWhitespace + 2 * pad, ft.Height + pad);
            dc.DrawRoundedRectangle(Brush, null, box, pad, pad);
            dc.DrawText(ft, new Point(X, Y));
            return;
        }

        // Colored text with a white halo: readable on any background.
        var text = Format(Brush);
        var geometry = text.BuildGeometry(new Point(X, Y));
        var halo = new Pen(Brushes.White, Math.Max(2, FontSize / 6)) { LineJoin = PenLineJoin.Round };
        halo.Freeze();
        dc.DrawGeometry(null, halo, geometry);
        dc.DrawGeometry(Brush, null, geometry);
    }
}
