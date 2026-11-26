using System.Runtime.InteropServices;

namespace Cometsnap.Video.Capture;

/// <summary>Draws the current mouse cursor into a frame (screen-space origin given).</summary>
internal sealed class CursorPainter
{
    private IntPtr _cursor;
    private int _hotX, _hotY;

    public void Paint(FrameCanvas canvas, int originX, int originY)
    {
        var info = new Native.CURSORINFO { cbSize = Marshal.SizeOf<Native.CURSORINFO>() };
        if (!Native.GetCursorInfo(ref info) || (info.flags & Native.CURSOR_SHOWING) == 0 || info.hCursor == IntPtr.Zero)
            return;

        if (info.hCursor != _cursor)
        {
            _cursor = info.hCursor;
            _hotX = _hotY = 0;
            if (Native.GetIconInfo(info.hCursor, out var icon))
            {
                _hotX = icon.xHotspot;
                _hotY = icon.yHotspot;
                if (icon.hbmMask != IntPtr.Zero) Native.DeleteObject(icon.hbmMask);
                if (icon.hbmColor != IntPtr.Zero) Native.DeleteObject(icon.hbmColor);
            }
        }

        var x = info.ptScreenPos.X - _hotX - originX;
        var y = info.ptScreenPos.Y - _hotY - originY;
        if (x > canvas.Width || y > canvas.Height || x < -256 || y < -256) return;
        Native.DrawIconEx(canvas.Hdc, x, y, info.hCursor, 0, 0, 0, IntPtr.Zero, Native.DI_NORMAL);
        Native.GdiFlush();
    }
}
