using Cometsnap.Linux.Core;
using Cometsnap.Linux.Platform;

namespace Cometsnap.Linux.Tests;

public class SnapNamingTests
{
    private static readonly DateTime When = new(2026, 11, 26, 8, 56, 12);

    [Fact]
    public void Uses_month_folder_and_timestamp_name()
    {
        var path = SnapNaming.NextFreePath("/pics", When, _ => false);
        Assert.Equal(Path.Combine("/pics", "2026-11", "snap_2026-11-26_08-56-12.png"), path);
    }

    [Fact]
    public void Adds_suffix_when_same_second_is_taken()
    {
        var taken = new HashSet<string> { Path.Combine("r", "2026-11", "snap_2026-11-26_08-56-12.png") };
        Assert.EndsWith("snap_2026-11-26_08-56-12_2.png", SnapNaming.NextFreePath("r", When, taken.Contains));
    }
}

public class HotkeyTests
{
    [Theory]
    [InlineData("PrintScreen", "Print", "Print", "Print Screen")]
    [InlineData("Ctrl+Shift+S", "<Control><Shift>s", "CTRL+SHIFT+s", "Ctrl+Shift+S")]
    [InlineData("Shift+PrintScreen", "<Shift>Print", "SHIFT+Print", "Shift+Print Screen")]
    [InlineData("Super+F5", "<Super>F5", "LOGO+F5", "Super+F5")]
    public void Converts_to_desktop_formats(string text, string gtk, string portal, string label)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var g));
        Assert.Equal(gtk, g.ToGtkAccelerator());
        Assert.Equal(portal, g.ToPortalTrigger());
        Assert.Equal(label, g.ToString());
    }

    [Fact]
    public void Print_screen_keysym()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+Print", out var g));
        Assert.Equal(0xff61u, g.KeySym);
        Assert.Equal(HotkeyModifiers.Ctrl, g.Modifiers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+Banana")]
    public void Rejects_bad_text(string text) => Assert.False(HotkeyGesture.TryParse(text, out _));
}

public class GVariantListTests
{
    [Fact]
    public void Parses_lists()
    {
        Assert.Empty(DesktopIntegration.ParseStringList("@as []"));
        Assert.Equal(new[] { "Print", "<Shift>Print" }, DesktopIntegration.ParseStringList("['Print', '<Shift>Print']"));
        Assert.Equal(new[] { "it's" }, DesktopIntegration.ParseStringList("[\"it's\"]"));
    }

    [Fact]
    public void Round_trips()
    {
        var items = new[] { "/org/gnome/a/", "quote'd", "back\\slash" };
        Assert.Equal(items, DesktopIntegration.ParseStringList(DesktopIntegration.FormatStringList(items)));
        Assert.Equal("@as []", DesktopIntegration.FormatStringList(Array.Empty<string>()));
    }
}

public class GeometryTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void Loupe_leaves_corner_when_cursor_comes_close()
    {
        var corner = LoupePlacement.ResolveCorner(LoupeCorner.BottomLeft, LoupeCorner.BottomLeft, Monitor, 200, 200, 60, 1020);
        Assert.NotEqual(LoupeCorner.BottomLeft, corner);
        var far = LoupePlacement.ResolveCorner(LoupeCorner.BottomLeft, LoupeCorner.BottomLeft, Monitor, 200, 200, 960, 300);
        Assert.Equal(LoupeCorner.BottomLeft, far);
    }

    [Fact]
    public void Edges_snap_within_threshold()
    {
        var snapper = new EdgeSnapper(new[] { new PixelRect(100, 100, 200, 100) }, 8);
        Assert.Equal(100, snapper.SnapX(105));
        Assert.Equal(299, snapper.SnapX(295));
        Assert.Equal(150, snapper.SnapX(150));
    }
}
