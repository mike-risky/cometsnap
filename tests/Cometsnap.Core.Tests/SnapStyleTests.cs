using Cometsnap.Core.Settings;
using Cometsnap.Core.Styling;

namespace Cometsnap.Core.Tests;

public class SnapStyleTests
{
    private static byte[] Solid(int w, int h, byte b, byte g, byte r)
    {
        var p = new byte[w * h * 4];
        for (var i = 0; i < p.Length; i += 4) { p[i] = b; p[i + 1] = g; p[i + 2] = r; p[i + 3] = 0; } // BGRX: junk alpha
        return p;
    }

    [Fact]
    public void Unknown_ids_fall_back_to_clean()
    {
        Assert.Equal("clean", SnapStyles.Get(null).Id);
        Assert.Equal("clean", SnapStyles.Get("nope").Id);
        Assert.Equal("comet", SnapStyles.Get("COMET").Id);
        Assert.Equal(SnapStyles.All.Count, SnapStyles.All.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void Defaults_keep_captures_raw_but_offer_the_button()
    {
        var s = new AppSettings();
        Assert.False(s.StyleOnCopy);
        Assert.False(s.StyleOnExport);
        Assert.True(s.StylePerShot);
        Assert.Equal(SnapStyles.DefaultId, s.SnapStyle);
    }

    [Theory]
    [InlineData("clean")]
    [InlineData("showcase")]
    [InlineData("frosted")]
    [InlineData("comet")]
    [InlineData("laser")]
    public void Every_style_keeps_the_capture_intact_in_the_middle(string id)
    {
        const int w = 300, h = 200;
        var style = SnapStyles.Get(id);
        var output = StyleRenderer.Render(Solid(w, h, 10, 20, 30), w, h, w * 4, style, 0x3D6BFF);
        var m = StyleRenderer.Measure(w, h, style);
        Assert.Equal(m.Width, output.Width);
        Assert.Equal(m.Height, output.Height);
        Assert.True(output.Width > w && output.Height > h);

        var center = ((m.ImageY + h / 2) * output.Width + m.ImageX + w / 2) * 4;
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, output.Bgra[center..(center + 4)]);
        // A pixel just inside a straight edge is the capture too.
        var edge = ((m.ImageY + h / 2) * output.Width + m.ImageX + 3) * 4;
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, output.Bgra[edge..(edge + 4)]);
    }

    [Fact]
    public void Corners_are_rounded_and_transparent_without_a_backdrop()
    {
        const int w = 400, h = 300;
        var style = SnapStyles.Get("clean");
        var output = StyleRenderer.Render(Solid(w, h, 200, 200, 200), w, h, w * 4, style, 0x3D6BFF);
        var m = StyleRenderer.Measure(w, h, style);
        // The very corner pixel of the capture is cut by the rounding.
        var corner = (m.ImageY * output.Width + m.ImageX) * 4;
        Assert.NotEqual(200, output.Bgra[corner]);
        // The canvas corner is (almost) transparent.
        Assert.True(output.Bgra[3] < 10);
        Assert.True(StyleRenderer.HasTransparency(output));
        Assert.False(StyleRenderer.HasTransparency(StyleRenderer.Flatten(output, 0xFFFFFF)));
    }

    [Fact]
    public void Backdrops_are_opaque()
    {
        const int w = 120, h = 80;
        foreach (var id in new[] { "showcase", "frosted", "comet" })
        {
            var output = StyleRenderer.Render(Solid(w, h, 1, 2, 3), w, h, w * 4, SnapStyles.Get(id), 0xE2553A);
            Assert.False(StyleRenderer.HasTransparency(output), id);
        }
    }

    [Fact]
    public void Tiny_and_tall_captures_work()
    {
        var tiny = StyleRenderer.Render(Solid(3, 2, 5, 5, 5), 3, 2, 12, SnapStyles.Get("comet"), 0x3D6BFF);
        Assert.True(tiny.Width > 3);
        var tall = StyleRenderer.Measure(800, 20000, SnapStyles.Get("showcase"));
        Assert.True(tall.ImageX <= 120); // the frame does not explode on scrolling captures
    }

    [Fact]
    public void Styled_copy_sits_beside_the_original()
    {
        var path = Path.Combine("C:", "Snaps", "2026-11", "snap_1.png");
        Assert.Equal(Path.Combine("C:", "Snaps", "2026-11", "snap_1-styled.png"), SnapStyles.StyledPath(path));
    }
}
