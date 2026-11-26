using System.Runtime.InteropServices;

namespace Cometsnap.Linux.Platform;

/// <summary>Minimal libX11 interop: screen grabs, the window list and global hotkeys.</summary>
internal static unsafe partial class X11
{
    private const string Lib = "libX11.so.6";

    public const int ZPixmap = 2;
    public const ulong AllPlanes = ulong.MaxValue;
    public const int KeyPress = 2;
    public const int GrabModeAsync = 1;
    public const int IsViewable = 2;
    public const int Success = 0;
    public const int BadAccess = 10;

    public const uint ShiftMask = 1, LockMask = 2, ControlMask = 4, Mod1Mask = 8, Mod2Mask = 16, Mod4Mask = 64;

    [LibraryImport(Lib)] public static partial IntPtr XOpenDisplay(IntPtr name);
    [LibraryImport(Lib)] public static partial int XCloseDisplay(IntPtr display);
    [LibraryImport(Lib)] public static partial IntPtr XDefaultRootWindow(IntPtr display);
    [LibraryImport(Lib)] public static partial int XDefaultScreen(IntPtr display);
    [LibraryImport(Lib)] public static partial int XDisplayWidth(IntPtr display, int screen);
    [LibraryImport(Lib)] public static partial int XDisplayHeight(IntPtr display, int screen);
    [LibraryImport(Lib)] public static partial IntPtr XGetImage(IntPtr display, IntPtr drawable, int x, int y, uint width, uint height, ulong planeMask, int format);
    [LibraryImport(Lib)] public static partial int XFree(IntPtr data);
    [LibraryImport(Lib)] public static partial int XSync(IntPtr display, [MarshalAs(UnmanagedType.Bool)] bool discard);
    [LibraryImport(Lib)] public static partial int XFlush(IntPtr display);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] public static partial IntPtr XInternAtom(IntPtr display, string name, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

    [LibraryImport(Lib)]
    public static partial int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property, long offset, long length,
        [MarshalAs(UnmanagedType.Bool)] bool delete, IntPtr reqType, out IntPtr actualType, out int actualFormat,
        out ulong nItems, out ulong bytesAfter, out IntPtr prop);

    [LibraryImport(Lib)] public static partial int XGetWindowAttributes(IntPtr display, IntPtr window, out XWindowAttributes attributes);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool XTranslateCoordinates(IntPtr display, IntPtr src, IntPtr dest, int srcX, int srcY, out int destX, out int destY, out IntPtr child);

    [LibraryImport(Lib)] public static partial int XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int rootX, out int rootY, out int winX, out int winY, out uint mask);

    [LibraryImport(Lib)] public static partial byte XKeysymToKeycode(IntPtr display, ulong keysym);
    [LibraryImport(Lib)] public static partial int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow, [MarshalAs(UnmanagedType.Bool)] bool ownerEvents, int pointerMode, int keyboardMode);
    [LibraryImport(Lib)] public static partial int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow);
    [LibraryImport(Lib)] public static partial int XSelectInput(IntPtr display, IntPtr window, long mask);
    [LibraryImport(Lib)] public static partial int XNextEvent(IntPtr display, XEvent* ev);
    [LibraryImport(Lib)] public static partial int XPending(IntPtr display);
    [LibraryImport(Lib)] public static partial int XConnectionNumber(IntPtr display);
    [LibraryImport(Lib)] public static partial IntPtr XSetErrorHandler(IntPtr handler);
    [LibraryImport(Lib)] public static partial int XInitThreads();
    [LibraryImport(Lib)] public static partial int XSetInputFocus(IntPtr display, IntPtr window, int revertTo, ulong time);
    [LibraryImport(Lib)] public static partial int XRaiseWindow(IntPtr display, IntPtr window);
    [LibraryImport(Lib)] public static partial int XWarpPointer(IntPtr display, IntPtr srcWindow, IntPtr destWindow, int srcX, int srcY, uint srcWidth, uint srcHeight, int destX, int destY);

    private static IntPtr _helper;
    private static readonly object HelperGate = new();

    /// <summary>A private display connection for small one-off requests (focus, pointer).</summary>
    public static IntPtr Helper
    {
        get
        {
            lock (HelperGate)
            {
                if (_helper == IntPtr.Zero) _helper = XOpenDisplay(IntPtr.Zero);
                return _helper;
            }
        }
    }

    /// <summary>Gives the keyboard focus to one of our windows (overlays shown from a hotkey).</summary>
    public static void ForceFocus(IntPtr window)
    {
        var d = Helper;
        if (d == IntPtr.Zero || window == IntPtr.Zero) return;
        lock (HelperGate)
        {
            XRaiseWindow(d, window);
            XSetInputFocus(d, window, 2 /* RevertToParent */, 0 /* CurrentTime */);
            XFlush(d);
        }
    }

    /// <summary>Moves the mouse pointer by a few pixels (arrow-key precision in the overlay).</summary>
    public static void MovePointer(int dx, int dy)
    {
        var d = Helper;
        if (d == IntPtr.Zero) return;
        lock (HelperGate)
        {
            XWarpPointer(d, IntPtr.Zero, IntPtr.Zero, 0, 0, 0, 0, dx, dy);
            XFlush(d);
        }
    }

    /// <summary>The pointer position in root (desktop) coordinates.</summary>
    public static (int X, int Y)? PointerPosition()
    {
        var d = Helper;
        if (d == IntPtr.Zero) return null;
        lock (HelperGate)
        {
            XQueryPointer(d, XDefaultRootWindow(d), out _, out _, out var x, out var y, out _, out _, out _);
            return (x, y);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XWindowAttributes
    {
        public int x, y, width, height, border_width, depth;
        public IntPtr visual, root;
        public int c_class, bit_gravity, win_gravity, backing_store;
        public ulong backing_planes, backing_pixel;
        public int save_under;
        public IntPtr colormap;
        public int map_installed, map_state;
        public long all_event_masks, your_event_mask, do_not_propagate_mask;
        public int override_redirect;
        public IntPtr screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XKeyEvent
    {
        public int type;
        public ulong serial;
        public int send_event;
        public IntPtr display, window, root, subwindow;
        public ulong time;
        public int x, y, x_root, y_root;
        public uint state, keycode;
        public int same_screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XErrorEvent
    {
        public int type;
        public IntPtr display;
        public ulong resourceid, serial;
        public byte error_code, request_code, minor_code;
    }

    /// <summary>XEvent is a 24-long union.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24 * 8)]
    public struct XEvent
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(0)] public XKeyEvent xkey;
    }

    /// <summary>The fields of XImage we read (x86_64 / aarch64 layout).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XImage
    {
        public int width, height, xoffset, format;
        public IntPtr data;
        public int byte_order, bitmap_unit, bitmap_bit_order, bitmap_pad, depth, bytes_per_line, bits_per_pixel;
        public ulong red_mask, green_mask, blue_mask;
        public IntPtr obdata;
        public IntPtr create_image, destroy_image, get_pixel, put_pixel, sub_image, add_pixel;
    }

    public static void DestroyImage(IntPtr image)
    {
        var img = (XImage*)image;
        if (img->destroy_image != IntPtr.Zero)
            ((delegate* unmanaged<IntPtr, int>)img->destroy_image)(image);
    }

    // ------------------------------------------------------------------ properties

    public static IntPtr[] GetWindowListProperty(IntPtr display, IntPtr window, IntPtr atom)
    {
        if (XGetWindowProperty(display, window, atom, 0, 65536, false, IntPtr.Zero /* AnyPropertyType */,
                out _, out var format, out var n, out _, out var prop) != Success || prop == IntPtr.Zero)
            return Array.Empty<IntPtr>();
        try
        {
            if (format != 32) return Array.Empty<IntPtr>();
            var result = new IntPtr[(int)n];
            // Format-32 properties come back as C longs.
            for (var i = 0; i < (int)n; i++) result[i] = ((IntPtr*)prop)[i];
            return result;
        }
        finally { XFree(prop); }
    }

    public static long[] GetCardinals(IntPtr display, IntPtr window, IntPtr atom)
    {
        var list = GetWindowListProperty(display, window, atom);
        var r = new long[list.Length];
        for (var i = 0; i < list.Length; i++) r[i] = (long)list[i];
        return r;
    }
}
