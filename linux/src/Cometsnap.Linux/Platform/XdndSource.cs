using System.Runtime.InteropServices;
using System.Text;
using Cometsnap.Linux.Core;

namespace Cometsnap.Linux.Platform;

/// <summary>
/// An XDND drag source (freedesktop.org drag-and-drop, version 5), so a capture can be dropped
/// into any X11 or XWayland app: file managers, browsers, chat apps, office suites.
/// Avalonia has no outgoing drag on Linux, so we speak the protocol ourselves on a private
/// display connection. The drag follows the mouse button that is already held down (the press
/// happened in our window), polling the pointer until it is released.
/// </summary>
internal static unsafe partial class XdndSource
{
    private const string Lib = "libX11.so.6";
    private const int ClientMessage = 33, SelectionRequest = 30, PropertyNotify = 28;
    private const long PropertyChangeMask = 1L << 22;
    private const uint Button1Mask = 1 << 8;
    private const int XdndVersion = 5;
    private const int PropModeReplace = 0, PropModeAppend = 2;
    private const int MaxPropertyBytes = 12 * 1024 * 1024;
    private static readonly bool Debug = Environment.GetEnvironmentVariable("COMETSNAP_DEBUG_DND") == "1";

    private static void Trace(string text)
    {
        if (Debug) Log.Info("XDND " + text);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawEvent
    {
        public fixed long W[24];
    }

    [LibraryImport(Lib)] private static partial IntPtr XCreateSimpleWindow(IntPtr d, IntPtr parent, int x, int y, uint w, uint h, uint border, ulong borderPixel, ulong background);
    [LibraryImport(Lib)] private static partial int XDestroyWindow(IntPtr d, IntPtr w);
    [LibraryImport(Lib)] private static partial int XSetSelectionOwner(IntPtr d, IntPtr selection, IntPtr owner, ulong time);
    [LibraryImport(Lib)] private static partial int XChangeProperty(IntPtr d, IntPtr w, IntPtr property, IntPtr type, int format, int mode, byte* data, int count);
    [LibraryImport(Lib)] private static partial int XSendEvent(IntPtr d, IntPtr w, [MarshalAs(UnmanagedType.Bool)] bool propagate, long mask, RawEvent* ev);
    [LibraryImport(Lib, EntryPoint = "XNextEvent")] private static partial int NextEvent(IntPtr d, RawEvent* ev);

    /// <summary>What the drag offers.</summary>
    public sealed record Payload(string? FilePath, Func<byte[]?>? Png);

    /// <summary>
    /// Runs one drag on a background thread. <paramref name="moved"/> gets the pointer position
    /// (desktop pixels) at every step; the task ends with true when an app took the drop.
    /// </summary>
    public static Task<bool> RunAsync(Payload payload, Action<int, int> moved)
    {
        var done = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try { done.SetResult(Run(payload, moved)); }
            catch (Exception ex)
            {
                Log.Error("Drag and drop failed", ex);
                done.TrySetResult(false);
            }
        }) { IsBackground = true, Name = "XDND drag" };
        thread.Start();
        return done.Task;
    }

    private sealed class Atoms
    {
        public IntPtr Aware, Proxy, Selection, Enter, Position, Status, Leave, Drop, Finished, ActionCopy, TypeList;
        public IntPtr Targets, UriList, Png, TextPlain, TextPlainUtf8, Utf8String, AtomType, Timestamp;
    }

    private static bool Run(Payload payload, Action<int, int> moved)
    {
        var d = X11.XOpenDisplay(IntPtr.Zero);
        if (d == IntPtr.Zero) return false;
        var root = X11.XDefaultRootWindow(d);
        var a = new Atoms();
        IntPtr Atom(string name) => X11.XInternAtom(d, name, false);
        a.Aware = Atom("XdndAware"); a.Proxy = Atom("XdndProxy"); a.Selection = Atom("XdndSelection");
        a.Enter = Atom("XdndEnter"); a.Position = Atom("XdndPosition"); a.Status = Atom("XdndStatus");
        a.Leave = Atom("XdndLeave"); a.Drop = Atom("XdndDrop"); a.Finished = Atom("XdndFinished");
        a.ActionCopy = Atom("XdndActionCopy"); a.TypeList = Atom("XdndTypeList");
        a.Targets = Atom("TARGETS"); a.UriList = Atom("text/uri-list"); a.Png = Atom("image/png");
        a.TextPlain = Atom("text/plain"); a.TextPlainUtf8 = Atom("text/plain;charset=utf-8"); a.Utf8String = Atom("UTF8_STRING");
        a.AtomType = (IntPtr)4; /* XA_ATOM */ a.Timestamp = Atom("TIMESTAMP");

        var hasFile = payload.FilePath is not null && File.Exists(payload.FilePath);
        var types = new List<IntPtr>();
        if (hasFile) types.Add(a.UriList);
        if (payload.Png is not null) types.Add(a.Png);
        if (hasFile) { types.Add(a.TextPlainUtf8); types.Add(a.TextPlain); types.Add(a.Utf8String); }
        if (types.Count == 0) { X11.XCloseDisplay(d); return false; }

        byte[]? png = null;
        var pngLoaded = false;
        byte[]? Bytes(IntPtr target)
        {
            if (target == a.UriList) return Encoding.UTF8.GetBytes(new Uri(payload.FilePath!).AbsoluteUri + "\r\n");
            if (target == a.TextPlain || target == a.TextPlainUtf8 || target == a.Utf8String) return Encoding.UTF8.GetBytes(payload.FilePath!);
            if (target == a.Png)
            {
                if (!pngLoaded) { pngLoaded = true; png = payload.Png?.Invoke(); }
                return png is { Length: <= MaxPropertyBytes } ? png : null;
            }
            return null;
        }

        var source = XCreateSimpleWindow(d, root, -100, -100, 1, 1, 0, 0, 0);
        try
        {
            X11.XSelectInput(d, source, PropertyChangeMask);
            var typeList = types.ToArray();
            fixed (IntPtr* t = typeList)
                XChangeProperty(d, source, a.TypeList, a.AtomType, 32, PropModeReplace, (byte*)t, typeList.Length);
            var time = ServerTime(d, source, a.Timestamp);
            XSetSelectionOwner(d, a.Selection, source, time);
            X11.XFlush(d);

            IntPtr target = IntPtr.Zero, sendTo = IntPtr.Zero;
            var accepted = false;
            var waitingStatus = false;
            var lastX = int.MinValue;
            var lastY = int.MinValue;
            var positionDirty = false;
            var finished = false;
            var deadline = DateTime.MaxValue;
            var dropped = false;

            void Send(IntPtr to, IntPtr type, long l0, long l1, long l2, long l3, long l4, IntPtr window)
            {
                var ev = new RawEvent();
                ev.W[0] = ClientMessage;
                ev.W[4] = (long)window;
                ev.W[5] = (long)type;
                ev.W[6] = 32;
                ev.W[7] = l0; ev.W[8] = l1; ev.W[9] = l2; ev.W[10] = l3; ev.W[11] = l4;
                XSendEvent(d, to, false, 0, &ev);
            }

            void Pump()
            {
                while (X11.XPending(d) > 0)
                {
                    var ev = new RawEvent();
                    NextEvent(d, &ev);
                    var type = (int)ev.W[0];
                    if (type == ClientMessage)
                    {
                        var msg = (IntPtr)ev.W[5];
                        Trace($"message {msg} from {ev.W[7]:x} flags {ev.W[8]}");
                        if (msg == a.Status && (IntPtr)ev.W[7] == target)
                        {
                            waitingStatus = false;
                            accepted = (ev.W[8] & 1) != 0;
                        }
                        else if (msg == a.Finished)
                        {
                            finished = true;
                        }
                    }
                    else if (type == SelectionRequest)
                    {
                        Trace($"selection request for atom {ev.W[7]}");
                        Answer(d, &ev, a, typeList, Bytes);
                    }
                }
            }

            while (true)
            {
                Pump();
                if (X11.XQueryPointer(d, root, out _, out _, out var x, out var y, out _, out _, out var mask) == 0) break;
                if (x != lastX || y != lastY)
                {
                    lastX = x;
                    lastY = y;
                    moved(x, y);
                    positionDirty = true;
                }

                var (found, proxy, version) = FindTarget(d, root, x, y, a, source);
                if (found != target)
                {
                    Trace($"target {target:x} -> {found:x} (via {proxy:x}, version {version})");
                    if (target != IntPtr.Zero) Send(sendTo, a.Leave, (long)source, 0, 0, 0, 0, target);
                    target = found;
                    sendTo = proxy;
                    accepted = false;
                    waitingStatus = false;
                    if (target != IntPtr.Zero)
                    {
                        long flags = ((long)Math.Min(version, XdndVersion) << 24) | (typeList.Length > 3 ? 1L : 0L);
                        Send(sendTo, a.Enter, (long)source, flags,
                            (long)typeList[0], typeList.Length > 1 ? (long)typeList[1] : 0, typeList.Length > 2 ? (long)typeList[2] : 0, target);
                        positionDirty = true;
                    }
                }
                if (target != IntPtr.Zero && positionDirty && !waitingStatus)
                {
                    Send(sendTo, a.Position, (long)source, 0, ((long)(x & 0xFFFF) << 16) | (long)(y & 0xFFFF), (long)time, (long)a.ActionCopy, target);
                    waitingStatus = true;
                    positionDirty = false;
                }
                X11.XFlush(d);

                if ((mask & Button1Mask) == 0)
                {
                    Trace($"released at {x},{y} over {target:x}, accepted {accepted}, waiting {waitingStatus}");
                    // Released: give the target a moment to answer the last position, then drop or leave.
                    var until = DateTime.UtcNow.AddMilliseconds(300);
                    while (target != IntPtr.Zero && waitingStatus && DateTime.UtcNow < until)
                    {
                        Pump();
                        Thread.Sleep(5);
                    }
                    if (target != IntPtr.Zero && accepted)
                    {
                        Send(sendTo, a.Drop, (long)source, 0, (long)time, 0, 0, target);
                        dropped = true;
                        deadline = DateTime.UtcNow.AddSeconds(6);
                    }
                    else if (target != IntPtr.Zero)
                    {
                        Send(sendTo, a.Leave, (long)source, 0, 0, 0, 0, target);
                    }
                    X11.XFlush(d);
                    break;
                }
                Thread.Sleep(16);
            }

            // Serve the data until the target says it is done (or gives up).
            while (dropped && !finished && DateTime.UtcNow < deadline)
            {
                Pump();
                X11.XFlush(d);
                Thread.Sleep(5);
            }
            if (dropped) Log.Info(finished ? "Dropped a capture into another app" : "Drop sent; the app did not confirm");
            return dropped;
        }
        finally
        {
            XDestroyWindow(d, source);
            X11.XCloseDisplay(d);
        }
    }

    /// <summary>A real server timestamp (some toolkits refuse CurrentTime): touch a property and read the event time.</summary>
    private static ulong ServerTime(IntPtr d, IntPtr window, IntPtr atom)
    {
        byte none = 0;
        XChangeProperty(d, window, atom, atom, 8, PropModeAppend, &none, 0);
        X11.XFlush(d);
        var until = DateTime.UtcNow.AddMilliseconds(500);
        while (DateTime.UtcNow < until)
        {
            if (X11.XPending(d) > 0)
            {
                var ev = new RawEvent();
                NextEvent(d, &ev);
                if ((int)ev.W[0] == PropertyNotify && (IntPtr)ev.W[4] == window) return (ulong)ev.W[6];
                continue;
            }
            Thread.Sleep(2);
        }
        return 0;
    }

    /// <summary>The deepest XdndAware window under the point (the client inside its frame), with its proxy and version.</summary>
    private static (IntPtr Target, IntPtr SendTo, int Version) FindTarget(IntPtr d, IntPtr root, int x, int y, Atoms a, IntPtr self)
    {
        var w = root;
        for (var depth = 0; depth < 12; depth++)
        {
            if (!X11.XTranslateCoordinates(d, root, w, x, y, out _, out _, out var child) || child == IntPtr.Zero) break;
            w = child;
            if (w == self) break;
            var proxy = ReadWindow(d, w, a.Proxy);
            var version = ReadAware(d, proxy != IntPtr.Zero ? proxy : w, a.Aware);
            if (version >= 3) return (w, proxy != IntPtr.Zero ? proxy : w, version);
        }
        return (IntPtr.Zero, IntPtr.Zero, 0);
    }

    private static int ReadAware(IntPtr d, IntPtr w, IntPtr atom)
    {
        if (X11.XGetWindowProperty(d, w, atom, 0, 1, false, IntPtr.Zero, out _, out var format, out var n, out _, out var prop) != X11.Success
            || prop == IntPtr.Zero) return 0;
        try { return format == 32 && n > 0 ? (int)*(long*)prop : 0; }
        finally { X11.XFree(prop); }
    }

    private static IntPtr ReadWindow(IntPtr d, IntPtr w, IntPtr atom)
    {
        if (X11.XGetWindowProperty(d, w, atom, 0, 1, false, IntPtr.Zero, out _, out var format, out var n, out _, out var prop) != X11.Success
            || prop == IntPtr.Zero) return IntPtr.Zero;
        try { return format == 32 && n > 0 ? (IntPtr)(*(long*)prop) : IntPtr.Zero; }
        finally { X11.XFree(prop); }
    }

    /// <summary>Hands the requested format to the drop target and tells it the data is there.</summary>
    private static void Answer(IntPtr d, RawEvent* req, Atoms a, IntPtr[] types, Func<IntPtr, byte[]?> bytes)
    {
        var requestor = (IntPtr)req->W[5];
        var selection = (IntPtr)req->W[6];
        var target = (IntPtr)req->W[7];
        var property = (IntPtr)req->W[8];
        var time = req->W[9];
        if (property == IntPtr.Zero) property = target; // obsolete clients
        var ok = false;
        if (target == a.Targets)
        {
            var list = new IntPtr[types.Length + 1];
            list[0] = a.Targets;
            Array.Copy(types, 0, list, 1, types.Length);
            fixed (IntPtr* p = list) XChangeProperty(d, requestor, property, a.AtomType, 32, PropModeReplace, (byte*)p, list.Length);
            ok = true;
        }
        else if (Array.IndexOf(types, target) >= 0 && bytes(target) is { } data)
        {
            fixed (byte* p = data) XChangeProperty(d, requestor, property, target, 8, PropModeReplace, p, data.Length);
            ok = true;
        }
        var ev = new RawEvent();
        ev.W[0] = 31; // SelectionNotify
        ev.W[4] = (long)requestor;
        ev.W[5] = (long)selection;
        ev.W[6] = (long)target;
        ev.W[7] = ok ? (long)property : 0;
        ev.W[8] = time;
        XSendEvent(d, requestor, false, 0, &ev);
        X11.XFlush(d);
    }
}
