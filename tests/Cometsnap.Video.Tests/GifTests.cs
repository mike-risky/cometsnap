using Cometsnap.Video.Encoding;

namespace Cometsnap.Video.Tests;

public class GifTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(300)]
    [InlineData(70_000)]   // goes through several clear codes
    [InlineData(250_000)]
    public void Lzw_round_trips_random_and_repetitive_data(int length)
    {
        var rng = new Random(length);
        var data = new byte[length];
        for (var i = 0; i < length; i++) data[i] = (byte)(i % 7 == 0 ? rng.Next(256) : rng.Next(4));
        var ms = new MemoryStream();
        new LzwEncoder().Encode(data, 8, ms);
        var bytes = ms.ToArray();

        // Strip min code size + sub-block framing.
        Assert.Equal(8, bytes[0]);
        var payload = new List<byte>();
        var p = 1;
        while (bytes[p] != 0) { payload.AddRange(bytes.AsSpan(p + 1, bytes[p]).ToArray()); p += bytes[p] + 1; }
        Assert.Equal(bytes.Length - 1, p);

        Assert.Equal(data, GifDecoder.Lzw(payload.ToArray(), 8, length));
    }

    [Fact]
    public void Lzw_round_trips_a_single_repeated_value()
    {
        var data = Enumerable.Repeat((byte)42, 100_000).ToArray();
        var ms = new MemoryStream();
        new LzwEncoder().Encode(data, 8, ms);
        var bytes = ms.ToArray();
        var payload = new List<byte>();
        var p = 1;
        while (bytes[p] != 0) { payload.AddRange(bytes.AsSpan(p + 1, bytes[p]).ToArray()); p += bytes[p] + 1; }
        Assert.Equal(data, GifDecoder.Lzw(payload.ToArray(), 8, data.Length));
    }

    private static byte[] Frame(int w, int h, Func<int, int, uint> color)
    {
        var bytes = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                BitConverter.TryWriteBytes(bytes.AsSpan((y * w + x) * 4), color(x, y) | 0xFF000000);
        return bytes;
    }

    [Fact]
    public void Few_colour_frames_are_exact_and_unchanged_frames_are_merged()
    {
        const int w = 40, h = 30;
        var ms = new MemoryStream();
        var gif = new GifWriter(ms, w, h);
        var a = Frame(w, h, (x, y) => x < 20 ? 0x3D6BFFu : 0x07113Au);
        var b = Frame(w, h, (x, y) => x < 20 ? 0x3D6BFFu : (x == 30 && y == 10 ? 0xE5484Du : 0x07113Au));
        gif.AddFrame(a, TimeSpan.Zero);
        gif.AddFrame(a, TimeSpan.FromMilliseconds(100)); // identical: merged into the first
        gif.AddFrame(b, TimeSpan.FromMilliseconds(500));
        gif.Finish(TimeSpan.FromMilliseconds(1000));
        var bytes = ms.ToArray();

        var decoded = GifDecoder.Decode(bytes);
        Assert.True(decoded.Loops);
        Assert.Equal(2, decoded.Frames.Count);
        Assert.Equal(50, decoded.Frames[0].Delay);
        Assert.Equal(50, decoded.Frames[1].Delay);
        Assert.Equal(0x3D6BFFu, decoded.Frames[0].Pixels[5]);
        Assert.Equal(0x07113Au, decoded.Frames[0].Pixels[25]);
        Assert.Equal(0xE5484Du, decoded.Frames[1].Pixels[10 * w + 30]);
        Assert.Equal(0x07113Au, decoded.Frames[1].Pixels[10 * w + 31]);
        Assert.Equal(0x3D6BFFu, decoded.Frames[1].Pixels[0]);
    }

    [Fact]
    public void Many_colour_frames_are_quantized_close_to_the_source()
    {
        const int w = 128, h = 64;
        var ms = new MemoryStream();
        var gif = new GifWriter(ms, w, h);
        uint Gradient(int x, int y) => (uint)((x * 2) << 16 | (y * 4) << 8 | ((x + y) & 0xFF));
        gif.AddFrame(Frame(w, h, Gradient), TimeSpan.Zero);
        gif.Finish(TimeSpan.FromSeconds(1));

        var frame = GifDecoder.Decode(ms.ToArray()).Frames.Single().Pixels;
        double error = 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var src = Gradient(x, y);
                var got = frame[y * w + x];
                for (var s = 0; s < 24; s += 8)
                    error += Math.Abs((int)((src >> s) & 0xFF) - (int)((got >> s) & 0xFF));
            }
        var meanPerChannel = error / (w * h * 3);
        Assert.True(meanPerChannel < 10, $"mean error {meanPerChannel:F1}");
    }

    [Fact]
    public void Changed_area_is_cropped_to_its_bounding_box()
    {
        const int w = 200, h = 200;
        var ms = new MemoryStream();
        var gif = new GifWriter(ms, w, h);
        gif.AddFrame(Frame(w, h, (_, _) => 0x101010), TimeSpan.Zero);
        gif.AddFrame(Frame(w, h, (x, y) => x is >= 50 and < 60 && y is >= 70 and < 75 ? 0xFFFFFFu : 0x101010u), TimeSpan.FromSeconds(1));
        gif.Finish(TimeSpan.FromSeconds(2));
        var decoded = GifDecoder.Decode(ms.ToArray());
        Assert.Equal(2, decoded.Frames.Count);
        Assert.Equal(0xFFFFFFu, decoded.Frames[1].Pixels[72 * w + 55]);
        Assert.Equal(0x101010u, decoded.Frames[1].Pixels[72 * w + 60]);
        Assert.Equal((0, 0, w, h), decoded.Rects[0]);
        Assert.Equal((50, 70, 10, 5), decoded.Rects[1]);
    }
}
