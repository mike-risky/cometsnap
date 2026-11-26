using Cometsnap.Core.Geometry;
using Cometsnap.Core.Settings;

namespace Cometsnap.Core.Tests;

public class PixelRectTests
{
    [Fact]
    public void FromPixels_includes_both_pixels_in_any_direction()
    {
        Assert.Equal(new PixelRect(10, 10, 10, 10), PixelRect.FromPixels(10, 10, 19, 19));
        Assert.Equal(new PixelRect(10, 10, 10, 10), PixelRect.FromPixels(19, 19, 10, 10));
        Assert.Equal(new PixelRect(5, 5, 1, 1), PixelRect.FromPixels(5, 5, 5, 5));
    }

    [Fact]
    public void Intersect_and_keep_inside()
    {
        var screen = new PixelRect(0, 0, 100, 100);
        Assert.Equal(new PixelRect(90, 90, 10, 10), new PixelRect(90, 90, 50, 50).Intersect(screen));
        Assert.True(new PixelRect(200, 200, 5, 5).Intersect(screen).IsEmpty);
        Assert.Equal(new PixelRect(80, 0, 20, 20), new PixelRect(95, -5, 20, 20).KeepInside(screen));
    }
}

public class EdgeSnapperTests
{
    private readonly EdgeSnapper _snapper = new(new[] { new PixelRect(100, 200, 300, 400) }, threshold: 8);

    [Theory]
    [InlineData(95, 100)]   // near left edge
    [InlineData(105, 100)]
    [InlineData(395, 399)]  // near right edge pixel (100 + 300 - 1)
    [InlineData(250, 250)]  // far from any edge
    [InlineData(92, 100)]   // exactly at the threshold
    [InlineData(90, 90)]    // just outside the threshold
    public void Snaps_x_to_nearest_edge_within_threshold(int input, int expected) =>
        Assert.Equal(expected, _snapper.SnapX(input));

    [Fact]
    public void Snaps_y_to_bottom_edge_pixel() => Assert.Equal(599, _snapper.SnapY(603));
}

public class LoupePlacementTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);
    private const int W = 200, H = 260;

    [Fact]
    public void Stays_bottom_left_while_the_cursor_is_elsewhere()
    {
        var c = LoupePlacement.ResolveCorner(LoupeCorner.BottomLeft, LoupeCorner.BottomLeft, Monitor, W, H, 960, 540);
        Assert.Equal(LoupeCorner.BottomLeft, c);
        var r = LoupePlacement.RectFor(c, Monitor, W, H);
        Assert.Equal(LoupePlacement.Margin, r.X);
        Assert.Equal(1080 - LoupePlacement.Margin - H, r.Y);
    }

    [Fact]
    public void Jumps_away_when_the_cursor_comes_close()
    {
        var c = LoupePlacement.ResolveCorner(LoupeCorner.BottomLeft, LoupeCorner.BottomLeft, Monitor, W, H, 60, 1000);
        Assert.Equal(LoupeCorner.BottomRight, c);
        Assert.False(LoupePlacement.RectFor(c, Monitor, W, H).Inflate(LoupePlacement.AvoidDistance).Contains(60, 1000));
    }

    [Fact]
    public void Does_not_flicker_back_at_the_zone_boundary_but_returns_when_far()
    {
        // Just outside the avoid zone of the preferred corner, but within the hysteresis band.
        var zoneTop = 1080 - LoupePlacement.Margin - H - LoupePlacement.AvoidDistance;
        var nearY = zoneTop - 10;
        var c = LoupePlacement.ResolveCorner(LoupeCorner.BottomLeft, LoupeCorner.BottomRight, Monitor, W, H, 60, nearY);
        Assert.Equal(LoupeCorner.BottomRight, c);

        c = LoupePlacement.ResolveCorner(LoupeCorner.BottomLeft, LoupeCorner.BottomRight, Monitor, W, H, 960, 300);
        Assert.Equal(LoupeCorner.BottomLeft, c);
    }

    [Fact]
    public void Works_on_a_secondary_monitor_with_negative_coordinates()
    {
        var left = new PixelRect(-1280, 0, 1280, 1024);
        var c = LoupePlacement.ResolveCorner(LoupeCorner.BottomLeft, LoupeCorner.BottomLeft, left, W, H, -1250, 1000);
        Assert.Equal(LoupeCorner.BottomRight, c);
        Assert.True(left.Contains(LoupePlacement.RectFor(c, left, W, H)));
    }

    [Fact]
    public void Follow_mode_flips_near_monitor_edges()
    {
        var r = LoupePlacement.FollowRect(Monitor, W, H, 1900, 1070);
        Assert.True(r.Right <= 1900 && r.Bottom <= 1070);
        Assert.True(Monitor.Contains(r));
    }
}
