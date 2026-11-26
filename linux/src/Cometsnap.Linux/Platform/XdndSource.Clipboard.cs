using System.Runtime.InteropServices;
using System.Text;
using Cometsnap.Linux.Core;

namespace Cometsnap.Linux.Platform;

/// <summary>
/// Copying files on X11: Cometsnap owns the CLIPBOARD selection itself and hands out the files as
/// text/uri-list (most apps), x-special/gnome-copied-files (GNOME and friends) and plain text.
/// Avalonia's own clipboard sends an empty uri-list, so file managers and chat apps got nothing.
/// One background connection serves the clipboard until another app copies something.
/// </summary>
internal static unsafe partial class XdndSource
{
    private const int SelectionClear = 29;

    [LibraryImport(Lib)] private static partial IntPtr XGetSelectionOwner(IntPtr d, IntPtr selection);
    private static readonly object ClipLock = new();
    private static Thread? _clipThread;
    private static string[]? _wantedFiles;     // set by CopyFiles, picked up by the thread
    private static TaskCompletionSource<bool>? _wantedDone;

    /// <summary>Puts <paramref name="paths"/> on the X11 clipboard. True once Cometsnap owns it.</summary>
    public static Task<bool> CopyFilesAsync(IReadOnlyList<string> paths)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (ClipLock)
        {
            _wantedDone?.TrySetResult(false);
            _wantedFiles = paths.ToArray();
            _wantedDone = done;
            if (_clipThread is null)
            {
                _clipThread = new Thread(ClipboardLoop) { IsBackground = true, Name = "X11 clipboard" };
                _clipThread.Start();
            }
        }
        return done.Task;
    }

    private static void ClipboardLoop()
    {
        var d = X11.XOpenDisplay(IntPtr.Zero);
        if (d == IntPtr.Zero)
        {
            lock (ClipLock) { _wantedDone?.TrySetResult(false); _clipThread = null; }
            return;
        }
        IntPtr Atom(string name) => X11.XInternAtom(d, name, false);
        var a = new Atoms
        {
            Targets = Atom("TARGETS"), UriList = Atom("text/uri-list"), TextPlain = Atom("text/plain"),
            TextPlainUtf8 = Atom("text/plain;charset=utf-8"), Utf8String = Atom("UTF8_STRING"), AtomType = (IntPtr)4,
            Timestamp = Atom("TIMESTAMP"),
        };
        var clipboard = Atom("CLIPBOARD");
        var gnome = Atom("x-special/gnome-copied-files");
        var types = new[] { a.UriList, gnome, a.TextPlainUtf8, a.TextPlain, a.Utf8String };
        var window = XCreateSimpleWindow(d, X11.XDefaultRootWindow(d), -100, -100, 1, 1, 0, 0, 0);
        X11.XSelectInput(d, window, PropertyChangeMask);
        string[]? files = null;
        ulong ownedSince = 0;

        byte[]? Bytes(IntPtr target)
        {
            if (files is null) return null;
            var uris = files.Select(f => new Uri(f).AbsoluteUri).ToArray();
            if (target == a.UriList) return Encoding.UTF8.GetBytes(string.Join("\r\n", uris) + "\r\n");
            if (target == gnome) return Encoding.UTF8.GetBytes("copy\n" + string.Join("\n", uris));
            if (target == a.TextPlain || target == a.TextPlainUtf8 || target == a.Utf8String) return Encoding.UTF8.GetBytes(string.Join("\n", files));
            return null;
        }

        try
        {
            while (true)
            {
                string[]? wanted;
                TaskCompletionSource<bool>? done;
                lock (ClipLock)
                {
                    wanted = _wantedFiles;
                    done = _wantedDone;
                    _wantedFiles = null;
                    _wantedDone = null;
                }
                if (wanted is not null)
                {
                    files = wanted;
                    ownedSince = ServerTime(d, window, a.Timestamp);
                    XSetSelectionOwner(d, clipboard, window, ownedSince);
                    X11.XFlush(d);
                    var ok = XGetSelectionOwner(d, clipboard) == window;
                    if (!ok) files = null;
                    done?.TrySetResult(ok);
                }

                while (X11.XPending(d) > 0)
                {
                    var ev = new RawEvent();
                    NextEvent(d, &ev);
                    var type = (int)ev.W[0];
                    if (type == SelectionRequest && (IntPtr)ev.W[6] == clipboard)
                    {
                        if (files is null) Answer(d, &ev, a, Array.Empty<IntPtr>(), _ => null);
                        else Answer(d, &ev, a, types, Bytes);
                    }
                    else if (type == SelectionClear && (IntPtr)ev.W[4] == window)
                    {
                        files = null; // another app copied something
                    }
                }
                Thread.Sleep(15);
            }
        }
        catch (Exception ex)
        {
            Log.Error("X11 clipboard stopped", ex);
            lock (ClipLock) { _wantedDone?.TrySetResult(false); _clipThread = null; }
        }
    }
}
