namespace Cometsnap.Linux.Trimming;

/// <summary>
/// Reads an animated GIF frame by frame: what is on screen at each frame, composited like a
/// browser (disposal, transparency, local palettes, interlacing). Frames are decoded on demand,
/// with a few snapshots kept along the way so jumping around the timeline stays quick.
/// </summary>
public sealed class GifReader
{
    private readonly byte[] _data;
    private readonly uint[]? _globalPalette;
    private readonly List<FrameInfo> _frames = new();
    private readonly Dictionary<int, (uint[] Canvas, uint[]? Restore)> _snapshots = new();
    private readonly int _snapshotEvery;
    private uint[] _canvas;
    private int _rendered = -1;          // index of the frame _canvas shows (-1 = blank)
    private uint[]? _restore;            // canvas to go back to after a "restore previous" frame
    private readonly byte[] _indices;

    private sealed record FrameInfo(int X, int Y, int W, int H, int Disposal, int Transparent,
        bool Interlaced, int PaletteOffset, int PaletteSize, int DataOffset, TimeSpan Start, TimeSpan Length);

    public int Width { get; }
    public int Height { get; }
    public int FrameCount => _frames.Count;
    public TimeSpan Duration { get; }

    /// <summary>When frame <paramref name="i"/> appears.</summary>
    public TimeSpan FrameStart(int i) => _frames[i].Start;

    /// <summary>How long frame <paramref name="i"/> stays on screen.</summary>
    public TimeSpan FrameLength(int i) => _frames[i].Length;

    public static GifReader Open(string path) => new(File.ReadAllBytes(path));

    public GifReader(byte[] data)
    {
        _data = data;
        if (data.Length < 13 || data[0] != 'G' || data[1] != 'I' || data[2] != 'F')
            throw new InvalidDataException("Not a GIF file.");
        Width = data[6] | data[7] << 8;
        Height = data[8] | data[9] << 8;
        if (Width <= 0 || Height <= 0) throw new InvalidDataException("The GIF has no size.");
        var flags = data[10];
        var p = 13;
        if ((flags & 0x80) != 0)
        {
            var size = 1 << ((flags & 7) + 1);
            _globalPalette = ReadPalette(p, size);
            p += size * 3;
        }

        int delay = 0, disposal = 0, transparent = -1;
        var time = TimeSpan.Zero;
        while (p < data.Length)
        {
            var block = data[p++];
            if (block == 0x3B) break;
            if (block == 0x21)
            {
                if (p >= data.Length) break;
                var label = data[p++];
                if (label == 0xF9 && p + 5 < data.Length && data[p] >= 4)
                {
                    var packed = data[p + 1];
                    delay = data[p + 2] | data[p + 3] << 8;
                    disposal = (packed >> 2) & 7;
                    transparent = (packed & 1) != 0 ? data[p + 4] : -1;
                }
                p = SkipSubBlocks(p);
                continue;
            }
            if (block != 0x2C || p + 9 > data.Length) break;

            var x = data[p] | data[p + 1] << 8;
            var y = data[p + 2] | data[p + 3] << 8;
            var w = data[p + 4] | data[p + 5] << 8;
            var h = data[p + 6] | data[p + 7] << 8;
            var lflags = data[p + 8];
            p += 9;
            int paletteOffset = -1, paletteSize = 0;
            if ((lflags & 0x80) != 0)
            {
                paletteSize = 1 << ((lflags & 7) + 1);
                paletteOffset = p;
                p += paletteSize * 3;
            }
            var dataOffset = p;
            p++; // LZW minimum code size
            p = SkipSubBlocks(p);
            if (p > data.Length) break;

            // Browsers show "no delay" (0 or 1 hundredths) as 0.1 s; do the same so timing matches.
            var length = TimeSpan.FromMilliseconds((delay < 2 ? 10 : delay) * 10);
            _frames.Add(new FrameInfo(x, y, w, h, disposal, transparent, (lflags & 0x40) != 0,
                paletteOffset, paletteSize, dataOffset, time, length));
            time += length;
            delay = 0; disposal = 0; transparent = -1;
        }
        if (_frames.Count == 0) throw new InvalidDataException("The GIF has no frames.");
        Duration = time;
        _canvas = new uint[Width * Height];
        _indices = new byte[Width * Height];
        // Keep snapshots within about 256 MB in total.
        var bytesPerFrame = (long)Width * Height * 4;
        _snapshotEvery = (int)Math.Max(8, _frames.Count * bytesPerFrame / (256L * 1024 * 1024) + 1);
    }

    /// <summary>The frame on screen at <paramref name="t"/>.</summary>
    public int FrameAt(TimeSpan t)
    {
        int lo = 0, hi = _frames.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (_frames[mid].Start <= t) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>
    /// The picture of frame <paramref name="index"/>, as 0xAARRGGBB pixels (that is BGRA bytes),
    /// top-down. The array is reused: copy it if you keep it.
    /// </summary>
    public uint[] Render(int index)
    {
        index = Math.Clamp(index, 0, _frames.Count - 1);
        if (index == _rendered) return _canvas;
        var lowest = index < _rendered ? -1 : _rendered;
        var best = -1;
        foreach (var k in _snapshots.Keys) if (k <= index && k > lowest && k > best) best = k;
        if (best >= 0)
        {
            var (canvas, restore) = _snapshots[best];
            Array.Copy(canvas, _canvas, _canvas.Length);
            _restore = restore is null ? null : (uint[])restore.Clone();
            _rendered = best;
        }
        else if (index < _rendered)
        {
            Array.Clear(_canvas);
            _restore = null;
            _rendered = -1;
        }
        while (_rendered < index) Step();
        return _canvas;
    }

    private void Step()
    {
        // Undo what the frame on screen asked for before drawing the next one.
        if (_rendered >= 0)
        {
            var prev = _frames[_rendered];
            if (prev.Disposal == 2) ClearRect(prev);
            else if (prev.Disposal == 3 && _restore is not null) Array.Copy(_restore, _canvas, _canvas.Length);
        }
        var f = _frames[_rendered + 1];
        _restore = f.Disposal == 3 ? (uint[])_canvas.Clone() : null;
        Draw(f);
        _rendered++;
        if (_rendered % _snapshotEvery == 0 && !_snapshots.ContainsKey(_rendered))
            _snapshots[_rendered] = ((uint[])_canvas.Clone(), _restore is null ? null : (uint[])_restore.Clone());
    }

    private void ClearRect(FrameInfo f)
    {
        for (var y = Math.Max(0, f.Y); y < Math.Min(Height, f.Y + f.H); y++)
            Array.Clear(_canvas, y * Width + Math.Max(0, f.X), Math.Max(0, Math.Min(Width, f.X + f.W) - Math.Max(0, f.X)));
    }

    private void Draw(FrameInfo f)
    {
        var palette = f.PaletteOffset >= 0 ? ReadPalette(f.PaletteOffset, f.PaletteSize) : _globalPalette;
        if (palette is null || f.W <= 0 || f.H <= 0) return;
        var count = f.W * f.H;
        var indices = count <= _indices.Length ? _indices : new byte[count];
        var decoded = Lzw(f.DataOffset, indices, count);

        for (var i = 0; i < decoded; i++)
        {
            int row = i / f.W, col = i % f.W;
            var y = f.Y + (f.Interlaced ? InterlacedRow(row, f.H) : row);
            var x = f.X + col;
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) continue;
            var idx = indices[i];
            if (idx == f.Transparent || idx >= palette.Length) continue;
            _canvas[y * Width + x] = palette[idx];
        }
    }

    private static int InterlacedRow(int row, int height)
    {
        // Passes: every 8th from 0, every 8th from 4, every 4th from 2, every 2nd from 1.
        var p1 = (height + 7) / 8;
        if (row < p1) return row * 8;
        row -= p1;
        var p2 = (height + 3) / 8;
        if (row < p2) return row * 8 + 4;
        row -= p2;
        var p3 = (height + 1) / 4;
        if (row < p3) return row * 4 + 2;
        row -= p3;
        return row * 2 + 1;
    }

    private uint[] ReadPalette(int offset, int size)
    {
        var palette = new uint[size];
        for (var i = 0; i < size && offset + i * 3 + 2 < _data.Length; i++)
        {
            var o = offset + i * 3;
            palette[i] = 0xFF000000u | (uint)_data[o] << 16 | (uint)_data[o + 1] << 8 | _data[o + 2];
        }
        return palette;
    }

    private int SkipSubBlocks(int p)
    {
        while (p < _data.Length && _data[p] != 0) p += _data[p] + 1;
        return p + 1;
    }

    private readonly short[] _prefix = new short[4096];
    private readonly byte[] _suffix = new byte[4096];
    private readonly byte[] _first = new byte[4096];
    private readonly byte[] _stack = new byte[4097];

    /// <summary>Decodes the LZW data of a frame. Returns how many indices came out (tolerates broken files).</summary>
    private int Lzw(int offset, byte[] output, int expected)
    {
        var minCode = _data[offset];
        if (minCode is < 2 or > 11) return 0;
        var p = offset + 1;
        var blockLeft = 0;
        var clear = 1 << minCode;
        var end = clear + 1;
        for (var i = 0; i < clear; i++) { _prefix[i] = -1; _suffix[i] = (byte)i; _first[i] = (byte)i; }

        int size = minCode + 1, mask = (1 << size) - 1, avail = clear + 2, old = -1;
        int bits = 0, acc = 0, written = 0;
        while (written < expected)
        {
            while (bits < size)
            {
                if (blockLeft == 0)
                {
                    if (p >= _data.Length || _data[p] == 0) return written;
                    blockLeft = _data[p++];
                }
                if (p >= _data.Length) return written;
                acc |= _data[p++] << bits;
                blockLeft--;
                bits += 8;
            }
            var code = acc & mask;
            acc >>= size;
            bits -= size;

            if (code == clear)
            {
                size = minCode + 1; mask = (1 << size) - 1; avail = clear + 2; old = -1;
                continue;
            }
            if (code == end || code > avail) return written;

            if (old >= 0 && avail < 4096)
            {
                _prefix[avail] = (short)old;
                _first[avail] = _first[old];
                _suffix[avail] = code == avail ? _first[old] : _first[code];
                avail++;
            }
            else if (code == avail)
            {
                return written;
            }

            var n = 0;
            for (var c = code; c >= 0 && n < _stack.Length; c = _prefix[c]) _stack[n++] = _suffix[c];
            for (var i = n - 1; i >= 0 && written < expected; i--) output[written++] = _stack[i];
            old = code;

            if ((avail & mask) == 0 && avail < 4096)
            {
                size++;
                mask = (1 << size) - 1;
            }
        }
        return written;
    }
}
