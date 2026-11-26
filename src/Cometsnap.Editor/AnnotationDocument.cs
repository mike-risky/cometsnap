using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cometsnap.Editor;

/// <summary>An ordered list of annotations with undo/redo.</summary>
public sealed class AnnotationDocument
{
    private readonly List<Annotation> _items = new();
    private readonly Stack<Annotation> _redo = new();

    public IReadOnlyList<Annotation> Items => _items;
    public bool IsEmpty => _items.Count == 0;
    public bool CanUndo => _items.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public event Action? Changed;

    public void Add(Annotation annotation)
    {
        _items.Add(annotation);
        _redo.Clear();
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (_items.Count == 0) return;
        _redo.Push(_items[^1]);
        _items.RemoveAt(_items.Count - 1);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _items.Add(_redo.Pop());
        Changed?.Invoke();
    }

    public void Clear()
    {
        _items.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }

    public void Render(DrawingContext dc, Annotation? inProgress = null)
    {
        foreach (var a in _items) a.Render(dc);
        inProgress?.Render(dc);
    }

    /// <summary>
    /// Burns the annotations into the image. <paramref name="origin"/> is the position of the
    /// image's top-left pixel in annotation coordinates.
    /// </summary>
    public BitmapSource Flatten(BitmapSource image, Point origin)
    {
        if (IsEmpty) return image;

        var w = image.PixelWidth;
        var h = image.PixelHeight;
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

        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(_items, Json);

    public static AnnotationDocument FromJson(string json)
    {
        var doc = new AnnotationDocument();
        var items = JsonSerializer.Deserialize<List<Annotation>>(json, Json);
        if (items is not null) doc._items.AddRange(items);
        return doc;
    }
}
