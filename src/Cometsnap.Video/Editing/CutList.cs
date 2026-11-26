using System.Globalization;
using System.Text.Json;

namespace Cometsnap.Video.Editing;

/// <summary>A span of time in a recording, from <see cref="Start"/> (inclusive) to <see cref="End"/>.</summary>
public readonly record struct TimeRange(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Length => End - Start;
    public bool Contains(TimeSpan t) => t >= Start && t < End;
}

/// <summary>A piece of the source kept in the output: source span and where it lands in the output.</summary>
public readonly record struct KeptPiece(TimeSpan SourceStart, TimeSpan SourceEnd, TimeSpan OutputStart)
{
    public TimeSpan Length => SourceEnd - SourceStart;
}

/// <summary>
/// What to keep of a recording: a trimmed start and end, and sections cut out of the middle.
/// Pure logic (no Windows), so the editor and both exporters share one tested definition.
/// </summary>
public sealed class CutList
{
    /// <summary>The shortest piece the editor lets you keep or cut (a tenth of a second).</summary>
    public static readonly TimeSpan MinPiece = TimeSpan.FromMilliseconds(100);

    private readonly List<TimeRange> _cuts = new();
    private TimeSpan _trimStart;
    private TimeSpan _trimEnd;

    public CutList(TimeSpan duration)
    {
        Duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        _trimEnd = Duration;
    }

    /// <summary>Length of the whole source.</summary>
    public TimeSpan Duration { get; }

    /// <summary>Where the output starts in the source.</summary>
    public TimeSpan TrimStart
    {
        get => _trimStart;
        set => _trimStart = Clamp(value, TimeSpan.Zero, Max(TimeSpan.Zero, _trimEnd - MinPiece));
    }

    /// <summary>Where the output ends in the source.</summary>
    public TimeSpan TrimEnd
    {
        get => _trimEnd;
        set => _trimEnd = Clamp(value, Min(Duration, _trimStart + MinPiece), Duration);
    }

    /// <summary>Sections removed from the middle, sorted and not overlapping.</summary>
    public IReadOnlyList<TimeRange> Cuts => _cuts;

    /// <summary>True when nothing is trimmed or cut.</summary>
    public bool IsUntouched => _trimStart == TimeSpan.Zero && _trimEnd == Duration && _cuts.Count == 0;

    /// <summary>
    /// Cuts out a section. Overlapping or touching cuts merge. Refused (false) when it is too short,
    /// lies outside the trimmed part, or would leave less than <see cref="MinPiece"/> to keep.
    /// </summary>
    public bool AddCut(TimeSpan a, TimeSpan b)
    {
        if (b < a) (a, b) = (b, a);
        a = Max(a, _trimStart);
        b = Min(b, _trimEnd);
        if (b - a < MinPiece) return false;

        var merged = new List<TimeRange>();
        var cut = new TimeRange(a, b);
        foreach (var c in _cuts)
        {
            if (c.End < cut.Start || c.Start > cut.End) merged.Add(c);
            else cut = new TimeRange(Min(c.Start, cut.Start), Max(c.End, cut.End));
        }
        merged.Add(cut);
        merged.Sort((x, y) => x.Start.CompareTo(y.Start));

        var before = _cuts.ToList();
        _cuts.Clear();
        _cuts.AddRange(merged);
        if (KeptLength < MinPiece)
        {
            _cuts.Clear();
            _cuts.AddRange(before);
            return false;
        }
        return true;
    }

    /// <summary>Puts back the cut section under <paramref name="t"/>. False when there is none.</summary>
    public bool RestoreCutAt(TimeSpan t)
    {
        var i = _cuts.FindIndex(c => c.Contains(t));
        if (i < 0) return false;
        _cuts.RemoveAt(i);
        return true;
    }

    /// <summary>The cut section under <paramref name="t"/>, if any.</summary>
    public TimeRange? CutAt(TimeSpan t)
    {
        foreach (var c in _cuts) if (c.Contains(t)) return c;
        return null;
    }

    /// <summary>The parts of the source that end up in the output, in order.</summary>
    public IReadOnlyList<KeptPiece> Kept()
    {
        var pieces = new List<KeptPiece>();
        var at = _trimStart;
        var output = TimeSpan.Zero;
        foreach (var c in _cuts)
        {
            if (c.End <= _trimStart || c.Start >= _trimEnd) continue;
            var end = Max(at, c.Start);
            if (end > at)
            {
                pieces.Add(new KeptPiece(at, end, output));
                output += end - at;
            }
            at = Max(at, c.End);
        }
        if (_trimEnd > at) pieces.Add(new KeptPiece(at, _trimEnd, output));
        return pieces;
    }

    /// <summary>Length of the output.</summary>
    public TimeSpan KeptLength
    {
        get
        {
            var total = TimeSpan.Zero;
            foreach (var p in Kept()) total += p.Length;
            return total;
        }
    }

    /// <summary>True when the moment <paramref name="t"/> of the source is in the output.</summary>
    public bool IsKept(TimeSpan t)
    {
        foreach (var p in Kept()) if (t >= p.SourceStart && t < p.SourceEnd) return true;
        return false;
    }

    /// <summary>
    /// For playback: <paramref name="t"/> if it is kept, else the start of the next kept piece,
    /// or null when nothing is kept after it (playback is over).
    /// </summary>
    public TimeSpan? NextKept(TimeSpan t)
    {
        foreach (var p in Kept())
        {
            if (t < p.SourceStart) return p.SourceStart;
            if (t < p.SourceEnd) return t;
        }
        return null;
    }

    /// <summary>Source time → output time (a removed moment maps to where the cut joins).</summary>
    public TimeSpan ToOutput(TimeSpan t)
    {
        var last = TimeSpan.Zero;
        foreach (var p in Kept())
        {
            if (t < p.SourceStart) return p.OutputStart;
            if (t < p.SourceEnd) return p.OutputStart + (t - p.SourceStart);
            last = p.OutputStart + p.Length;
        }
        return last;
    }

    /// <summary>
    /// For a source frame shown from <paramref name="from"/> until <paramref name="to"/>: the output
    /// times at which it must be written. Usually one, none when the frame is cut, and one per kept
    /// piece that starts while the frame is on screen (the frame opens that piece).
    /// </summary>
    public List<TimeSpan> FrameOutputTimes(TimeSpan from, TimeSpan to)
    {
        var times = new List<TimeSpan>();
        foreach (var p in Kept())
        {
            if (p.SourceEnd <= from || p.SourceStart >= to) continue;
            var start = Max(from, p.SourceStart);
            times.Add(p.OutputStart + (start - p.SourceStart));
        }
        return times;
    }

    /// <summary>
    /// For an audio block covering <paramref name="from"/> to <paramref name="to"/>: the kept pieces of it,
    /// with where each lands in the output.
    /// </summary>
    public List<KeptPiece> Overlap(TimeSpan from, TimeSpan to)
    {
        var parts = new List<KeptPiece>();
        foreach (var p in Kept())
        {
            var a = Max(from, p.SourceStart);
            var b = Min(to, p.SourceEnd);
            if (b <= a) continue;
            parts.Add(new KeptPiece(a, b, p.OutputStart + (a - p.SourceStart)));
        }
        return parts;
    }

    public CutList Clone()
    {
        var c = new CutList(Duration) { _trimStart = _trimStart, _trimEnd = _trimEnd };
        c._cuts.AddRange(_cuts);
        return c;
    }

    /// <summary>Same trim and cuts as <paramref name="other"/>.</summary>
    public bool SameAs(CutList other) =>
        other._trimStart == _trimStart && other._trimEnd == _trimEnd && other._cuts.SequenceEqual(_cuts);

    // ------------------------------------------------------------------ saving

    private sealed record Saved(double Start, double End, double[][] Cuts);

    public string ToJson() => JsonSerializer.Serialize(new Saved(
        _trimStart.TotalSeconds, _trimEnd.TotalSeconds,
        _cuts.Select(c => new[] { c.Start.TotalSeconds, c.End.TotalSeconds }).ToArray()));

    /// <summary>Reads what <see cref="ToJson"/> wrote; anything odd falls back to "untouched".</summary>
    public static CutList FromJson(string json, TimeSpan duration)
    {
        var list = new CutList(duration);
        try
        {
            var s = JsonSerializer.Deserialize<Saved>(json);
            if (s is null) return list;
            list.TrimEnd = TimeSpan.FromSeconds(s.End);
            list.TrimStart = TimeSpan.FromSeconds(s.Start);
            foreach (var c in s.Cuts ?? Array.Empty<double[]>())
                if (c.Length == 2) list.AddCut(TimeSpan.FromSeconds(c[0]), TimeSpan.FromSeconds(c[1]));
        }
        catch (JsonException) { /* untouched */ }
        return list;
    }

    /// <summary>"1:05.3" style label for the timeline.</summary>
    public static string Label(TimeSpan t, bool tenths = true)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        var minutes = (int)t.TotalMinutes;
        var seconds = t.TotalSeconds - minutes * 60;
        return tenths
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00.0}", minutes, Math.Floor(seconds * 10) / 10)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", minutes, Math.Floor(seconds));
    }

    private static TimeSpan Clamp(TimeSpan v, TimeSpan lo, TimeSpan hi) => v < lo ? lo : v > hi ? hi : v;
    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
