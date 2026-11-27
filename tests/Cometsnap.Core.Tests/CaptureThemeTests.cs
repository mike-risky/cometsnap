using Cometsnap.Core.Settings;

namespace Cometsnap.Core.Tests;

public class CaptureThemeTests
{
    [Fact]
    public void Default_is_the_classic_blue_look()
    {
        Assert.Equal("blue", new AppSettings().CaptureTheme);
        var blue = CaptureThemes.Get(null);
        Assert.Equal(0x3D6BFFu, blue.Accent);
        Assert.Equal(0xFFD60Au, blue.Tag);
    }

    [Theory]
    [InlineData("yellow", "yellow")]
    [InlineData("TERRACOTTA", "terracotta")]
    [InlineData("", "blue")]
    [InlineData("purple", "blue")]
    public void Unknown_ids_fall_back_to_blue(string id, string expected) =>
        Assert.Equal(expected, CaptureThemes.Get(id).Id);

    [Fact]
    public void Theme_ids_are_unique() =>
        Assert.Equal(CaptureThemes.All.Count, CaptureThemes.All.Select(t => t.Id).Distinct().Count());

    [Fact]
    public void Color_text_forms()
    {
        Assert.Equal("#FFAA00", ColorText.Hex(255, 170, 0));
        Assert.Equal("rgb(255, 170, 0)", ColorText.Rgb(255, 170, 0));
        Assert.Equal("#07113A", CaptureThemes.Hex(0x07113A));
    }

    [Fact]
    public void Color_hotkey_has_a_default() => Assert.Equal("Win+Shift+C", new AppSettings().ColorHotkey);
}
