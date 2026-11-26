

namespace Cometsnap.Linux.Core;

/// <summary>
/// Decides where the magnifier loupe sits. It lives in a corner of the monitor under the
/// cursor and jumps to another corner as soon as the cursor comes close, so it never
/// hides the pixels you are aiming at. It only returns to the preferred corner once the
/// cursor has clearly moved away again (no flicker at the boundary).
/// </summary>
public static class LoupePlacement
{
    public const int Margin = 16;
    public const int AvoidDistance = 48;
    public const int ReturnHysteresis = 64;
    public const int FollowOffset = 28;

    public static PixelRect RectFor(LoupeCorner corner, PixelRect monitor, int width, int height, int margin = Margin) =>
        corner switch
        {
            LoupeCorner.TopLeft => new(monitor.X + margin, monitor.Y + margin, width, height),
            LoupeCorner.TopRight => new(monitor.Right - margin - width, monitor.Y + margin, width, height),
            LoupeCorner.BottomRight => new(monitor.Right - margin - width, monitor.Bottom - margin - height, width, height),
            _ => new(monitor.X + margin, monitor.Bottom - margin - height, width, height),
        };

    /// <summary>
    /// Picks the corner to use now. <paramref name="current"/> is the corner used for the
    /// previous mouse position (pass <paramref name="preferred"/> the first time).
    /// </summary>
    public static LoupeCorner ResolveCorner(LoupeCorner preferred, LoupeCorner current, PixelRect monitor,
        int width, int height, int cursorX, int cursorY)
    {
        if (preferred == LoupeCorner.FollowCursor) return LoupeCorner.FollowCursor;

        bool Near(LoupeCorner c, int extra) =>
            RectFor(c, monitor, width, height).Inflate(AvoidDistance + extra).Contains(cursorX, cursorY);

        // Still hovering around the preferred corner: stay where we are if that spot is safe.
        if (current != preferred && current != LoupeCorner.FollowCursor
            && Near(preferred, ReturnHysteresis) && !Near(current, 0))
            return current;

        foreach (var c in Candidates(preferred))
            if (!Near(c, 0)) return c;

        return preferred;
    }

    /// <summary>Rectangle for "follow cursor" mode: beside the cursor, flipped near monitor edges.</summary>
    public static PixelRect FollowRect(PixelRect monitor, int width, int height, int cursorX, int cursorY)
    {
        var x = cursorX + FollowOffset;
        var y = cursorY + FollowOffset;
        if (x + width > monitor.Right - Margin) x = cursorX - FollowOffset - width;
        if (y + height > monitor.Bottom - Margin) y = cursorY - FollowOffset - height;
        return new PixelRect(x, y, width, height).KeepInside(monitor);
    }

    /// <summary>Preferred first, then the horizontal neighbour, the vertical one, and the diagonal.</summary>
    public static IEnumerable<LoupeCorner> Candidates(LoupeCorner preferred)
    {
        yield return preferred;
        yield return FlipHorizontal(preferred);
        yield return FlipVertical(preferred);
        yield return FlipVertical(FlipHorizontal(preferred));
    }

    private static LoupeCorner FlipHorizontal(LoupeCorner c) => c switch
    {
        LoupeCorner.TopLeft => LoupeCorner.TopRight,
        LoupeCorner.TopRight => LoupeCorner.TopLeft,
        LoupeCorner.BottomRight => LoupeCorner.BottomLeft,
        _ => LoupeCorner.BottomRight,
    };

    private static LoupeCorner FlipVertical(LoupeCorner c) => c switch
    {
        LoupeCorner.TopLeft => LoupeCorner.BottomLeft,
        LoupeCorner.TopRight => LoupeCorner.BottomRight,
        LoupeCorner.BottomRight => LoupeCorner.TopRight,
        _ => LoupeCorner.TopLeft,
    };
}
