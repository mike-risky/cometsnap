namespace Cometsnap.Core.Geometry;

/// <summary>An integer rectangle in physical pixels. Right/Bottom are exclusive.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;

    public static PixelRect FromEdges(int left, int top, int right, int bottom) =>
        new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));

    /// <summary>
    /// The rectangle spanned by two pixels, both included. Dragging from pixel (10,10)
    /// to pixel (19,19) selects exactly 10 × 10 pixels, whatever the drag direction.
    /// </summary>
    public static PixelRect FromPixels(int x1, int y1, int x2, int y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1) + 1, Math.Abs(y2 - y1) + 1);

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    public bool Contains(PixelRect other) =>
        other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;

    public PixelRect Intersect(PixelRect other)
    {
        var l = Math.Max(X, other.X);
        var t = Math.Max(Y, other.Y);
        var r = Math.Min(Right, other.Right);
        var b = Math.Min(Bottom, other.Bottom);
        return r > l && b > t ? FromEdges(l, t, r, b) : default;
    }

    public PixelRect Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    public PixelRect Inflate(int d) => new(X - d, Y - d, Width + 2 * d, Height + 2 * d);

    /// <summary>Moves (never resizes, unless too big) the rectangle so it lies inside <paramref name="bounds"/>.</summary>
    public PixelRect KeepInside(PixelRect bounds)
    {
        var w = Math.Min(Width, bounds.Width);
        var h = Math.Min(Height, bounds.Height);
        var x = Math.Clamp(X, bounds.X, bounds.Right - w);
        var y = Math.Clamp(Y, bounds.Y, bounds.Bottom - h);
        return new PixelRect(x, y, w, h);
    }

    public override string ToString() => $"{X},{Y} {Width}×{Height}";
}
