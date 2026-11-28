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

    private static byte[] Px(StyledPixels o, int x, int y) => o.Bgra[((y * o.Width + x) * 4)..((y * o.Width + x) * 4 + 4)];

    [Fact]
    public void Defaults_keep_captures_raw_but_offer_the_button()
    {
        var s = new AppSettings();
        Assert.False(s.StyleOnCopy);
        Assert.False(s.StyleOnExport);
        Assert.True(s.StylePerShot);
        Assert.NotNull(s.SnapStyle);
    }

    [Fact]
    public void Every_preset_keeps_the_capture_intact_in_the_middle()
    {
        const int w = 300, h = 200;
        foreach (var (id, _) in SnapStylePresets.All)
        {
            var style = SnapStylePresets.Get(id);
            var output = StyleRenderer.Render(Solid(w, h, 10, 20, 30), w, h, w * 4, style);
            var m = StyleRenderer.Measure(w, h, style);
            Assert.Equal(m.Width, output.Width);
            Assert.True(output.Width > w && output.Height > h, id);
            Assert.Equal(new byte[] { 10, 20, 30, 255 }, Px(output, m.ImageX + w / 2, m.ImageY + h / 2));
            Assert.Equal(new byte[] { 10, 20, 30, 255 }, Px(output, m.ImageX + 3, m.ImageY + h / 2));
        }
    }

    [Fact]
    public void Shadow_offset_makes_room_on_its_side_only()
    {
        var style = new SnapStyle { ShadowX = 20, ShadowY = 30, ShadowBlur = 0, ShadowOpacity = 100, CornerRadius = 0 };
        var m = StyleRenderer.Measure(100, 100, style);
        Assert.Equal(0, m.ImageX);
        Assert.Equal(0, m.ImageY);
        Assert.True(m.Width >= 120 && m.Height >= 130);
        var o = StyleRenderer.Render(Solid(100, 100, 200, 200, 200), 100, 100, 400, style);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, Px(o, 110, 120)); // hard black shadow bottom right
        Assert.Equal(0, Px(o, 5, 115)[3]);                         // nothing below-left of it
    }

    [Fact]
    public void Shadow_border_and_glow_use_their_colors()
    {
        var style = new SnapStyle
        {
            CornerRadius = 0, Shadow = true, ShadowX = 0, ShadowY = 0, ShadowBlur = 0, ShadowOpacity = 100, ShadowColor = "#FF0000",
            Border = true, BorderWidth = 4, BorderColor = "#00FF00",
        };
        var o = StyleRenderer.Render(Solid(50, 50, 9, 9, 9), 50, 50, 200, style);
        var m = StyleRenderer.Measure(50, 50, style);
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, Px(o, m.ImageX - 2, m.ImageY + 25)); // green border
        Assert.Equal(new byte[] { 9, 9, 9, 255 }, Px(o, m.ImageX, m.ImageY));           // square corner kept

        var glow = new SnapStyle { Shadow = false, Glow = true, GlowColor = "#FF2BD6", GlowSize = 20, GlowStrength = 100 };
        var g = StyleRenderer.Render(Solid(80, 60, 0, 0, 0), 80, 60, 320, glow);
        var gm = StyleRenderer.Measure(80, 60, glow);
        var p = Px(g, gm.ImageX - 1, gm.ImageY + 30);
        Assert.True(p[2] > 200 && p[0] > 150 && p[3] > 200); // pink line right outside the edge
    }

    [Fact]
    public void Corners_are_rounded_and_transparent_without_a_backdrop()
    {
        const int w = 400, h = 300;
        var style = SnapStylePresets.Get("clean");
        var output = StyleRenderer.Render(Solid(w, h, 200, 200, 200), w, h, w * 4, style);
        var m = StyleRenderer.Measure(w, h, style);
        Assert.NotEqual(200, Px(output, m.ImageX, m.ImageY)[0]);
        Assert.True(output.Bgra[3] < 10);
        Assert.True(StyleRenderer.HasTransparency(output));
        Assert.False(StyleRenderer.HasTransparency(StyleRenderer.Flatten(output, 0xFFFFFF)));
    }

    [Fact]
    public void Backdrops_are_opaque()
    {
        foreach (var b in new[] { StyleBackdrop.Color, StyleBackdrop.Gradient, StyleBackdrop.Blur, StyleBackdrop.Space })
        {
            var output = StyleRenderer.Render(Solid(120, 80, 1, 2, 3), 120, 80, 480, new SnapStyle { Backdrop = b });
            Assert.False(StyleRenderer.HasTransparency(output), b.ToString());
        }
    }

    [Fact]
    public void Scale_doubles_every_size()
    {
        var style = new SnapStyle { Backdrop = StyleBackdrop.Color, Padding = 40 };
        Assert.Equal(40, StyleRenderer.Measure(100, 100, style).ImageX);
        Assert.Equal(80, StyleRenderer.Measure(100, 100, style, 2).ImageX);
    }

    [Fact]
    public void Odd_values_are_tamed()
    {
        var s = new SnapStyle { CornerRadius = -5, ShadowOpacity = 400, GlowColor = "nope", BorderColor = "f00" }.Normalized();
        Assert.Equal(0, s.CornerRadius);
        Assert.Equal(100, s.ShadowOpacity);
        Assert.Equal("#3D6BFF", s.GlowColor);
        Assert.Equal("#FF0000", s.BorderColor);
        var tiny = StyleRenderer.Render(Solid(3, 2, 5, 5, 5), 3, 2, 12, SnapStylePresets.Get("neon"));
        Assert.True(tiny.Width > 3);
    }

    [Fact]
    public void Styled_copy_sits_beside_the_original()
    {
        var path = Path.Combine("C:", "Snaps", "2026-11", "snap_1.png");
        Assert.Equal(Path.Combine("C:", "Snaps", "2026-11", "snap_1-styled.png"), StyledFiles.StyledPath(path));
    }

    [Fact]
    public void Settings_file_keeps_the_custom_style_and_reads_old_preset_ids()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cometsnap-style-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        try
        {
            var s = new AppSettings();
            s.SnapStyle.ShadowX = -7;
            s.SnapStyle.Glow = true;
            s.SnapStyle.GlowColor = "#FF2BD6";
            s.SnapStyle.Backdrop = StyleBackdrop.Space;
            s.Save(path);
            var back = AppSettings.Load(path);
            Assert.Equal(-7, back.SnapStyle.ShadowX);
            Assert.True(back.SnapStyle.Glow);
            Assert.Equal("#FF2BD6", back.SnapStyle.GlowColor);
            Assert.Equal(StyleBackdrop.Space, back.SnapStyle.Backdrop);

            // Early test builds saved a preset id: it loads as that preset, the other settings survive.
            File.WriteAllText(path, "{ \"SnapStyle\": \"neon\", \"StyleOnCopy\": true }");
            var old = AppSettings.Load(path);
            Assert.True(old.StyleOnCopy);
            Assert.True(old.SnapStyle.Glow);
            Assert.Equal("#FF2BD6", old.SnapStyle.GlowColor);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
