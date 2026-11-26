using System.Runtime.InteropServices;
using Cometsnap.Linux.Core;

namespace Cometsnap.Linux.Platform;

/// <summary>
/// Global hotkeys on X11 with XGrabKey, on a private display connection and thread.
/// Each gesture is grabbed with every NumLock / CapsLock combination so it works whatever the lock keys.
/// </summary>
public sealed unsafe class X11Hotkeys : IDisposable
{
    private static readonly uint[] LockCombos = { 0, X11.LockMask, X11.Mod2Mask, X11.LockMask | X11.Mod2Mask };

    private readonly Dictionary<(uint Keycode, uint Mods), string> _bindings = new();
    private IntPtr _display;
    private Thread? _thread;
    private volatile bool _stop;
    private static int _lastError;

    public event Action<string>? Pressed;

    /// <summary>Grabs <paramref name="gesture"/>. False when another app (or the desktop) already owns it.</summary>
    public bool Register(string id, HotkeyGesture gesture)
    {
        if (_display == IntPtr.Zero)
        {
            X11.XInitThreads();
            _display = X11.XOpenDisplay(IntPtr.Zero);
            if (_display == IntPtr.Zero) return false;
        }
        var root = X11.XDefaultRootWindow(_display);
        var keycode = X11.XKeysymToKeycode(_display, gesture.KeySym);
        if (keycode == 0) return false;
        var mods = ToX11(gesture.Modifiers);

        // Catch BadAccess (key already grabbed) without disturbing anyone else's error handler.
        _lastError = 0;
        var previous = X11.XSetErrorHandler((IntPtr)(delegate* unmanaged<IntPtr, X11.XErrorEvent*, int>)&OnError);
        foreach (var l in LockCombos) X11.XGrabKey(_display, keycode, mods | l, root, false, X11.GrabModeAsync, X11.GrabModeAsync);
        X11.XSync(_display, false);
        X11.XSetErrorHandler(previous);

        if (_lastError != 0)
        {
            foreach (var l in LockCombos) X11.XUngrabKey(_display, (int)keycode, mods | l, root);
            X11.XSync(_display, false);
            Log.Info($"Hotkey {gesture} is taken by another program");
            return false;
        }
        _bindings[(keycode, mods)] = id;
        Log.Info($"Hotkey {gesture} → {id}");
        return true;
    }

    [UnmanagedCallersOnly]
    private static int OnError(IntPtr display, X11.XErrorEvent* e)
    {
        _lastError = e->error_code;
        return 0;
    }

    public void Start()
    {
        if (_display == IntPtr.Zero || _thread is not null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "X11 hotkeys" };
        _thread.Start();
    }

    private void Loop()
    {
        var fd = X11.XConnectionNumber(_display);
        X11.XEvent ev;
        while (!_stop)
        {
            if (X11.XPending(_display) == 0)
            {
                // Wait for the X socket to become readable (or 500 ms, to notice Stop).
                var pfd = new PollFd { fd = fd, events = 1 };
                poll(&pfd, 1, 500);
                continue;
            }
            X11.XNextEvent(_display, &ev);
            if (ev.type != X11.KeyPress) continue;
            var mods = ev.xkey.state & (X11.ShiftMask | X11.ControlMask | X11.Mod1Mask | X11.Mod4Mask);
            if (_bindings.TryGetValue((ev.xkey.keycode, mods), out var id))
            {
                try { Pressed?.Invoke(id); }
                catch (Exception ex) { Log.Error("Hotkey handler failed", ex); }
            }
        }
    }

    private static uint ToX11(HotkeyModifiers m)
    {
        uint r = 0;
        if (m.HasFlag(HotkeyModifiers.Shift)) r |= X11.ShiftMask;
        if (m.HasFlag(HotkeyModifiers.Ctrl)) r |= X11.ControlMask;
        if (m.HasFlag(HotkeyModifiers.Alt)) r |= X11.Mod1Mask;
        if (m.HasFlag(HotkeyModifiers.Super)) r |= X11.Mod4Mask;
        return r;
    }

    public void UnregisterAll()
    {
        if (_display == IntPtr.Zero) return;
        var root = X11.XDefaultRootWindow(_display);
        foreach (var (keycode, mods) in _bindings.Keys)
            foreach (var l in LockCombos) X11.XUngrabKey(_display, (int)keycode, mods | l, root);
        X11.XFlush(_display);
        _bindings.Clear();
    }

    public void Dispose()
    {
        _stop = true;
        UnregisterAll();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int fd;
        public short events, revents;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int poll(PollFd* fds, ulong nfds, int timeout);
}
