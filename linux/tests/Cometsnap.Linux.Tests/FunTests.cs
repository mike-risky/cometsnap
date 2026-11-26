using Cometsnap.Linux.Core;


namespace Cometsnap.Linux.Tests;

public class FunTests
{
    [Theory]
    [InlineData(10, true)]
    [InlineData(100, true)]
    [InlineData(1000, true)]
    [InlineData(30000, true)]
    [InlineData(1, false)]
    [InlineData(999, false)]
    [InlineData(12000, false)]
    public void Milestones_are_round_numbers(int n, bool expected) => Assert.Equal(expected, SnapMilestones.IsMilestone(n));

    [Fact]
    public void Title_uses_ordinal_and_thousands() => Assert.Equal("1,000th snap!", SnapMilestones.Title(1000));

    [Fact]
    public void Counting_reaches_milestone()
    {
        var s = new AppSettings { SnapCount = 99, SnapMonth = "2026-11", SnapMonthCount = 5 };
        var e = SnapMilestones.Record(s, new DateTime(2026, 11, 26));
        Assert.Equal(100, e.Milestone);
        Assert.Null(e.FinishedMonth);
        Assert.Equal(6, s.SnapMonthCount);
    }

    [Fact]
    public void First_snap_of_a_month_reports_the_last_one()
    {
        var s = new AppSettings { SnapCount = 500, SnapMonth = "2026-10", SnapMonthCount = 412 };
        var e = SnapMilestones.Record(s, new DateTime(2026, 11, 1, 9, 0, 0));
        Assert.Equal(new MonthSummary(2026, 10, 412), e.FinishedMonth);
        Assert.Equal("October", e.FinishedMonth!.MonthName);
        Assert.Equal("2026-11", s.SnapMonth);
        Assert.Equal(1, s.SnapMonthCount);
        Assert.Null(e.Milestone);
    }

    [Fact]
    public void Seeding_counts_existing_snaps_quietly()
    {
        var s = new AppSettings();
        var now = new DateTime(2026, 11, 26);
        SnapMilestones.Seed(s, new[] { now.AddDays(-1), now.AddDays(-2), now.AddMonths(-2) }, now);
        Assert.True(s.SnapCountSeeded);
        Assert.Equal(3, s.SnapCount);
        Assert.Equal(2, s.SnapMonthCount);
    }

    [Theory]
    [InlineData(2026, 10, 17, Season.None)]
    [InlineData(2026, 10, 31, Season.Halloween)]
    [InlineData(2026, 12, 24, Season.Winter)]
    [InlineData(2027, 1, 2, Season.None)]
    public void Seasons_by_date(int y, int m, int d, Season expected) => Assert.Equal(expected, Seasons.For(new DateTime(y, m, d)));

    [Fact]
    public void Konami_code_is_detected_even_after_a_false_start()
    {
        var k = new KonamiCode();
        var keys = new[] { "Up", "Up", "Up", "Down", "Down", "Left", "Right", "Left", "Right", "B" };
        foreach (var key in keys) Assert.False(k.Push(key));
        Assert.True(k.Push("A"));
        Assert.False(k.Push("A"));
    }
}
