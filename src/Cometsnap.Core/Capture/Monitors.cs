using System.Runtime.InteropServices;
using Cometsnap.Core.Geometry;
using Cometsnap.Core.Native;

namespace Cometsnap.Core.Capture;

/// <summary>Monitor rectangles in physical virtual-screen pixels (process is per-monitor DPI aware).</summary>
public static class Monitors
{
    public static IReadOnlyList<PixelRect> All()
    {
        var list = new List<PixelRect>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = new Win32.MONITORINFO { cbSize = (uint)Marshal.SizeOf<Win32.MONITORINFO>() };
            if (Win32.GetMonitorInfo(h, ref info))
                list.Add(ToRect(info.rcMonitor));
            return true;
        }, IntPtr.Zero);
        if (list.Count == 0) list.Add(VirtualScreen());
        return list;
    }

    public static PixelRect VirtualScreen() => new(
        Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN),
        Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN),
        Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN),
        Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN));

    /// <summary>The monitor containing the point, or the closest one.</summary>
    public static PixelRect Containing(IReadOnlyList<PixelRect> monitors, int x, int y)
    {
        var best = monitors[0];
        var bestDist = long.MaxValue;
        foreach (var m in monitors)
        {
            if (m.Contains(x, y)) return m;
            long dx = x < m.X ? m.X - x : x >= m.Right ? x - m.Right + 1 : 0;
            long dy = y < m.Y ? m.Y - y : y >= m.Bottom ? y - m.Bottom + 1 : 0;
            var d = dx * dx + dy * dy;
            if (d < bestDist) { bestDist = d; best = m; }
        }
        return best;
    }

    internal static PixelRect ToRect(RECT r) => PixelRect.FromEdges(r.Left, r.Top, r.Right, r.Bottom);
}
