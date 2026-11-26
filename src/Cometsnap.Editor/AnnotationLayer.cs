using System.Windows;
using System.Windows.Media;

namespace Cometsnap.Editor;

/// <summary>Draws a document (plus the shape being drawn) on screen.</summary>
public sealed class AnnotationLayer : FrameworkElement
{
    private AnnotationDocument? _document;

    public AnnotationLayer()
    {
        IsHitTestVisible = false;
    }

    public AnnotationDocument? Document
    {
        get => _document;
        set
        {
            if (_document is not null) _document.Changed -= InvalidateVisual;
            _document = value;
            if (_document is not null) _document.Changed += InvalidateVisual;
            InvalidateVisual();
        }
    }

    private Annotation? _inProgress;

    /// <summary>The shape currently being dragged out (not yet in the document).</summary>
    public Annotation? InProgress
    {
        get => _inProgress;
        set { _inProgress = value; InvalidateVisual(); }
    }

    /// <summary>Editor mode: show the crop frame instead of applying it.</summary>
    public bool Editing { get; set; }

    private Annotation? _selected;

    /// <summary>The shape picked with the select tool, drawn with a dashed frame.</summary>
    public Annotation? Selected
    {
        get => _selected;
        set { _selected = value; InvalidateVisual(); }
    }

    /// <summary>Screen-to-image scale, so the selection frame stays one screen pixel thin.</summary>
    public double PixelSize { get; set; } = 1;

    protected override void OnRender(DrawingContext dc)
    {
        _document?.Render(dc, _inProgress, Editing);
        if (_selected is null) return;
        var b = _selected.Bounds;
        var pad = 4 * PixelSize;
        b.Inflate(pad, pad);
        var dark = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0x07, 0x11, 0x3A)), 3 * PixelSize);
        var blue = new Pen(new SolidColorBrush(Color.FromRgb(0x3D, 0x6B, 0xFF)), 1.5 * PixelSize) { DashStyle = DashStyles.Dash };
        dc.DrawRoundedRectangle(null, dark, b, 3 * PixelSize, 3 * PixelSize);
        dc.DrawRoundedRectangle(null, blue, b, 3 * PixelSize, 3 * PixelSize);
    }
}
