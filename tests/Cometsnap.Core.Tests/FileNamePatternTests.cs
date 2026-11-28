using Cometsnap.Core.Storage;

namespace Cometsnap.Core.Tests;

public class FileNamePatternTests
{
    private static readonly DateTime When = new(2026, 11, 26, 8, 56, 12);
    private static readonly char Sep = Path.DirectorySeparatorChar;

    [Fact]
    public void Default_gives_the_classic_month_folder_name() =>
        Assert.Equal($"2026-11{Sep}snap_2026-11-26_08-56-12", FileNamePattern.Expand(FileNamePattern.Default, When));

    [Theory]
    [InlineData("{date}_{time}", "2026-11-26_08-56-12")]
    [InlineData("Screenshot {year}{month}{day}-{hour}{minute}{second}", "Screenshot 20261126-085612")]
    [InlineData("shot-{counter}", "shot-007")]
    [InlineData("{DATE} {Time}", "2026-11-26 08-56-12")]
    [InlineData("{unknown}-{date}", "{unknown}-2026-11-26")]
    [InlineData("a:b*c?d", "a_b_c_d")]
    public void Expands_tokens_and_cleans_names(string pattern, string expected) =>
        Assert.Equal(expected, FileNamePattern.Expand(pattern, When, 7));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("///")]
    [InlineData("..")]
    [InlineData(null)]
    public void Falls_back_to_the_default_when_nothing_is_left(string? pattern) =>
        Assert.Equal(FileNamePattern.Expand(FileNamePattern.Default, When), FileNamePattern.Expand(pattern, When));

    [Fact]
    public void Never_leaves_the_captures_folder()
    {
        var name = FileNamePattern.Expand("../../etc/{date}", When);
        Assert.Equal($"etc{Sep}2026-11-26", name);
        Assert.DoesNotContain("..", name);
        Assert.False(Path.IsPathRooted(FileNamePattern.Expand("/abs/{date}", When)));
    }

    [Fact]
    public void Limits_the_folder_depth()
    {
        var name = FileNamePattern.Expand("a/b/c/d/e/f", When);
        Assert.Equal(string.Join(Sep, "a", "b", "c", "d_e_f"), name);
    }

    [Fact]
    public void Avoids_Windows_device_names() =>
        Assert.Equal("_CON", FileNamePattern.Expand("CON", When));

    [Fact]
    public void Knows_when_a_counter_is_used()
    {
        Assert.True(FileNamePattern.UsesCounter("shot {Counter}"));
        Assert.False(FileNamePattern.UsesCounter(FileNamePattern.Default));
        Assert.False(FileNamePattern.IsUsable("  /  "));
        Assert.True(FileNamePattern.IsUsable("plain"));
    }

    [Fact]
    public void Next_free_path_uses_pattern_and_format()
    {
        var taken = new HashSet<string> { Path.Combine("r", "shots", "a.jpg") };
        Assert.Equal(Path.Combine("r", "shots", "a_2.jpg"), SnapNaming.NextFreePath("r", When, "shots/a", ".jpg", 1, taken.Contains));
        Assert.Equal(".jpg", SnapNaming.ExtensionFor("JPEG"));
        Assert.Equal(".png", SnapNaming.ExtensionFor("webp"));
        Assert.Equal("snap_2026-11-26_08-56-12", SnapNaming.SuggestedName(null, When));
    }
}
