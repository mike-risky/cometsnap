using Cometsnap.Core.Storage;

namespace Cometsnap.Core.Tests;

public class SnapNamingTests
{
    private static readonly DateTime When = new(2026, 11, 26, 8, 56, 12);

    [Fact]
    public void Uses_month_folder_and_timestamp_name()
    {
        var path = SnapNaming.NextFreePath(@"C:\Pics", When, _ => false);
        Assert.Equal(Path.Combine(@"C:\Pics", "2026-11", "snap_2026-11-26_08-56-12.png"), path);
    }

    [Fact]
    public void Adds_suffix_when_same_second_is_taken()
    {
        var taken = new HashSet<string>
        {
            Path.Combine("r", "2026-11", "snap_2026-11-26_08-56-12.png"),
            Path.Combine("r", "2026-11", "snap_2026-11-26_08-56-12_2.png"),
        };
        var path = SnapNaming.NextFreePath("r", When, taken.Contains);
        Assert.EndsWith("snap_2026-11-26_08-56-12_3.png", path);
    }

    [Fact]
    public void Sidecar_sits_next_to_image()
    {
        Assert.Equal(Path.Combine("a", "snap_x.cometsnap.json"), SnapNaming.SidecarFor(Path.Combine("a", "snap_x.png")));
    }
}
