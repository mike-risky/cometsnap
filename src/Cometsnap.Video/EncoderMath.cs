namespace Cometsnap.Video;

/// <summary>Small pure helpers, kept separate so they can be unit tested.</summary>
internal static class EncoderMath
{
    /// <summary>H.264 wants even dimensions: drop the last odd row/column.</summary>
    public static ScreenRect EvenSize(ScreenRect r) =>
        r with { Width = Math.Max(2, r.Width & ~1), Height = Math.Max(2, r.Height & ~1) };

    /// <summary>About 0.1 bit per pixel per frame works well for screen content, clamped to a sane range.</summary>
    public static int DefaultBitrate(int width, int height, int fps)
    {
        var bits = (long)width * height * Math.Max(1, fps) / 10;
        return (int)Math.Clamp(bits, 1_500_000, 40_000_000);
    }

    /// <summary>
    /// The H.264 encoders shipped with Windows top out around 4096 × 2304. Bigger regions are
    /// halved (in both directions) until they fit.
    /// </summary>
    public static int DownscaleFactor(int width, int height)
    {
        var f = 1;
        while (width / f > 4096 || height / f > 2304) f *= 2;
        return f;
    }

    /// <summary>Converts a time in seconds to Media Foundation's 100-nanosecond units.</summary>
    public static long ToHns(double seconds) => (long)Math.Round(seconds * 10_000_000.0);

    /// <summary>
    /// GIF delays are in hundredths of a second. Rounding each frame on its own drifts, so the
    /// delay is the difference of the rounded absolute times. Browsers treat delays under 2 as 10,
    /// so never go below 2.
    /// </summary>
    public static int GifDelay(TimeSpan frameStart, TimeSpan nextFrameStart)
    {
        var a = (long)Math.Round(frameStart.TotalMilliseconds / 10.0);
        var b = (long)Math.Round(nextFrameStart.TotalMilliseconds / 10.0);
        return (int)Math.Clamp(b - a, 2, 65535);
    }
}
