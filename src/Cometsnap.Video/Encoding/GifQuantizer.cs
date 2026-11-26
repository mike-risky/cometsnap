namespace Cometsnap.Video.Encoding;

/// <summary>
/// Turns BGRA pixels into at most 255 palette colours (index 255 is kept for "transparent",
/// meaning "unchanged since the previous frame"). Screens often have few colours: those are
/// kept exactly. Otherwise a median cut over a 15-bit histogram is used.
/// </summary>
internal sealed class GifQuantizer
{
    public const int MaxColors = 255;
    public const byte TransparentIndex = 255;

    private readonly int[] _counts = new int[32768];
    private readonly long[] _sumR = new long[32768];
    private readonly long[] _sumG = new long[32768];
    private readonly long[] _sumB = new long[32768];
    private readonly byte[] _lut = new byte[32768];
    private readonly Dictionary<int, byte> _exact = new(MaxColors + 1);
    private int[] _keys = Array.Empty<int>();

    /// <summary>The palette produced by the last call, as RGB triplets (256 entries, padded).</summary>
    public byte[] Palette { get; } = new byte[256 * 3];

    /// <summary>
    /// Quantizes the <paramref name="w"/>×<paramref name="h"/> box at (<paramref name="x0"/>,<paramref name="y0"/>)
    /// of <paramref name="frame"/>. Pixels equal to <paramref name="previous"/> (when given) become transparent.
    /// </summary>
    public void Quantize(ReadOnlySpan<uint> frame, ReadOnlySpan<uint> previous, int stride, int x0, int y0, int w, int h, Span<byte> indices)
    {
        var diff = !previous.IsEmpty;
        Array.Clear(Palette);

        if (TryExact(frame, previous, diff, stride, x0, y0, w, h, indices)) return;

        // ---- histogram
        Array.Clear(_counts);
        Array.Clear(_sumR);
        Array.Clear(_sumG);
        Array.Clear(_sumB);
        for (var y = 0; y < h; y++)
        {
            var row = (y0 + y) * stride + x0;
            for (var x = 0; x < w; x++)
            {
                var p = frame[row + x];
                if (diff && ((p ^ previous[row + x]) & 0xFFFFFF) == 0) continue;
                var k = Key(p);
                _counts[k]++;
                _sumB[k] += p & 0xFF;
                _sumG[k] += (p >> 8) & 0xFF;
                _sumR[k] += (p >> 16) & 0xFF;
            }
        }

        var used = 0;
        if (_keys.Length < 32768) _keys = new int[32768];
        for (var k = 0; k < 32768; k++)
            if (_counts[k] > 0) _keys[used++] = k;

        MedianCut(used);

        // ---- map
        for (var y = 0; y < h; y++)
        {
            var row = (y0 + y) * stride + x0;
            var o = y * w;
            for (var x = 0; x < w; x++)
            {
                var p = frame[row + x];
                indices[o + x] = diff && ((p ^ previous[row + x]) & 0xFFFFFF) == 0 ? TransparentIndex : _lut[Key(p)];
            }
        }
    }

    private bool TryExact(ReadOnlySpan<uint> frame, ReadOnlySpan<uint> previous, bool diff, int stride, int x0, int y0, int w, int h, Span<byte> indices)
    {
        _exact.Clear();
        var last = uint.MaxValue;
        byte lastIndex = 0;
        for (var y = 0; y < h; y++)
        {
            var row = (y0 + y) * stride + x0;
            var o = y * w;
            for (var x = 0; x < w; x++)
            {
                var p = frame[row + x];
                if (diff && ((p ^ previous[row + x]) & 0xFFFFFF) == 0)
                {
                    indices[o + x] = TransparentIndex;
                    continue;
                }
                var c = p & 0xFFFFFF;
                if (c == last)
                {
                    indices[o + x] = lastIndex;
                    continue;
                }
                if (!_exact.TryGetValue((int)c, out var index))
                {
                    if (_exact.Count == MaxColors) return false;
                    index = (byte)_exact.Count;
                    _exact[(int)c] = index;
                    Palette[index * 3] = (byte)(c >> 16);
                    Palette[index * 3 + 1] = (byte)(c >> 8);
                    Palette[index * 3 + 2] = (byte)c;
                }
                last = c;
                lastIndex = index;
                indices[o + x] = index;
            }
        }
        return true;
    }

    private static int Key(uint p) => (int)(((p >> 9) & 0x7C00) | ((p >> 6) & 0x03E0) | ((p >> 3) & 0x001F));

    private static int R5(int k) => (k >> 10) & 31;
    private static int G5(int k) => (k >> 5) & 31;
    private static int B5(int k) => k & 31;

    private readonly record struct Box(int Start, int Length, long Pixels, int Range);

    private void MedianCut(int used)
    {
        var boxes = new List<Box>(MaxColors) { Measure(0, used) };
        while (boxes.Count < MaxColors)
        {
            // Split the box with the most "error": wide colour range × many pixels.
            var best = -1;
            double bestScore = 0;
            for (var i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                if (b.Length < 2) continue;
                var score = (double)b.Range * b.Pixels;
                if (score > bestScore) { bestScore = score; best = i; }
            }
            if (best < 0) break;

            var box = boxes[best];
            var span = _keys.AsSpan(box.Start, box.Length);
            var axis = LongestAxis(span);
            Comparison<int> cmp = axis switch
            {
                0 => (a, b) => R5(a).CompareTo(R5(b)),
                1 => (a, b) => G5(a).CompareTo(G5(b)),
                _ => (a, b) => B5(a).CompareTo(B5(b)),
            };
            span.Sort(cmp);

            long half = box.Pixels / 2, acc = 0;
            var cut = 1;
            for (var i = 0; i < span.Length - 1; i++)
            {
                acc += _counts[span[i]];
                cut = i + 1;
                if (acc >= half) break;
            }
            boxes[best] = Measure(box.Start, cut);
            boxes.Add(Measure(box.Start + cut, box.Length - cut));
        }

        for (var i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            long r = 0, g = 0, bl = 0, n = 0;
            for (var j = b.Start; j < b.Start + b.Length; j++)
            {
                var k = _keys[j];
                r += _sumR[k]; g += _sumG[k]; bl += _sumB[k]; n += _counts[k];
                _lut[k] = (byte)i;
            }
            if (n == 0) continue;
            Palette[i * 3] = (byte)(r / n);
            Palette[i * 3 + 1] = (byte)(g / n);
            Palette[i * 3 + 2] = (byte)(bl / n);
        }
    }

    private Box Measure(int start, int length)
    {
        long pixels = 0;
        for (var i = start; i < start + length; i++) pixels += _counts[_keys[i]];
        var span = _keys.AsSpan(start, length);
        return new Box(start, length, pixels, Range(span, LongestAxis(span)));
    }

    private static int LongestAxis(ReadOnlySpan<int> keys)
    {
        var (r, g, b) = (Range(keys, 0), Range(keys, 1), Range(keys, 2));
        // Eyes are most sensitive to green, then red.
        return g * 3 >= r * 2 && g * 3 >= b * 4 ? 1 : r * 2 >= b * 4 ? 0 : 2;
    }

    private static int Range(ReadOnlySpan<int> keys, int axis)
    {
        int min = 31, max = 0;
        foreach (var k in keys)
        {
            var v = axis == 0 ? R5(k) : axis == 1 ? G5(k) : B5(k);
            if (v < min) min = v;
            if (v > max) max = v;
        }
        return keys.IsEmpty ? 0 : max - min;
    }
}
