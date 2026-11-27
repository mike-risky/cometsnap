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
    private int[] _lastSig = Array.Empty<int>();

    /// <summary>Columns of each row's coarse signature (average brightness of that many slices).</summary>
    internal const int Slices = 16;
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
        var (hashes, plain, sig) = HashRows(pixels);
        if (_last is null)
        {
            _first = _last = pixels;
            _lastHashes = hashes;
            _lastPlain = plain;
            _lastSig = sig;
            return StitchStep.First;
        }

        if (hashes.AsSpan().SequenceEqual(_lastHashes)) return StitchStep.Unchanged;

        if (!_edgesKnown)
        {
            (Header, Footer) = FindFixedEdges(_lastHashes, _lastPlain, hashes);
            _edgesKnown = true;
        }

        var shift = FindShift(_lastHashes, hashes, plain, Header, FrameHeight - Footer, LastShift);
        // No exact match: smooth scrolling and fractional display scaling redraw text a little
        // differently at every position, and hover effects change a few rows. Match loosely.
        if (shift < 0) shift = FindShiftLoose(_lastSig, sig, plain, Header, FrameHeight - Footer, LastShift);
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
        _lastSig = sig;
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

    private (ulong[] Hashes, bool[] Plain, int[] Signature) HashRows(byte[] pixels)
    {
        var hashes = new ulong[FrameHeight];
        var plain = new bool[FrameHeight];
        var sig = new int[FrameHeight * Slices];
        var rowBytes = Width * 4;
        var usedPixels = Width - IgnoreRight;
        var used = usedPixels * 4;
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

            // Average brightness (×4 for precision) of each slice of the row.
            for (var k = 0; k < Slices; k++)
            {
                var from = k * usedPixels / Slices;
                var to = Math.Max(from + 1, (k + 1) * usedPixels / Slices);
                var sum = 0;
                for (var x = from; x < to; x++)
                {
                    var i = x * 4;
                    sum += row[i] + 2 * row[i + 1] + row[i + 2];
                }
                sig[y * Slices + k] = sum / (to - from);
            }
        }
        return (hashes, plain, sig);
    }

    /// <summary>
    /// Like <see cref="FindShift"/>, but rows only have to look alike (same brightness slice by slice,
    /// give or take a little), not be identical. The shift with the smallest difference wins when it
    /// is clearly better than not moving at all. 0 when nothing moved, -1 when no shift fits.
    /// </summary>
    internal static int FindShiftLoose(int[] previous, int[] current, bool[] currentPlain, int top, int bottom, int expected)
    {
        var height = bottom - top;
        if (height < 16) return -1;
        var minOverlap = Math.Max(12, height / 6);
        var step = height > 300 ? 2 : 1;
        var errors = new double[height - minOverlap];
        for (var shift = 0; shift < errors.Length; shift++)
        {
            long sum = 0;
            var n = 0;
            for (var i = 0; i < height - shift; i += step)
            {
                var y = top + i;
                if (currentPlain[y]) continue;
                var a = y * Slices;
                var b = (y + shift) * Slices;
                for (var k = 0; k < Slices; k++) sum += Math.Abs(current[a + k] - previous[b + k]);
                n++;
            }
            errors[shift] = n < 6 ? double.MaxValue : sum / (n * (double)Slices);
        }

        var min = errors.Min();
        // Brightness is ×4 (0…1020). Sharp text drawn half a pixel off differs quite a bit, so the
        // test is mostly relative: the best shift must fit far better than not moving at all.
        const double cap = 80;
        if (min > cap) return -1;
        if (errors[0] <= min * 1.15 + 1) return 0; // staying put fits as well: nothing moved
        if (errors[0] < min * 4) return -1;        // moved, but not clearly: do not guess

        // Several dips about as good (a repeating list): the one closest to the last shift.
        // Only true dips count, not the neighbours of the best one.
        var best = -1;
        var bestDistance = int.MaxValue;
        for (var shift = 1; shift < errors.Length; shift++)
        {
            if (errors[shift] > min * 1.1 + 0.5) continue;
            if (errors[shift] > errors[shift - 1] || (shift + 1 < errors.Length && errors[shift] > errors[shift + 1])) continue;
            var distance = expected > 0 ? Math.Abs(shift - expected) : shift;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = shift;
            }
        }
        return best;
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
