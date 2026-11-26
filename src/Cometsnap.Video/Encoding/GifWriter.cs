using System.Runtime.InteropServices;

namespace Cometsnap.Video.Encoding;

/// <summary>
/// Streaming animated GIF writer. Each frame only stores the box of pixels that changed, and
/// unchanged pixels inside that box are transparent, so screen recordings stay small.
/// Frames identical to the previous one are merged (the previous frame is simply shown longer).
/// </summary>
internal sealed class GifWriter : IDisposable
{
    private readonly Stream _out;
    private readonly int _width, _height;
    private readonly uint[] _previous;
    private readonly GifQuantizer _quantizer = new();
    private readonly LzwEncoder _lzw = new();
    private readonly MemoryStream _pending = new();
    private byte[] _indices = Array.Empty<byte>();
    private bool _hasPrevious;
    private bool _pendingTransparent;
    private TimeSpan _pendingStart;
    private bool _finished;

    public int FramesWritten { get; private set; }

    public GifWriter(Stream output, int width, int height)
    {
        _out = output;
        _width = width;
        _height = height;
        _previous = new uint[width * height];
        WriteHeader();
    }

    private void WriteHeader()
    {
        var w = new BinaryWriter(_out);
        w.Write("GIF89a"u8);
        w.Write((ushort)_width);
        w.Write((ushort)_height);
        w.Write((byte)0x70); // no global colour table, 8-bit colour resolution
        w.Write((byte)0);    // background colour index
        w.Write((byte)0);    // square pixels
        // NETSCAPE2.0: loop forever
        w.Write(new byte[] { 0x21, 0xFF, 0x0B });
        w.Write("NETSCAPE2.0"u8);
        w.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
        w.Flush();
    }

    /// <summary>Adds a top-down BGRA frame (stride = width × 4) shown from <paramref name="time"/>.</summary>
    public void AddFrame(ReadOnlySpan<byte> bgra, TimeSpan time)
    {
        var pixels = MemoryMarshal.Cast<byte, uint>(bgra)[..(_width * _height)];
        int x0 = 0, y0 = 0, x1 = _width - 1, y1 = _height - 1;
        if (_hasPrevious && !ChangedBox(pixels, out x0, out y0, out x1, out y1)) return;

        FlushPending(time);

        var w = x1 - x0 + 1;
        var h = y1 - y0 + 1;
        if (_indices.Length < w * h) _indices = new byte[w * h];
        var indices = _indices.AsSpan(0, w * h);
        _quantizer.Quantize(pixels, _hasPrevious ? _previous : ReadOnlySpan<uint>.Empty, _width, x0, y0, w, h, indices);

        _pending.SetLength(0);
        var bw = new BinaryWriter(_pending);
        bw.Write((byte)0x2C);
        bw.Write((ushort)x0);
        bw.Write((ushort)y0);
        bw.Write((ushort)w);
        bw.Write((ushort)h);
        bw.Write((byte)0x87); // local colour table, 256 entries
        bw.Write(_quantizer.Palette);
        bw.Flush();
        _lzw.Encode(indices, 8, _pending);

        _pendingTransparent = _hasPrevious;
        _pendingStart = time;
        pixels.CopyTo(_previous);
        _hasPrevious = true;
    }

    /// <summary>Finds the bounding box of pixels that differ from the previous frame (alpha ignored).</summary>
    private bool ChangedBox(ReadOnlySpan<uint> pixels, out int x0, out int y0, out int x1, out int y1)
    {
        x0 = _width; y0 = _height; x1 = -1; y1 = -1;
        for (var y = 0; y < _height; y++)
        {
            var row = pixels.Slice(y * _width, _width);
            var prev = _previous.AsSpan(y * _width, _width);
            if (row.SequenceEqual(prev)) continue;
            int first = -1, last = -1;
            for (var x = 0; x < _width; x++)
            {
                if (((row[x] ^ prev[x]) & 0xFFFFFF) == 0) continue;
                if (first < 0) first = x;
                last = x;
            }
            if (first < 0) continue;
            if (y < y0) y0 = y;
            y1 = y;
            if (first < x0) x0 = first;
            if (last > x1) x1 = last;
        }
        return x1 >= 0;
    }

    private void FlushPending(TimeSpan nextStart)
    {
        if (_pending.Length == 0) return;
        var delay = EncoderMath.GifDelay(_pendingStart, nextStart);
        // Graphics control extension: disposal "leave in place", optional transparency.
        _out.Write(new byte[]
        {
            0x21, 0xF9, 0x04,
            (byte)(0x04 | (_pendingTransparent ? 0x01 : 0x00)),
            (byte)(delay & 0xFF), (byte)(delay >> 8),
            GifQuantizer.TransparentIndex,
            0x00,
        });
        _pending.Position = 0;
        _pending.CopyTo(_out);
        _pending.SetLength(0);
        FramesWritten++;
    }

    /// <summary>Writes the last frame (shown until <paramref name="end"/>) and the trailer.</summary>
    public void Finish(TimeSpan end)
    {
        if (_finished) return;
        _finished = true;
        FlushPending(end);
        _out.WriteByte(0x3B);
        _out.Flush();
    }

    public void Dispose()
    {
        _pending.Dispose();
        _out.Dispose();
    }
}
