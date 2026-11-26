using System.Runtime.InteropServices;

namespace Cometsnap.App.Recording;

internal static class RecordingNative
{
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_LAYERED = 0x00080000;
    /// <summary>Windows 10 2004+: the window is visible on screen but left out of every capture.</summary>
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr CreateIconIndirect(ref ICONINFO info);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateBitmap(int w, int h, uint planes, uint bitCount, byte[]? bits);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);

    /// <summary>Turns a WPF window into a tool window that never takes focus and is kept out of screen recordings.</summary>
    public static void MakeOverlay(IntPtr hwnd, bool clickThrough)
    {
        var ex = Cometsnap.Core.Native.Win32.GetWindowLong(hwnd, Cometsnap.Core.Native.Win32.GWL_EXSTYLE)
                 | Cometsnap.Core.Native.Win32.WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        if (clickThrough) ex |= Cometsnap.Core.Native.Win32.WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, Cometsnap.Core.Native.Win32.GWL_EXSTYLE, ex);
        if (!SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE))
            Cometsnap.Core.Diagnostics.Log.Info($"SetWindowDisplayAffinity failed ({Marshal.GetLastWin32Error()}), the recording controls may be visible in the video");
    }
}
