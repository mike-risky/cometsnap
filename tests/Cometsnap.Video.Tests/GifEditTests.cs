using Cometsnap.Video.Editing;
using Cometsnap.Video.Encoding;

namespace Cometsnap.Video.Tests;

public class GifEditTests
{
    private const int W = 40, H = 30;

    /// <summary>Frame i is a flat colour with a small block at x = i, so every frame is different.</summary>
    private static uint Color(int i) => (uint)(0x102030 + i * 0x0A0B0C) & 0xFFFFFF;

    private static byte[] Frame(int i)
    {
        var bytes = new byte[W * H * 4];
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var c = x >= i && x < i + 3 && y < 5 ? 0xFFFFFFu : Color(i);
                BitConverter.TryWriteBytes(bytes.AsSpan((y * W + x) * 4), c | 0xFF000000);
            }
        return bytes;
    }

    /// <summary>A 2-second GIF: 20 different frames of 0.1 s.</summary>
    private static string MakeGif()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cometsnap-test-{Guid.NewGuid():N}.gif");
        using var writer = new GifWriter(File.Create(path), W, H);
        for (var i = 0; i < 20; i++) writer.AddFrame(Frame(i), TimeSpan.FromMilliseconds(i * 100));
        writer.Finish(TimeSpan.FromSeconds(2));
        return path;
    }

    private static uint Pixel(uint[] canvas, int x, int y) => canvas[y * W + x] & 0xFFFFFF;

    [Fact]
    public void Reader_sees_every_frame_with_its_timing()
    {
        var path = MakeGif();
        try
        {
            var r = GifReader.Open(path);
            Assert.Equal((W, H), (r.Width, r.Height));
            Assert.Equal(20, r.FrameCount);
            Assert.Equal(TimeSpan.FromSeconds(2), r.Duration);
            Assert.Equal(TimeSpan.FromMilliseconds(700), r.FrameStart(7));
            Assert.Equal(7, r.FrameAt(TimeSpan.FromMilliseconds(750)));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Reader_composites_frames_in_any_order()
    {
        var path = MakeGif();
        try
        {
            var r = GifReader.Open(path);
            foreach (var i in new[] { 0, 5, 19, 3, 12, 12, 0, 18 })
            {
                var c = r.Render(i);
                Assert.Equal(Color(i), Pixel(c, W - 1, H - 1));
                Assert.Equal(0xFFFFFFu, Pixel(c, i + 1, 2));
                if (i > 0) Assert.Equal(Color(i), Pixel(c, i - 1, 2));
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Trimmed_gif_keeps_only_the_kept_frames()
    {
        var source = MakeGif();
        var output = Path.ChangeExtension(source, ".out.gif");
        try
        {
            var cuts = new CutList(TimeSpan.FromSeconds(2)) { TrimStart = TimeSpan.FromMilliseconds(200), TrimEnd = TimeSpan.FromMilliseconds(1800) };
            Assert.True(cuts.AddCut(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1000)));
            var result = await RecordingTrimmer.ExportAsync(source, output, cuts);

            Assert.Equal(TimeSpan.FromMilliseconds(1100), result.Duration);
            Assert.Equal(RecordingFormat.Gif, result.Format);
            var r = GifReader.Open(output);
            Assert.Equal(TimeSpan.FromMilliseconds(1100), r.Duration);
            // Kept: frames 2, 3, 4, then 10 … 17.
            var expected = new[] { 2, 3, 4, 10, 11, 12, 13, 14, 15, 16, 17 };
            Assert.Equal(expected.Length, r.FrameCount);
            for (var i = 0; i < expected.Length; i++)
                Assert.Equal(Color(expected[i]), Pixel(r.Render(i), W - 1, H - 1));
        }
        finally
        {
            File.Delete(source);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public void Reader_rejects_files_that_are_not_gifs() =>
        Assert.Throws<InvalidDataException>(() => new GifReader(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 }));
}
