using System.Text;

namespace Cometsnap.Core.Text;

/// <summary>A line of recognized text and where it sits in the image (pixels).</summary>
public readonly record struct RecognizedLine(string Text, double Left, double Top, double Height);

/// <summary>Turns the lines an OCR engine found into clean text, ready for the clipboard.</summary>
public static class OcrText
{
    /// <summary>
    /// Joins lines in the order the engine read them. A gap taller than a line starts a new paragraph (an empty line),
    /// and the spaces some engines put between Chinese or Japanese characters are removed.
    /// </summary>
    public static string Join(IEnumerable<RecognizedLine> lines)
    {
        var sorted = lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).ToList();
        var sb = new StringBuilder();
        RecognizedLine? previous = null;
        foreach (var line in sorted)
        {
            if (previous is { } p)
            {
                var gap = line.Top - (p.Top + p.Height);
                var size = Math.Max(1, Math.Min(p.Height, line.Height));
                sb.Append(gap > size * 1.1 ? "\n\n" : "\n");
            }
            sb.Append(TidyLine(line.Text));
            previous = line;
        }
        return sb.ToString();
    }

    /// <summary>Removes spaces between two CJK characters and trims the line.</summary>
    public static string TidyLine(string text)
    {
        text = text.Trim();
        if (text.Length < 3) return text;
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == ' ' && i > 0 && i < text.Length - 1 && IsCjk(text[i - 1]) && IsCjk(text[i + 1])) continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Han, kana, CJK punctuation and full-width forms.</summary>
    public static bool IsCjk(char c) =>
        c is >= '　' and <= 'ヿ'   // CJK punctuation, hiragana, katakana
          or >= '㐀' and <= '䶿'   // CJK extension A
          or >= '一' and <= '鿿'   // CJK unified ideographs
          or >= '豈' and <= '﫿'   // compatibility ideographs
          or >= '＀' and <= '｠';  // full-width forms

    /// <summary>
    /// Splits a tall image into bands of at most <paramref name="maxRows"/> rows for an OCR engine
    /// with a size limit, cutting on empty rows so no line of text is cut in half.
    /// Returns (start, length) pairs that cover every row.
    /// </summary>
    public static List<(int Start, int Length)> Bands(IReadOnlyList<bool> emptyRow, int maxRows)
    {
        var bands = new List<(int, int)>();
        var n = emptyRow.Count;
        if (maxRows <= 0) throw new ArgumentOutOfRangeException(nameof(maxRows));
        var start = 0;
        while (start < n)
        {
            if (n - start <= maxRows)
            {
                bands.Add((start, n - start));
                break;
            }
            var end = start + maxRows; // exclusive
            var cut = end;
            // Look back up to a third of the band for an empty row to cut on.
            for (var y = end - 1; y > start + maxRows * 2 / 3; y--)
            {
                if (emptyRow[y])
                {
                    cut = y + 1;
                    break;
                }
            }
            bands.Add((start, cut - start));
            start = cut;
        }
        return bands;
    }
}
