using Cometsnap.Linux.Trimming;

namespace Cometsnap.Linux.Tests;

public class CutListTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void New_list_keeps_everything()
    {
        var c = new CutList(S(10));
        Assert.True(c.IsUntouched);
        var k = Assert.Single(c.Kept());
        Assert.Equal(new KeptPiece(S(0), S(10), S(0)), k);
        Assert.Equal(S(10), c.KeptLength);
    }

    [Fact]
    public void Trim_and_cut_give_the_kept_pieces_in_order()
    {
        var c = new CutList(S(10)) { TrimStart = S(1), TrimEnd = S(9) };
        Assert.True(c.AddCut(S(4), S(6)));
        Assert.Equal(new[]
        {
            new KeptPiece(S(1), S(4), S(0)),
            new KeptPiece(S(6), S(9), S(3)),
        }, c.Kept());
        Assert.Equal(S(6), c.KeptLength);
        Assert.False(c.IsUntouched);
    }

    [Fact]
    public void Overlapping_cuts_merge_and_can_be_restored()
    {
        var c = new CutList(S(10));
        c.AddCut(S(2), S(4));
        c.AddCut(S(3), S(5));
        c.AddCut(S(7), S(8));
        Assert.Equal(new[] { new TimeRange(S(2), S(5)), new TimeRange(S(7), S(8)) }, c.Cuts);
        Assert.True(c.RestoreCutAt(S(2.5)));
        Assert.Equal(new[] { new TimeRange(S(7), S(8)) }, c.Cuts);
        Assert.False(c.RestoreCutAt(S(2.5)));
    }

    [Fact]
    public void Cutting_everything_is_refused()
    {
        var c = new CutList(S(5));
        Assert.False(c.AddCut(S(0), S(5)));
        Assert.True(c.IsUntouched);
        Assert.False(c.AddCut(S(1), S(1.05))); // too short to be a cut
    }

    [Fact]
    public void Trim_handles_cannot_cross()
    {
        var c = new CutList(S(5)) { TrimStart = S(4) };
        c.TrimEnd = S(1);
        Assert.True(c.TrimEnd - c.TrimStart >= CutList.MinPiece);
        c.TrimStart = S(99);
        Assert.True(c.TrimStart < c.TrimEnd);
    }

    [Fact]
    public void Playback_skips_cut_sections()
    {
        var c = new CutList(S(10)) { TrimStart = S(1), TrimEnd = S(9) };
        c.AddCut(S(4), S(6));
        Assert.Equal(S(1), c.NextKept(S(0)));
        Assert.Equal(S(2), c.NextKept(S(2)));
        Assert.Equal(S(6), c.NextKept(S(4.5)));
        Assert.Null(c.NextKept(S(9)));
        Assert.Equal(S(3), c.ToOutput(S(5)));
        Assert.Equal(S(4), c.ToOutput(S(7)));
    }

    [Fact]
    public void A_frame_spanning_a_cut_opens_the_next_piece()
    {
        var c = new CutList(S(10));
        c.AddCut(S(2), S(4));
        // Frame 1.9–4.1 s: shown at 1.9 (output 1.9) and again where the second piece starts (output 2.0).
        Assert.Equal(new[] { S(1.9), S(2) }, c.FrameOutputTimes(S(1.9), S(4.1)));
        Assert.Empty(c.FrameOutputTimes(S(2.5), S(3)));
        Assert.Equal(new[] { S(3) }, c.FrameOutputTimes(S(5), S(5.1)));
    }

    [Fact]
    public void Audio_blocks_are_sliced_at_cuts()
    {
        var c = new CutList(S(10));
        c.AddCut(S(2), S(4));
        Assert.Equal(new[]
        {
            new KeptPiece(S(1.5), S(2), S(1.5)),
            new KeptPiece(S(4), S(4.5), S(2)),
        }, c.Overlap(S(1.5), S(4.5)));
    }

    [Fact]
    public void Saved_cuts_come_back()
    {
        var c = new CutList(S(10)) { TrimStart = S(0.5), TrimEnd = S(9.5) };
        c.AddCut(S(3), S(4));
        var back = CutList.FromJson(c.ToJson(), S(10));
        Assert.True(back.SameAs(c));
        Assert.True(CutList.FromJson("nonsense", S(10)).IsUntouched);
    }

    [Theory]
    [InlineData(0, "0:00.0")]
    [InlineData(5.37, "0:05.3")]
    [InlineData(65.2, "1:05.2")]
    public void Labels_read_like_a_player(double seconds, string expected) =>
        Assert.Equal(expected, CutList.Label(S(seconds)));
}
