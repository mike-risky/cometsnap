using Cometsnap.Core.Capture;
using Cometsnap.Core.Text;

namespace Cometsnap.Core.Tests;

public class ScrollStitcherTests
{
    private const int W = 240, H = 320, Header = 30, Footer = 16, Scrollbar = 14;

    /// <summary>A long page of "text" rows with blank gaps between paragraphs.</summary>
    private static byte[] Page(int height, int seed = 7)
    {
        var rnd = new Random(seed);
        var page = new byte[W * 4 * height];
        for (var y = 0; y < height; y++)
        {
            var blank = y / 10 % 3 == 2;
            for (var x = 0; x < W; x++)
            {
                var v = blank ? (byte)255 : (byte)rnd.Next(256);
                var i = (y * W + x) * 4;
                page[i] = page[i + 1] = page[i + 2] = v;
                page[i + 3] = 255;
            }
        }
        return page;
    }

    /// <summary>What the window shows when scrolled down by <paramref name="scroll"/>: sticky header and footer, moving scrollbar thumb.</summary>
    private static byte[] Frame(byte[] page, int pageHeight, int scroll)
    {
        var f = new byte[W * 4 * H];
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            byte v;
            if (x >= W - Scrollbar) v = y >= scroll * H / pageHeight && y < scroll * H / pageHeight + 20 ? (byte)40 : (byte)210;
            else if (y < Header) v = (byte)((x * 7 + y) % 256);
            else if (y >= H - Footer) v = (byte)((x * 3 + y * 5) % 256);
            else v = page[((scroll + y - Header) * W + x) * 4];
            var i = (y * W + x) * 4;
            f[i] = f[i + 1] = f[i + 2] = v;
            f[i + 3] = 255;
        }
        return f;
    }

    [Fact]
    public void Glues_a_long_page_with_sticky_header_and_footer()
    {
        const int pageHeight = 1500;
        var page = Page(pageHeight);
        var maxScroll = pageHeight - (H - Header - Footer);
        var stitcher = new ScrollStitcher(W, H);
        Assert.Equal(StitchStep.First, stitcher.Add(Frame(page, pageHeight, 0)));

        var steps = new[] { 90, 140, 37, 200, 120 };
        var at = 0;
        for (var k = 0; at < maxScroll; k++)
        {
            var next = Math.Min(maxScroll, at + steps[k % steps.Length]);
            Assert.Equal(StitchStep.Appended, stitcher.Add(Frame(page, pageHeight, next)));
            Assert.Equal(next - at, stitcher.LastShift);
            at = next;
        }
        Assert.Equal(StitchStep.Unchanged, stitcher.Add(Frame(page, pageHeight, maxScroll)));
        Assert.Equal(Header, stitcher.Header);
        Assert.Equal(Footer, stitcher.Footer);

        var image = stitcher.Compose(out var height);
        Assert.Equal(Header + pageHeight + Footer, height);
        for (var y = 0; y < pageHeight; y++)
        for (var x = 0; x < W - Scrollbar; x += 3)
            Assert.Equal(page[(y * W + x) * 4], image[((Header + y) * W + x) * 4]);
    }

    [Fact]
    public void A_frame_from_somewhere_else_is_lost()
    {
        var page = Page(1200);
        var other = Page(1200, seed: 99);
        var stitcher = new ScrollStitcher(W, H);
        stitcher.Add(Frame(page, 1200, 0));
        Assert.Equal(StitchStep.Lost, stitcher.Add(Frame(other, 1200, 100)));
        stitcher.Compose(out var height);
        Assert.Equal(H, height);
    }

    [Fact]
    public void Scrolling_back_up_by_hand_is_picked_up_again_further_down()
    {
        // Held-button scrolling: the user scrolls in small uneven steps, goes back up, then on.
        const int pageHeight = 1400;
        var page = Page(pageHeight);
        var stitcher = new ScrollStitcher(W, H);
        stitcher.Add(Frame(page, pageHeight, 0));
        foreach (var at in new[] { 12, 40, 41, 95, 160 })
            Assert.Equal(StitchStep.Appended, stitcher.Add(Frame(page, pageHeight, at)));
        Assert.Equal(StitchStep.Lost, stitcher.Add(Frame(page, pageHeight, 100))); // back up
        Assert.Equal(StitchStep.Unchanged, stitcher.Add(Frame(page, pageHeight, 160)));
        Assert.Equal(StitchStep.Appended, stitcher.Add(Frame(page, pageHeight, 300)));

        var image = stitcher.Compose(out var height);
        Assert.Equal(H + 300, height);
        for (var y = 0; y < 300 + H - Header - Footer; y++)
        for (var x = 0; x < W - Scrollbar; x += 5)
            Assert.Equal(page[(y * W + x) * 4], image[((Header + y) * W + x) * 4]);
    }

    [Fact]
    public void Same_frame_is_unchanged()
    {
        var page = Page(800);
        var stitcher = new ScrollStitcher(W, H);
        stitcher.Add(Frame(page, 800, 50));
        Assert.Equal(StitchStep.Unchanged, stitcher.Add(Frame(page, 800, 50)));
    }
}

public class OcrTextTests
{
    [Fact]
    public void Joins_lines_and_keeps_paragraphs()
    {
        var text = OcrText.Join(new[]
        {
            new RecognizedLine("Hello world", 0, 0, 10),
            new RecognizedLine("second line", 0, 13, 10),
            new RecognizedLine("New paragraph", 0, 40, 10),
        });
        Assert.Equal("Hello world\nsecond line\n\nNew paragraph", text);
    }

    [Theory]
    [InlineData("你 好 世 界", "你好世界")]
    [InlineData("截 图 Cometsnap 工 具", "截图 Cometsnap 工具")]
    [InlineData("  plain text  ", "plain text")]
    public void Removes_spaces_between_chinese_characters(string line, string expected) =>
        Assert.Equal(expected, OcrText.TidyLine(line));

    [Fact]
    public void Bands_cover_every_row_and_cut_on_empty_rows()
    {
        var empty = Enumerable.Range(0, 1000).Select(i => i % 37 == 0).ToArray();
        var bands = OcrText.Bands(empty, 300);
        Assert.Equal(0, bands[0].Start);
        for (var i = 1; i < bands.Count; i++)
            Assert.Equal(bands[i - 1].Start + bands[i - 1].Length, bands[i].Start);
        Assert.Equal(1000, bands[^1].Start + bands[^1].Length);
        Assert.All(bands, b => Assert.InRange(b.Length, 1, 300));
        Assert.All(bands.Take(bands.Count - 1), b => Assert.True(empty[b.Start + b.Length - 1]));
    }
}
