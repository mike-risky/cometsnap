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

    protected override void OnRender(DrawingContext dc) => _document?.Render(dc, _inProgress);
}
