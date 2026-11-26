namespace Cometsnap.Core.Geometry;

/// <summary>
/// Pulls a dragged selection edge onto nearby window edges, so a free-hand
/// rectangle lands exactly on a window border.
/// Values are pixel coordinates of the edge pixel itself (inclusive).
/// </summary>
public sealed class EdgeSnapper
{
    private readonly int[] _xs;
    private readonly int[] _ys;

    public int Threshold { get; }

    public EdgeSnapper(IEnumerable<PixelRect> rects, int threshold)
    {
        Threshold = threshold;
        var xs = new SortedSet<int>();
        var ys = new SortedSet<int>();
        foreach (var r in rects)
        {
            if (r.IsEmpty) continue;
            xs.Add(r.X); xs.Add(r.Right - 1);
            ys.Add(r.Y); ys.Add(r.Bottom - 1);
        }
        _xs = xs.ToArray();
        _ys = ys.ToArray();
    }

    public int SnapX(int x) => Snap(_xs, x, Threshold);
    public int SnapY(int y) => Snap(_ys, y, Threshold);

    private static int Snap(int[] edges, int value, int threshold)
    {
        if (edges.Length == 0 || threshold <= 0) return value;
        var i = Array.BinarySearch(edges, value);
        if (i >= 0) return value;
        i = ~i;
        var best = value;
        var bestDist = threshold + 1;
        if (i < edges.Length && edges[i] - value < bestDist) { best = edges[i]; bestDist = edges[i] - value; }
        if (i > 0 && value - edges[i - 1] < bestDist) { best = edges[i - 1]; }
        return best;
    }
}
