using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Cometsnap.Linux.Editing;

/// <summary>
/// An ordered list of annotations with undo/redo of every change (add, move, recolor, delete).
/// Pixel effects (blur, spotlight, crop) need the image: set <see cref="Source"/>.
/// </summary>
public sealed class AnnotationDocument
{
    private List<Annotation> _items = new();
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private readonly Dictionary<(int, int, int, int, int), Bitmap> _pixelated = new();

    private Bitmap? _source;
    private byte[]? _pixels;
    private int _stride;
    private int _width;
    private int _height;

    public IReadOnlyList<Annotation> Items => _items;
    public bool IsEmpty => _items.Count == 0;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public event Action? Changed;

    /// <summary>Constellation mode: numbered steps are joined, in order, by faint star lines.</summary>
    public bool ConnectSteps { get; set; }

    /// <summary>The image under the annotations, in annotation coordinates (0,0 = its top-left pixel).</summary>
    public Bitmap? Source
    {
        get => _source;
        set
        {
            _source = value;
            _pixelated.Clear();
            _pixels = null;
            if (value is null) return;
            _width = value.PixelSize.Width;
            _height = value.PixelSize.Height;
            _stride = _width * 4;
            _pixels = new byte[_stride * _height];
            unsafe
            {
                fixed (byte* p = _pixels)
                    value.CopyPixels(new PixelRect(0, 0, _width, _height), (IntPtr)p, _pixels.Length, _stride);
            }
        }
    }

    public void Checkpoint()
    {
        _undo.Push(ToJson());
        _redo.Clear();
    }

    public void Touch() => Changed?.Invoke();

    public void Add(Annotation annotation)
    {
        Checkpoint();
        _items.Add(annotation);
        Changed?.Invoke();
    }

    public void Remove(Annotation annotation)
    {
        var i = _items.IndexOf(annotation);
        if (i < 0) return;
        Checkpoint();
        _items.RemoveAt(i);
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(ToJson());
        _items = Parse(_undo.Pop());
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(ToJson());
        _items = Parse(_redo.Pop());
        Changed?.Invoke();
    }

    public Annotation? HitTest(Point p, double tolerance)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
            if (_items[i].HitTest(p, tolerance)) return _items[i];
        return null;
    }

    public int NextStepNumber() =>
        _items.OfType<StepAnnotation>().Select(s => s.Number).DefaultIfEmpty(0).Max() + 1;

    public Rect? CropRect =>
        _items.OfType<CropAnnotation>().LastOrDefault() is { IsDegenerate: false } c ? c.Box : null;

    /// <summary>
    /// Draws the shapes. Blurs first (they hide pixels), then the spotlight dimming, then shapes
    /// in order. <paramref name="editing"/> also shows the crop frame (export applies it instead).
    /// </summary>
    public void Render(DrawingContext dc, Annotation? inProgress = null, bool editing = false)
    {
        var all = inProgress is null ? _items : _items.Append(inProgress).ToList();

        foreach (var blur in all.OfType<BlurAnnotation>()) RenderBlur(dc, blur);

        var spots = all.OfType<SpotlightAnnotation>().Where(s => !s.IsDegenerate).ToList();
        if (spots.Count > 0)
        {
            Geometry dim = new RectangleGeometry(ImageArea(all));
            foreach (var s in spots)
                dim = new CombinedGeometry(GeometryCombineMode.Exclude, dim, new RectangleGeometry(s.Box, 6, 6));
            dc.DrawGeometry(new ImmutableSolidColorBrush(Color.FromArgb((byte)(255 * spots[^1].Dim), 0, 0, 0)), null, dim);
        }

        if (ConnectSteps) RenderConstellation(dc, all.OfType<StepAnnotation>());

        foreach (var a in all)
            if (a is not (BlurAnnotation or SpotlightAnnotation or CropAnnotation)) a.Render(dc);

        if (editing && all.OfType<CropAnnotation>().LastOrDefault() is { IsDegenerate: false } crop)
        {
            var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(ImageArea(all)), new RectangleGeometry(crop.Box));
            dc.DrawGeometry(new ImmutableSolidColorBrush(Color.FromArgb(0xB0, 0x07, 0x11, 0x3A)), null, outside);
            dc.DrawRectangle(null, new ImmutablePen(Brushes.White.ToImmutable(), Math.Max(1, crop.Thickness / 2), new ImmutableDashStyle(new double[] { 4, 3 }, 0)), crop.Box);
        }
    }

    /// <summary>Faint lines from step 1 to 2 to 3…, with a little four-pointed star halfway along each.</summary>
    private static void RenderConstellation(DrawingContext dc, IEnumerable<StepAnnotation> steps)
    {
        var ordered = steps.OrderBy(s => s.Number).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            var a = ordered[i - 1];
            var b = ordered[i];
            var from = new Point(a.X, a.Y);
            var to = new Point(b.X, b.Y);
            var v = to - from;
            var length = Math.Sqrt(v.X * v.X + v.Y * v.Y);
            if (length <= a.Radius + b.Radius + 4) continue;
            v /= length;
            var start = from + v * (a.Radius + 3);
            var end = to - v * (b.Radius + 3);
            var width = Math.Max(1.2, (a.Radius + b.Radius) / 16);
            var glow = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x38, 0xA9, 0xC8, 0xFF)), width * 4, lineCap: PenLineCap.Round);
            var line = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)), width,
                new ImmutableDashStyle(new[] { 0.0, 3.2 }, 0), PenLineCap.Round);
            dc.DrawLine(glow, start, end);
            dc.DrawLine(line, start, end);
            DrawSparkle(dc, start + (end - start) / 2, Math.Max(4, width * 3.2));
        }
    }

    private static void DrawSparkle(DrawingContext dc, Point c, double r)
    {
        var k = r * 0.22;
        var star = new StreamGeometry();
        using (var g = star.Open())
        {
            g.BeginFigure(new Point(c.X, c.Y - r), true);
            g.LineTo(new Point(c.X + k, c.Y - k));
            g.LineTo(new Point(c.X + r, c.Y));
            g.LineTo(new Point(c.X + k, c.Y + k));
            g.LineTo(new Point(c.X, c.Y + r));
            g.LineTo(new Point(c.X - k, c.Y + k));
            g.LineTo(new Point(c.X - r, c.Y));
            g.LineTo(new Point(c.X - k, c.Y - k));
            g.EndFigure(true);
        }
        dc.DrawEllipse(new ImmutableSolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xE8, 0x80)), null, c, r * 0.8, r * 0.8);
        dc.DrawGeometry(new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xC2)), null, star);
    }

    private Rect ImageArea(IEnumerable<Annotation> all)
    {
        if (_source is not null) return new Rect(0, 0, _width, _height);
        var r = new Rect(0, 0, 1, 1);
        foreach (var a in all) r = r.Union(a.Bounds);
        return r.Inflate(4000);
    }

    private void RenderBlur(DrawingContext dc, BlurAnnotation blur)
    {
        if (blur.IsDegenerate) return;
        if (_source is null || _pixels is null)
        {
            blur.Render(dc);
            return;
        }
        var x0 = Math.Clamp((int)Math.Floor(Math.Min(blur.X1, blur.X2)), 0, _width);
        var y0 = Math.Clamp((int)Math.Floor(Math.Min(blur.Y1, blur.Y2)), 0, _height);
        var x1 = Math.Clamp((int)Math.Ceiling(Math.Max(blur.X1, blur.X2)), 0, _width);
        var y1 = Math.Clamp((int)Math.Ceiling(Math.Max(blur.Y1, blur.Y2)), 0, _height);
        if (x1 - x0 < 1 || y1 - y0 < 1) return;
        var block = Math.Max(3, (int)Math.Round(blur.BlockSize));

        var key = (x0, y0, x1, y1, block);
        if (!_pixelated.TryGetValue(key, out var bitmap))
        {
            bitmap = Pixelate(x0, y0, x1 - x0, y1 - y0, block);
            if (_pixelated.Count > 64) _pixelated.Clear();
            _pixelated[key] = bitmap;
        }
        using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            dc.DrawImage(bitmap, new Rect(0, 0, x1 - x0, y1 - y0), new Rect(x0, y0, x1 - x0, y1 - y0));
    }

    /// <summary>Averages each block of pixels: unreadable, but still looks like the area it hides.</summary>
    private unsafe Bitmap Pixelate(int x, int y, int width, int height, int block)
    {
        var src = _pixels!;
        var wb = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = wb.Lock();
        var outStride = fb.RowBytes;
        var dst = (byte*)fb.Address;
        for (var by = 0; by < height; by += block)
        {
            var bh = Math.Min(block, height - by);
            for (var bx = 0; bx < width; bx += block)
            {
                var bw = Math.Min(block, width - bx);
                long b = 0, g = 0, r = 0;
                for (var yy = 0; yy < bh; yy++)
                {
                    var row = (y + by + yy) * _stride + (x + bx) * 4;
                    for (var xx = 0; xx < bw; xx++)
                    {
                        var i = row + xx * 4;
                        b += src[i]; g += src[i + 1]; r += src[i + 2];
                    }
                }
                var n = bw * bh;
                byte cb = (byte)(b / n), cg = (byte)(g / n), cr = (byte)(r / n);
                for (var yy = 0; yy < bh; yy++)
                {
                    var row = dst + (by + yy) * outStride + bx * 4;
                    for (var xx = 0; xx < bw; xx++)
                    {
                        var p = row + xx * 4;
                        p[0] = cb; p[1] = cg; p[2] = cr; p[3] = 255;
                    }
                }
            }
        }
        return wb;
    }

    /// <summary>Burns the annotations into the image and applies the crop.</summary>
    public Bitmap Flatten(Bitmap image)
    {
        if (IsEmpty) return image;
        var w = image.PixelSize.Width;
        var h = image.PixelSize.Height;
        if (!ReferenceEquals(_source, image)) Source = image;

        var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (var dc = rtb.CreateDrawingContext())
        {
            using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
                dc.DrawImage(image, new Rect(0, 0, w, h));
            using (dc.PushClip(new Rect(0, 0, w, h)))
                Render(dc);
        }

        if (CropRect is not { } crop) return rtb;
        var cx = Math.Clamp((int)Math.Round(crop.X), 0, w - 1);
        var cy = Math.Clamp((int)Math.Round(crop.Y), 0, h - 1);
        var cw = Math.Clamp((int)Math.Round(crop.Width), 1, w - cx);
        var ch = Math.Clamp((int)Math.Round(crop.Height), 1, h - cy);
        var cropped = new WriteableBitmap(new PixelSize(cw, ch), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = cropped.Lock())
            rtb.CopyPixels(new PixelRect(cx, cy, cw, ch), fb.Address, fb.RowBytes * ch, fb.RowBytes);
        rtb.Dispose();
        return cropped;
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(_items, Json);

    private static List<Annotation> Parse(string json) =>
        JsonSerializer.Deserialize<List<Annotation>>(json, Json) ?? new List<Annotation>();

    public static AnnotationDocument FromJson(string json)
    {
        var doc = new AnnotationDocument();
        doc._items = Parse(json);
        return doc;
    }
}
