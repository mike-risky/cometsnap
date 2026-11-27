using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Cometsnap.App.Services;

/// <summary>
/// Keeps a small card (Copied, color toast) fully inside the bottom-right corner of the work area
/// of the monitor you are working on. Works in physical pixels, so mixed scaling (a 150% laptop next
/// to a 100% screen) can't push it past the edge, and it stays anchored when its content grows
/// (for example when "saving…" turns into the file name).
/// </summary>
internal sealed class CornerPlacement
{
    private readonly Window _window;
    private readonly int _marginDip;
    private RECT? _work;     // work area (pixels) the window is anchored to
    private bool _placing;

    public CornerPlacement(Window window, int marginDip = 4)
    {
        _window = window;
        _marginDip = marginDip;
        // Content changed size (longer text, a button shown or hidden, a new DPI): re-anchor.
        window.SizeChanged += (_, _) => { if (!_placing && _work is not null && window.IsVisible) Apply(); };
    }

    /// <summary>Anchors the window to the bottom-right of the monitor under the mouse.</summary>
    public void PlaceNearCursor()
    {
        GetCursorPos(out var pt);
        PlaceOnMonitor(MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST));
    }

    private void PlaceOnMonitor(IntPtr monitor)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            // Should never happen; fall back to the primary work area in WPF units.
            var wa = SystemParameters.WorkArea;
            _work = null;
            _window.Left = wa.Right - _window.ActualWidth - _marginDip;
            _window.Top = wa.Bottom - _window.ActualHeight - _marginDip;
            return;
        }
        _work = info.rcWork;
        Apply();
    }

    private void Apply()
    {
        if (_work is not { } work) return;
        var hwnd = new WindowInteropHelper(_window).EnsureHandle();
        _placing = true;
        try
        {
            // Moving onto a monitor with another scale makes Windows rescale the window, and WPF may
            // move it again while doing so: repeat until it sits where we want it.
            for (var i = 0; i < 4; i++)
            {
                _window.UpdateLayout();
                if (!GetWindowRect(hwnd, out var r)) return;
                var w = r.Right - r.Left;
                var h = r.Bottom - r.Top;
                var dpi = GetDpiForWindow(hwnd);
                var margin = (int)Math.Round(_marginDip * (dpi == 0 ? 96 : dpi) / 96.0);
                var x = Math.Max(work.Left, work.Right - w - margin);
                var y = Math.Max(work.Top, work.Bottom - h - margin);
                if (r.Left == x && r.Top == y) return;
                SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            }
        }
        finally { _placing = false; }
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
