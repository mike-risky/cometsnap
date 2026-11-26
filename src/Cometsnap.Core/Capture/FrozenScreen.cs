using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cometsnap.Core.Geometry;
using Cometsnap.Core.Native;

namespace Cometsnap.Core.Capture;

/// <summary>
/// A pixel-exact copy of (part of) the virtual screen, taken the instant a capture starts.
/// The overlay, the loupe and the final crop all read from this one bitmap.
/// </summary>
public sealed class FrozenScreen
{
    /// <summary>Position and size in virtual-screen physical pixels.</summary>
    public PixelRect Bounds { get; }
    public int Stride { get; }
    /// <summary>BGRX pixels, top-down.</summary>
    public byte[] Pixels { get; }
    /// <summary>Frozen WPF image of <see cref="Pixels"/> (96 DPI: one image pixel = one screen pixel).</summary>
    public BitmapSource Image { get; }

    private FrozenScreen(PixelRect bounds, int stride, byte[] pixels)
    {
        Bounds = bounds;
        Stride = stride;
        Pixels = pixels;
        Image = BitmapSource.Create(bounds.Width, bounds.Height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
        Image.Freeze();
    }

    public static FrozenScreen CaptureVirtualScreen() => Capture(Monitors.VirtualScreen());

    public static FrozenScreen Capture(PixelRect area)
    {
        if (area.IsEmpty) throw new ArgumentException("Empty capture area", nameof(area));

        var screenDc = Win32.GetDC(IntPtr.Zero);
        var memDc = Win32.CreateCompatibleDC(screenDc);
        var header = new Win32.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
            biWidth = area.Width,
            biHeight = -area.Height, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        var dib = Win32.CreateDIBSection(screenDc, ref header, 0, out var bits, IntPtr.Zero, 0);
        try
        {
            if (dib == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection failed");
            var old = Win32.SelectObject(memDc, dib);
            Win32.BitBlt(memDc, 0, 0, area.Width, area.Height, screenDc, area.X, area.Y, Win32.SRCCOPY | Win32.CAPTUREBLT);
            Win32.SelectObject(memDc, old);

            var stride = area.Width * 4;
            var pixels = GC.AllocateUninitializedArray<byte>(stride * area.Height);
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return new FrozenScreen(area, stride, pixels);
        }
        finally
        {
            if (dib != IntPtr.Zero) Win32.DeleteObject(dib);
            Win32.DeleteDC(memDc);
            Win32.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>Color at a point in local image coordinates (0,0 = top-left of <see cref="Bounds"/>).</summary>
    public Color ColorAt(int localX, int localY)
    {
        if ((uint)localX >= (uint)Bounds.Width || (uint)localY >= (uint)Bounds.Height) return Colors.Transparent;
        var i = localY * Stride + localX * 4;
        return Color.FromRgb(Pixels[i + 2], Pixels[i + 1], Pixels[i]);
    }

    /// <summary>Copies a region (local coordinates) into a new, small, frozen bitmap.</summary>
    public BitmapSource Crop(PixelRect local)
    {
        local = local.Intersect(new PixelRect(0, 0, Bounds.Width, Bounds.Height));
        if (local.IsEmpty) throw new ArgumentException("Crop area is outside the captured screen", nameof(local));

        var stride = local.Width * 4;
        var buffer = GC.AllocateUninitializedArray<byte>(stride * local.Height);
        for (var row = 0; row < local.Height; row++)
            Buffer.BlockCopy(Pixels, (local.Y + row) * Stride + local.X * 4, buffer, row * stride, stride);

        var bmp = BitmapSource.Create(local.Width, local.Height, 96, 96, PixelFormats.Bgr32, null, buffer, stride);
        bmp.Freeze();
        return bmp;
    }

    public BitmapSource CropAll() => Crop(new PixelRect(0, 0, Bounds.Width, Bounds.Height));

    public PixelRect ToLocal(PixelRect virtualRect) => virtualRect.Offset(-Bounds.X, -Bounds.Y);
    public PixelRect ToVirtual(PixelRect localRect) => localRect.Offset(Bounds.X, Bounds.Y);
}
