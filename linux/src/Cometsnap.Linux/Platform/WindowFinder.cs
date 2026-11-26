using System.Runtime.InteropServices;
using System.Text;
using Cometsnap.Linux.Core;
using PixelRect = Cometsnap.Linux.Core.PixelRect;

namespace Cometsnap.Linux.Platform;

public sealed record WindowInfo(IntPtr Handle, PixelRect Bounds, string Title);

/// <summary>
/// Lists the visible top-level windows, top-most first, with their real visible bounds:
/// the window manager frame included (title bar), client-side shadows excluded.
/// X11 only: Wayland does not let apps see other windows.
/// </summary>
public static unsafe class WindowFinder
{
    private static IntPtr _display;
    private static readonly object Gate = new();

    public static bool IsAvailable => !DesktopSession.IsWayland || ScreenGrabber.ForceX11;

    public static IReadOnlyList<WindowInfo> List(PixelRect desktop)
    {
        if (!IsAvailable) return Array.Empty<WindowInfo>();
        lock (Gate)
        {
            try { return ListCore(desktop); }
            catch (Exception ex)
            {
                Log.Error("Listing windows failed", ex);
                return Array.Empty<WindowInfo>();
            }
        }
    }

    private static IReadOnlyList<WindowInfo> ListCore(PixelRect desktop)
    {
        if (_display == IntPtr.Zero) _display = X11.XOpenDisplay(IntPtr.Zero);
        var d = _display;
        if (d == IntPtr.Zero) return Array.Empty<WindowInfo>();
        var root = X11.XDefaultRootWindow(d);

        IntPtr Atom(string name) => X11.XInternAtom(d, name, false);
        var stacking = X11.GetWindowListProperty(d, root, Atom("_NET_CLIENT_LIST_STACKING"));
        if (stacking.Length == 0) stacking = X11.GetWindowListProperty(d, root, Atom("_NET_CLIENT_LIST"));

        var currentDesktop = X11.GetCardinals(d, root, Atom("_NET_CURRENT_DESKTOP")) is { Length: > 0 } cd ? cd[0] : -1;
        var aState = Atom("_NET_WM_STATE");
        var aHidden = Atom("_NET_WM_STATE_HIDDEN");
        var aType = Atom("_NET_WM_WINDOW_TYPE");
        var aTypeDesktop = Atom("_NET_WM_WINDOW_TYPE_DESKTOP");
        var aDesktop = Atom("_NET_WM_DESKTOP");
        var aFrame = Atom("_NET_FRAME_EXTENTS");
        var aGtkFrame = Atom("_GTK_FRAME_EXTENTS");
        var aPid = Atom("_NET_WM_PID");
        var aName = Atom("_NET_WM_NAME");
        var myPid = Environment.ProcessId;

        var result = new List<WindowInfo>();
        for (var i = stacking.Length - 1; i >= 0; i--)
        {
            var w = stacking[i];
            if (X11.XGetWindowAttributes(d, w, out var attr) == 0 || attr.map_state != X11.IsViewable) continue;
            if (X11.GetWindowListProperty(d, w, aState).Contains(aHidden)) continue;
            if (X11.GetWindowListProperty(d, w, aType).Contains(aTypeDesktop)) continue;
            var desk = X11.GetCardinals(d, w, aDesktop);
            if (currentDesktop >= 0 && desk.Length > 0 && desk[0] != currentDesktop && desk[0] != 0xFFFFFFFF) continue;
            if (X11.GetCardinals(d, w, aPid) is { Length: > 0 } pid && pid[0] == myPid) continue;

            if (!X11.XTranslateCoordinates(d, w, root, 0, 0, out var x, out var y, out _)) continue;
            int left = x, top = y, right = x + attr.width, bottom = y + attr.height;

            // Server-side decorations: the frame around the client.
            if (X11.GetCardinals(d, w, aFrame) is { Length: 4 } f)
            {
                left -= (int)f[0]; right += (int)f[1]; top -= (int)f[2]; bottom += (int)f[3];
            }
            // Client-side decorations (GTK): an invisible shadow margin inside the window.
            if (X11.GetCardinals(d, w, aGtkFrame) is { Length: 4 } g)
            {
                left += (int)g[0]; right -= (int)g[1]; top += (int)g[2]; bottom -= (int)g[3];
            }

            var rect = PixelRect.FromEdges(left, top, right, bottom).Intersect(desktop.IsEmpty ? PixelRect.FromEdges(-100000, -100000, 100000, 100000) : desktop);
            if (rect.Width < 8 || rect.Height < 8) continue;
            result.Add(new WindowInfo(w, rect, ReadString(d, w, aName)));
        }
        return result;
    }

    private static string ReadString(IntPtr d, IntPtr w, IntPtr atom)
    {
        if (X11.XGetWindowProperty(d, w, atom, 0, 1024, false, IntPtr.Zero, out _, out var format, out var n, out _, out var prop) != X11.Success
            || prop == IntPtr.Zero)
            return "";
        try
        {
            return format == 8 && n > 0 ? Encoding.UTF8.GetString((byte*)prop, (int)n) : "";
        }
        finally { X11.XFree(prop); }
    }

    /// <summary>The top-most window containing a desktop point.</summary>
    public static WindowInfo? At(IReadOnlyList<WindowInfo> windows, int x, int y)
    {
        foreach (var w in windows)
            if (w.Bounds.Contains(x, y)) return w;
        return null;
    }

    /// <summary>The window that has the keyboard focus (_NET_ACTIVE_WINDOW), if listed.</summary>
    public static WindowInfo? Active(IReadOnlyList<WindowInfo> windows)
    {
        lock (Gate)
        {
            if (_display == IntPtr.Zero) return null;
            var root = X11.XDefaultRootWindow(_display);
            var active = X11.GetWindowListProperty(_display, root, X11.XInternAtom(_display, "_NET_ACTIVE_WINDOW", false));
            if (active.Length == 0) return null;
            return windows.FirstOrDefault(w => w.Handle == active[0]);
        }
    }
}
