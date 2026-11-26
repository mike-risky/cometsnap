using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Cometsnap.Linux.Platform;

internal static partial class X11
{
    [LibraryImport("libXext.so.6")]
    private static partial void XShapeCombineRectangles(IntPtr display, IntPtr window, int destKind, int xOff, int yOff,
        IntPtr rectangles, int count, int op, int ordering);

    /// <summary>
    /// Lets clicks pass through one of our windows (meteor showers, the flying comet, the drag thumbnail):
    /// an empty input shape. Harmless where XShape is missing.
    /// </summary>
    public static void MakeClickThrough(Window window)
    {
        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle is null || handle.HandleDescriptor != "XID") return;
            var d = Helper;
            if (d == IntPtr.Zero) return;
            lock (HelperGate)
            {
                XShapeCombineRectangles(d, handle.Handle, 2 /* ShapeInput */, 0, 0, IntPtr.Zero, 0, 0 /* ShapeSet */, 0 /* Unsorted */);
                XFlush(d);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // No XShape: the window simply stays clickable for its short life.
        }
    }
}
