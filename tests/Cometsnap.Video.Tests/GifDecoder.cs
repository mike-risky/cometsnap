namespace Cometsnap.Video.Tests;

/// <summary>
/// A minimal, spec-following GIF decoder (same LZW rules as stb_image / giflib) used to check
/// what the encoder writes. Composites frames like a browser with disposal "leave in place".
/// </summary>
internal sealed class GifDecoder
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public List<(uint[] Pixels, int Delay)> Frames { get; } = new();
    public bool Loops { get; private set; }
    public List<(int X, int Y, int W, int H)> Rects { get; } = new();

    public static GifDecoder Decode(byte[] data)
    {
        var d = new GifDecoder();
        d.Run(data);
        return d;
    }

    private void Run(byte[] b)
    {
        var p = 0;
        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(b, 0, 6));
        Width = b[6] | b[7] << 8;
        Height = b[8] | b[9] << 8;
        var flags = b[10];
        p = 13;
        Assert.Equal(0, flags & 0x80); // we never write a global table
        var canvas = new uint[Width * Height];
        int delay = 0, transparent = -1;
        while (true)
        {
            var block = b[p++];
            if (block == 0x3B) break;
            if (block == 0x21)
            {
                var label = b[p++];
                if (label == 0xF9)
                {
                    Assert.Equal(4, b[p]);
                    var packed = b[p + 1];
                    delay = b[p + 2] | b[p + 3] << 8;
                    transparent = (packed & 1) != 0 ? b[p + 4] : -1;
                    p += 6; // size + 4 bytes + terminator
                    continue;
                }
                if (label == 0xFF && System.Text.Encoding.ASCII.GetString(b, p + 1, 11) == "NETSCAPE2.0") Loops = true;
                while (b[p] != 0) p += b[p] + 1;
                p++;
                continue;
            }
            Assert.Equal(0x2C, block);
            var x0 = b[p] | b[p + 1] << 8;
            var y0 = b[p + 2] | b[p + 3] << 8;
            var w = b[p + 4] | b[p + 5] << 8;
            var h = b[p + 6] | b[p + 7] << 8;
            var lflags = b[p + 8];
            p += 9;
            Assert.True((lflags & 0x80) != 0);
            var size = 1 << ((lflags & 7) + 1);
            var palette = new uint[size];
            for (var i = 0; i < size; i++, p += 3) palette[i] = (uint)(b[p] << 16 | b[p + 1] << 8 | b[p + 2]);
            var minCode = b[p++];
            var data = new List<byte>();
            while (b[p] != 0)
            {
                var n = b[p++];
                data.AddRange(b.AsSpan(p, n).ToArray());
                p += n;
            }
            p++;
            var indices = Lzw(data.ToArray(), minCode, w * h);
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var idx = indices[y * w + x];
                    if (idx == transparent) continue;
                    canvas[(y0 + y) * Width + x0 + x] = palette[idx];
                }
            Frames.Add(((uint[])canvas.Clone(), delay));
            Rects.Add((x0, y0, w, h));
            transparent = -1;
        }
    }

    public static byte[] Lzw(byte[] data, int minCode, int expected)
    {
        var clear = 1 << minCode;
        var end = clear + 1;
        var prefix = new short[4096];
        var suffix = new byte[4096];
        var first = new byte[4096];
        var length = new int[4096];
        for (var i = 0; i < clear; i++) { prefix[i] = -1; suffix[i] = (byte)i; first[i] = (byte)i; length[i] = 1; }

        var output = new List<byte>(expected);
        int size = minCode + 1, mask = (1 << size) - 1, avail = clear + 2, old = -1;
        int bits = 0, acc = 0, pos = 0;
        var stack = new byte[4096];
        while (true)
        {
            while (bits < size)
            {
                if (pos >= data.Length) throw new InvalidDataException("LZW data ran out before the end code");
                acc |= data[pos++] << bits;
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
            if (code == end) break;
            if (code > avail) throw new InvalidDataException($"Illegal code {code} (avail {avail})");

            if (old >= 0)
            {
                if (avail >= 4096) throw new InvalidDataException("Table overflow without clear code");
                prefix[avail] = (short)old;
                first[avail] = first[old];
                suffix[avail] = code == avail ? first[old] : first[code];
                length[avail] = length[old] + 1;
                avail++;
            }
            else if (code == avail)
            {
                throw new InvalidDataException("First code after clear must be a literal");
            }

            var n = 0;
            for (var c = code; c >= 0; c = prefix[c]) stack[n++] = suffix[c];
            for (var i = n - 1; i >= 0; i--) output.Add(stack[i]);
            old = code;

            if ((avail & mask) == 0 && avail <= 0x0FFF)
            {
                size++;
                mask = (1 << size) - 1;
            }
        }
        return output.ToArray();
    }
}
