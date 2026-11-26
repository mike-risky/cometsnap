using System.Runtime.InteropServices;
using Cometsnap.Core.Geometry;
using Cometsnap.Core.Native;

namespace Cometsnap.Core.Capture;

public sealed record WindowInfo(IntPtr Handle, PixelRect Bounds, string Title, string ClassName);

/// <summary>
/// Takes a Z-ordered snapshot of the visible top-level windows at capture time, using the
/// true DWM frame bounds (no invisible Windows 10/11 resize borders, no shadow).
/// </summary>
public static class WindowFinder
{
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_InputSwitchTopLevelWindow", "Windows.UI.Core.CoreWindow",
    };

    /// <summary>Visible windows, topmost first. Windows of <paramref name="ownProcessId"/> are skipped.</summary>
    public static IReadOnlyList<WindowInfo> Snapshot(uint ownProcessId)
    {
        var result = new List<WindowInfo>(64);
        var classBuf = new char[256];
        var titleBuf = new char[256];

        Win32.EnumWindows((hwnd, _) =>
        {
            try
            {
                if (!Win32.IsWindowVisible(hwnd) || Win32.IsIconic(hwnd)) return true;

                Win32.GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == ownProcessId) return true;

                if (Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return true;

                var exStyle = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
                if ((exStyle & Win32.WS_EX_TRANSPARENT) != 0) return true;

                var len = Win32.GetClassName(hwnd, classBuf, classBuf.Length);
                var cls = new string(classBuf, 0, Math.Max(0, len));
                if (IgnoredClasses.Contains(cls)) return true;

                var bounds = GetBounds(hwnd);
                if (bounds.Width < 2 || bounds.Height < 2) return true;

                var tlen = Win32.GetWindowText(hwnd, titleBuf, titleBuf.Length);
                result.Add(new WindowInfo(hwnd, bounds, new string(titleBuf, 0, Math.Max(0, tlen)), cls));
            }
            catch
            {
                // a window vanishing mid-enumeration is fine
            }
            return true;
        }, IntPtr.Zero);

        return result;
    }

    public static PixelRect GetBounds(IntPtr hwnd)
    {
        if (Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>()) == 0
            && r.Width > 0 && r.Height > 0)
            return Monitors.ToRect(r);
        return Win32.GetWindowRect(hwnd, out r) ? Monitors.ToRect(r) : default;
    }

    /// <summary>The topmost window containing the point (windows are in Z-order).</summary>
    public static WindowInfo? HitTest(IReadOnlyList<WindowInfo> windows, int x, int y)
    {
        foreach (var w in windows)
            if (w.Bounds.Contains(x, y)) return w;
        return null;
    }
}
