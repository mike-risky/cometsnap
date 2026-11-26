namespace Cometsnap.Core.Capture;

/// <summary>What happened to a frame given to <see cref="ScrollStitcher.Add"/>.</summary>
public enum StitchStep
{
    /// <summary>The first frame: the start of the image.</summary>
    First,
    /// <summary>The page moved and the new rows were added at the bottom.</summary>
    Appended,
    /// <summary>Nothing moved (the end of the page, or the scroll has not happened yet).</summary>
    Unchanged,
    /// <summary>The frame does not continue the previous one (the page jumped, or changed too much).</summary>
    Lost,
}

/// <summary>
/// Glues the frames of a scrolling capture into one tall image. Each new frame is compared with
/// the previous one row by row to find how far the page scrolled; only the rows that came into
/// view are kept. Rows that never move at the top and bottom (a sticky header, a status bar)
/// appear once. Works on BGRX/BGRA pixels, top-down, stride = width × 4.
/// </summary>
public sealed class ScrollStitcher
{
    /// <summary>Columns on the right ignored when comparing rows: the scrollbar thumb moves at every step.</summary>
    public int IgnoreRight { get; }

    public int Width { get; }
    public int FrameHeight { get; }

    /// <summary>Rows of the sticky header and footer (known after the first scroll).</summary>
    public int Header { get; private set; }
    public int Footer { get; private set; }
    private bool _edgesKnown;

    /// <summary>Height of the part that scrolls (frame minus header and footer).</summary>
    public int ScrollingHeight => FrameHeight - Header - Footer;

    /// <summary>How far the last appended frame had scrolled, in pixels.</summary>
    public int LastShift { get; private set; }

    /// <summary>Height of the image so far.</summary>
    public int Height => _first is null ? 0 : FrameHeight + _strips.Sum(s => s.Length) / (Width * 4);

    private byte[]? _first;
    private byte[]? _last;
    private ulong[] _lastHashes = Array.Empty<ulong>();
    private bool[] _lastPlain = Array.Empty<bool>();
    private readonly List<byte[]> _strips = new();

    public ScrollStitcher(int width, int frameHeight, int? ignoreRight = null)
    {
        if (width <= 0 || frameHeight <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        FrameHeight = frameHeight;
        IgnoreRight = Math.Clamp(ignoreRight ?? Math.Min(24, width / 8), 0, width - 1);
    }

    public StitchStep Add(byte[] pixels)
    {
        if (pixels.Length < Width * 4 * FrameHeight) throw new ArgumentException("Frame is too small", nameof(pixels));
        var (hashes, plain) = HashRows(pixels);
        if (_last is null)
        {
            _first = _last = pixels;
            _lastHashes = hashes;
            _lastPlain = plain;
            return StitchStep.First;
        }

        if (hashes.AsSpan().SequenceEqual(_lastHashes)) return StitchStep.Unchanged;

        if (!_edgesKnown)
        {
            (Header, Footer) = FindFixedEdges(_lastHashes, _lastPlain, hashes);
            _edgesKnown = true;
        }

        var shift = FindShift(_lastHashes, hashes, plain, Header, FrameHeight - Footer, LastShift);
        if (shift <= 0)
        {
            // Only the sticky parts changed (or nothing we can match): not a scroll.
            return shift == 0 ? StitchStep.Unchanged : StitchStep.Lost;
        }

        // The rows that scrolled into view: the bottom `shift` rows of the scrolling part.
        var rowBytes = Width * 4;
        var strip = new byte[shift * rowBytes];
        Buffer.BlockCopy(pixels, (FrameHeight - Footer - shift) * rowBytes, strip, 0, strip.Length);
        _strips.Add(strip);
        _last = pixels;
        _lastHashes = hashes;
        _lastPlain = plain;
        LastShift = shift;
        return StitchStep.Appended;
    }

    /// <summary>The finished image: first frame (without its footer), the new rows, then the footer of the last frame.</summary>
    public byte[] Compose(out int height)
    {
        if (_first is null || _last is null) throw new InvalidOperationException("No frames");
        var rowBytes = Width * 4;
        height = Height;
        var result = new byte[height * rowBytes];
        var top = (FrameHeight - Footer) * rowBytes;
        Buffer.BlockCopy(_first, 0, result, 0, top);
        var at = top;
        foreach (var strip in _strips)
        {
            Buffer.BlockCopy(strip, 0, result, at, strip.Length);
            at += strip.Length;
        }
        Buffer.BlockCopy(_last, top, result, at, Footer * rowBytes);
        return result;
    }

    private (ulong[] Hashes, bool[] Plain) HashRows(byte[] pixels)
    {
        var hashes = new ulong[FrameHeight];
        var plain = new bool[FrameHeight];
        var rowBytes = Width * 4;
        var used = (Width - IgnoreRight) * 4;
        for (var y = 0; y < FrameHeight; y++)
        {
            var row = pixels.AsSpan(y * rowBytes, used);
            var h = 14695981039346656037UL;
            var firstPixel = row[0] | row[1] << 8 | row[2] << 16;
            var same = true;
            for (var i = 0; i < row.Length; i += 4)
            {
                var px = row[i] | row[i + 1] << 8 | row[i + 2] << 16; // the 4th byte (alpha / X) is ignored
                if (px != firstPixel) same = false;
                h = (h ^ (uint)px) * 1099511628211UL;
            }
            hashes[y] = h;
            plain[y] = same;
        }
        return (hashes, plain);
    }

    /// <summary>
    /// Rows that stayed put at the top and the bottom. A run ends at its last row with some
    /// content, so the empty part of a page right under a header is not mistaken for the header.
    /// </summary>
    internal static (int Header, int Footer) FindFixedEdges(ulong[] previous, bool[] previousPlain, ulong[] current)
    {
        var n = previous.Length;
        var limit = n / 3;
        int Run(int start, int step)
        {
            var run = 0;
            var lastContent = 0;
            for (var y = start; run < limit && y >= 0 && y < n && previous[y] == current[y]; y += step)
            {
                run++;
                if (!previousPlain[y]) lastContent = run;
            }
            return lastContent;
        }
        return (Run(0, 1), Run(n - 1, -1));
    }

    /// <summary>
    /// How many pixels the part between <paramref name="top"/> and <paramref name="bottom"/> moved up:
    /// the shift that lines up the most rows with content. 0 when nothing moved, -1 when no shift fits.
    /// </summary>
    internal static int FindShift(ulong[] previous, ulong[] current, bool[] currentPlain, int top, int bottom, int expected)
    {
        var height = bottom - top;
        if (height < 8) return -1;

        var bestShift = -1;
        var bestScore = 0;
        var bestDistance = int.MaxValue;
        for (var shift = 0; shift < height - 4; shift++)
        {
            int matches = 0, misses = 0;
            for (var i = 0; i < height - shift; i++)
            {
                var y = top + i;
                if (currentPlain[y]) continue; // empty rows match anything: they prove nothing
                if (current[y] == previous[y + shift]) matches++;
                else misses++;
            }
            var total = matches + misses;
            if (total == 0 || matches < Math.Min(6, total) || matches * 10 < total * 9) continue;

            var score = matches - 4 * misses;
            var distance = expected > 0 ? Math.Abs(shift - expected) : shift;
            if (score > bestScore || (score == bestScore && distance < bestDistance))
            {
                bestScore = score;
                bestShift = shift;
                bestDistance = distance;
            }
        }
        return bestShift;
    }
}
