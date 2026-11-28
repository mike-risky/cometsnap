using System.Globalization;
using System.IO;
using System.Text;

namespace Cometsnap.Core.Storage;

/// <summary>
/// The name a capture gets when it is saved on its own, written by the user with tokens like
/// <c>{date}</c> and <c>{time}</c>. A "/" makes a subfolder: the default
/// <c>{year}-{month}/snap_{date}_{time}</c> gives <c>2026-11\snap_2026-11-26_08-56-12.png</c>.
/// Shared with the Linux app (linked file), and mirrored in the Mac app (FileNamePattern.swift).
/// </summary>
public static class FileNamePattern
{
    public const string Default = "{year}-{month}/snap_{date}_{time}";

    /// <summary>Most folder levels a pattern may create, so a typo can't dig a deep tunnel.</summary>
    public const int MaxFolders = 3;

    /// <summary>The tokens, in the order the settings show them.</summary>
    public static readonly string[] Tokens =
        { "{date}", "{time}", "{year}", "{month}", "{day}", "{hour}", "{minute}", "{second}", "{counter}" };

    /// <summary>
    /// The path of the capture relative to the captures folder, without extension, using "/" or
    /// the platform separator between folders. Never empty, never escapes the captures folder.
    /// </summary>
    /// <param name="counter">Value of {counter}: the snap number (1, 2, 3...).</param>
    public static string Expand(string? pattern, DateTime when, int counter = 1)
    {
        var expanded = ExpandTokens(string.IsNullOrWhiteSpace(pattern) ? Default : pattern, when, counter);
        var segments = Clean(expanded);
        if (segments.Count == 0) segments = Clean(ExpandTokens(Default, when, counter));
        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    /// <summary>True when the pattern gives a usable name (it may still have no token at all).</summary>
    public static bool IsUsable(string? pattern) =>
        !string.IsNullOrWhiteSpace(pattern) && Clean(ExpandTokens(pattern, new DateTime(2026, 1, 2, 3, 4, 5), 1)).Count > 0;

    /// <summary>True when the pattern has {counter}, so each save uses up a number.</summary>
    public static bool UsesCounter(string? pattern) =>
        (string.IsNullOrWhiteSpace(pattern) ? Default : pattern).Contains("{counter}", StringComparison.OrdinalIgnoreCase);

    private static string ExpandTokens(string pattern, DateTime when, int counter)
    {
        var sb = new StringBuilder(pattern.Length + 16);
        var i = 0;
        while (i < pattern.Length)
        {
            if (pattern[i] == '{')
            {
                var end = pattern.IndexOf('}', i + 1);
                if (end > i)
                {
                    var value = Token(pattern.Substring(i + 1, end - i - 1), when, counter);
                    if (value is not null)
                    {
                        sb.Append(value);
                        i = end + 1;
                        continue;
                    }
                }
            }
            sb.Append(pattern[i]);
            i++;
        }
        return sb.ToString();
    }

    private static string? Token(string name, DateTime when, int counter)
    {
        string F(string format) => when.ToString(format, CultureInfo.InvariantCulture);
        return name.Trim().ToLowerInvariant() switch
        {
            "date" => F("yyyy-MM-dd"),
            "time" => F("HH-mm-ss"),
            "year" => F("yyyy"),
            "month" => F("MM"),
            "day" => F("dd"),
            "hour" => F("HH"),
            "minute" => F("mm"),
            "second" => F("ss"),
            "counter" => Math.Max(1, counter).ToString("D3", CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    // Characters Windows refuses in a file name (the strictest of the three platforms).
    private static readonly char[] Invalid = { '<', '>', ':', '"', '|', '?', '*' };

    private static List<string> Clean(string path)
    {
        var parts = path.Split(new[] { '/', '\\' }, StringSplitOptions.None);
        var result = new List<string>(parts.Length);
        foreach (var raw in parts)
        {
            var sb = new StringBuilder(raw.Length);
            foreach (var c in raw)
                sb.Append(c < 32 || Array.IndexOf(Invalid, c) >= 0 ? '_' : c);
            // Windows drops trailing dots and spaces; leading dots would hide the file.
            var seg = sb.ToString().Trim().TrimEnd('.').TrimStart('.').Trim();
            if (seg.Length == 0) continue;
            if (seg.Length > 120) seg = seg[..120];
            if (IsReservedDeviceName(seg)) seg = "_" + seg;
            result.Add(seg);
        }
        // Keep the file name and at most MaxFolders folders before it.
        if (result.Count > MaxFolders + 1)
        {
            var folders = result.Take(MaxFolders);
            result = folders.Append(string.Join("_", result.Skip(MaxFolders))).ToList();
        }
        return result;
    }

    private static bool IsReservedDeviceName(string segment)
    {
        var stem = segment.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL"
            || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3]) && stem[3] != '0');
    }
}
