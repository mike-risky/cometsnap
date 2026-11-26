using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cometsnap.Editor;

/// <summary>
/// An ordered list of annotations with undo/redo of every change (add, move, recolor, delete).
/// Pixel effects (blur, spotlight, crop) need the image: set <see cref="Source"/>.
/// </summary>
public sealed class AnnotationDocument
{
    private List<Annotation> _items = new();
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private readonly Dictionary<(int, int, int, int, int), BitmapSource> _pixelated = new();

    private BitmapSource? _source;
    private byte[]? _pixels;
    private int _stride;

    public IReadOnlyList<Annotation> Items => _items;
    public bool IsEmpty => _items.Count == 0;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public event Action? Changed;

    /// <summary>The image under the annotations, in annotation coordinates (0,0 = its top-left pixel).</summary>
    public BitmapSource? Source
    {
        get => _source;
        set
        {
            _source = value;
            _pixelated.Clear();
            _pixels = null;
            if (value is null) return;
            var bgra = new FormatConvertedBitmap(value, PixelFormats.Bgra32, null, 0);
            _stride = bgra.PixelWidth * 4;
            _pixels = new byte[_stride * bgra.PixelHeight];
            bgra.CopyPixels(_pixels, _stride, 0);
        }
    }

    /// <summary>Call before changing a shape in place (move, recolor), so it can be undone.</summary>
    public void Checkpoint()
    {
        _undo.Push(ToJson());
        _redo.Clear();
    }

    /// <summary>Tells listeners a shape changed in place.</summary>
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

    public void Clear()
    {
        _items.Clear();
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }

    /// <summary>The topmost shape under <paramref name="p"/>.</summary>
    public Annotation? HitTest(Point p, double tolerance)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
            if (_items[i].HitTest(p, tolerance)) return _items[i];
        return null;
    }

    /// <summary>The next free step number.</summary>
    public int NextStepNumber() =>
        _items.OfType<StepAnnotation>().Select(s => s.Number).DefaultIfEmpty(0).Max() + 1;

    /// <summary>The crop that applies (the last one), if any.</summary>
    public Rect? CropRect =>
        _items.OfType<CropAnnotation>().LastOrDefault() is { IsDegenerate: false } c ? c.Box : null;

    /// <summary>An independent copy, moved by (dx, dy): e.g. from screen to image coordinates.</summary>
    public AnnotationDocument CopyOffset(double dx, double dy)
    {
        var copy = FromJson(ToJson());
        foreach (var a in copy._items) a.Offset(dx, dy);
        return copy;
    }

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
            var area = ImageArea(all);
            Geometry dim = new RectangleGeometry(area);
            foreach (var s in spots)
                dim = Geometry.Combine(dim, new RectangleGeometry(s.Box, 6, 6), GeometryCombineMode.Exclude, null);
            var brush = new SolidColorBrush(Color.FromArgb((byte)(255 * spots[^1].Dim), 0, 0, 0));
            brush.Freeze();
            dc.DrawGeometry(brush, null, dim);
        }

        foreach (var a in all)
            if (a is not (BlurAnnotation or SpotlightAnnotation or CropAnnotation)) a.Render(dc);

        if (editing)
        {
            var crop = all.OfType<CropAnnotation>().LastOrDefault();
            if (crop is { IsDegenerate: false })
            {
                var area = ImageArea(all);
                var outside = Geometry.Combine(new RectangleGeometry(area), new RectangleGeometry(crop.Box), GeometryCombineMode.Exclude, null);
                var shade = new SolidColorBrush(Color.FromArgb(0xB0, 0x07, 0x11, 0x3A));
                shade.Freeze();
                dc.DrawGeometry(shade, null, outside);
                var frame = new Pen(Brushes.White, Math.Max(1, crop.Thickness / 2)) { DashStyle = DashStyles.Dash };
                frame.Freeze();
                dc.DrawRectangle(null, frame, crop.Box);
            }
        }
    }

    private Rect ImageArea(IEnumerable<Annotation> all)
    {
        if (_source is not null) return new Rect(0, 0, _source.PixelWidth, _source.PixelHeight);
        var r = new Rect(0, 0, 1, 1);
        foreach (var a in all) r.Union(a.Bounds);
        r.Inflate(4000, 4000); // no image known: dim "everything"
        return r;
    }

    private void RenderBlur(DrawingContext dc, BlurAnnotation blur)
    {
        if (blur.IsDegenerate) return;
        if (_source is null || _pixels is null)
        {
            blur.Render(dc);
            return;
        }
        var w = _source.PixelWidth;
        var h = _source.PixelHeight;
        var x0 = Math.Clamp((int)Math.Floor(Math.Min(blur.X1, blur.X2)), 0, w);
        var y0 = Math.Clamp((int)Math.Floor(Math.Min(blur.Y1, blur.Y2)), 0, h);
        var x1 = Math.Clamp((int)Math.Ceiling(Math.Max(blur.X1, blur.X2)), 0, w);
        var y1 = Math.Clamp((int)Math.Ceiling(Math.Max(blur.Y1, blur.Y2)), 0, h);
        if (x1 - x0 < 1 || y1 - y0 < 1) return;
        var block = Math.Max(3, (int)Math.Round(blur.BlockSize));

        var key = (x0, y0, x1, y1, block);
        if (!_pixelated.TryGetValue(key, out var bitmap))
        {
            bitmap = Pixelate(x0, y0, x1 - x0, y1 - y0, block);
            if (_pixelated.Count > 64) _pixelated.Clear();
            _pixelated[key] = bitmap;
        }
        dc.DrawImage(bitmap, new Rect(x0, y0, x1 - x0, y1 - y0));
    }

    /// <summary>Averages each block of pixels: unreadable, but still looks like the area it hides.</summary>
    private BitmapSource Pixelate(int x, int y, int width, int height, int block)
    {
        var src = _pixels!;
        var outStride = width * 4;
        var dst = new byte[outStride * height];
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
                var cb = (byte)(b / n); var cg = (byte)(g / n); var cr = (byte)(r / n);
                for (var yy = 0; yy < bh; yy++)
                {
                    var row = (by + yy) * outStride + bx * 4;
                    for (var xx = 0; xx < bw; xx++)
                    {
                        var i = row + xx * 4;
                        dst[i] = cb; dst[i + 1] = cg; dst[i + 2] = cr; dst[i + 3] = 255;
                    }
                }
            }
        }
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, dst, outStride);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>
    /// Burns the annotations into the image (and applies the crop). <paramref name="origin"/> is the
    /// position of the image's top-left pixel in annotation coordinates.
    /// </summary>
    public BitmapSource Flatten(BitmapSource image, Point origin)
    {
        if (IsEmpty) return image;

        var w = image.PixelWidth;
        var h = image.PixelHeight;
        var previous = _source;
        var sameImage = ReferenceEquals(previous, image) && origin == default;
        if (!sameImage && origin == default) Source = image;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(image, new Rect(0, 0, w, h));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
            dc.PushTransform(new TranslateTransform(-origin.X, -origin.Y));
            Render(dc);
            dc.Pop();
            dc.Pop();
        }
        if (!sameImage && origin == default) Source = previous;

        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        BitmapSource result = bitmap;

        if (CropRect is { } crop)
        {
            var c = new Int32Rect(
                Math.Clamp((int)Math.Round(crop.X - origin.X), 0, w - 1),
                Math.Clamp((int)Math.Round(crop.Y - origin.Y), 0, h - 1), 0, 0);
            c.Width = Math.Clamp((int)Math.Round(crop.Width), 1, w - c.X);
            c.Height = Math.Clamp((int)Math.Round(crop.Height), 1, h - c.Y);
            result = new CroppedBitmap(bitmap, c);
        }
        result.Freeze();
        return result;
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
