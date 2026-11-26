using System.Globalization;
using Cometsnap.Core.Settings;

namespace Cometsnap.Core.Fun;

/// <summary>A finished month: "You captured 412 snaps in October".</summary>
public sealed record MonthSummary(int Year, int Month, int Count)
{
    public string MonthName => new DateTime(Year, Month, 1).ToString("MMMM", CultureInfo.InvariantCulture);
}

/// <summary>What one more snap unlocked, if anything.</summary>
public sealed record SnapEvent(int? Milestone, MonthSummary? FinishedMonth);

/// <summary>Counts snaps and spots the round numbers worth a tiny celebration.</summary>
public static class SnapMilestones
{
    private static readonly int[] Steps = { 10, 50, 100, 250, 500, 1000, 2500, 5000, 10000 };

    public static bool IsMilestone(int count) =>
        count > 0 && (Array.IndexOf(Steps, count) >= 0 || (count > 10000 && count % 10000 == 0));

    /// <summary>"1,000th snap!"</summary>
    public static string Title(int count) => $"{count.ToString("N0", CultureInfo.InvariantCulture)}{Suffix(count)} snap!";

    public static string Suffix(int n)
    {
        if (n % 100 is 11 or 12 or 13) return "th";
        return (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
    }

    public static string MonthKey(DateTime when) => when.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>Counts one snap taken at <paramref name="now"/>, updating the settings.</summary>
    public static SnapEvent Record(AppSettings s, DateTime now)
    {
        var key = MonthKey(now);
        MonthSummary? finished = null;
        if (s.SnapMonth != key)
        {
            if (s.SnapMonthCount > 0 && TryParseMonth(s.SnapMonth, out var y, out var m))
                finished = new MonthSummary(y, m, s.SnapMonthCount);
            s.SnapMonth = key;
            s.SnapMonthCount = 0;
        }
        s.SnapMonthCount++;
        s.SnapCount++;
        return new SnapEvent(IsMilestone(s.SnapCount) ? s.SnapCount : null, finished);
    }

    /// <summary>First run with this feature: start from the snaps already on disk, without celebrating.</summary>
    public static void Seed(AppSettings s, IEnumerable<DateTime> existingSnaps, DateTime now)
    {
        var key = MonthKey(now);
        var total = 0;
        var thisMonth = 0;
        foreach (var t in existingSnaps)
        {
            total++;
            if (MonthKey(t) == key) thisMonth++;
        }
        s.SnapCount = Math.Max(s.SnapCount, total);
        if (s.SnapMonth != key) { s.SnapMonth = key; s.SnapMonthCount = 0; }
        s.SnapMonthCount = Math.Max(s.SnapMonthCount, thisMonth);
        s.SnapCountSeeded = true;
    }

    private static bool TryParseMonth(string key, out int year, out int month)
    {
        year = month = 0;
        return DateTime.TryParseExact(key, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            && (year = d.Year) > 0 && (month = d.Month) > 0;
    }
}
