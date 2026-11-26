using System.Runtime.InteropServices;

namespace Cometsnap.Video.Capture;

/// <summary>
/// One video frame: a top-down 32-bit BGRA GDI DIB section. Sources write into <see cref="Bits"/>
/// (or BitBlt into <see cref="Hdc"/>), and the cursor is drawn on top with GDI, which handles
/// every cursor type (colour, monochrome, inverting) exactly like Windows does.
/// </summary>
internal sealed class FrameCanvas : IDisposable
{
    private readonly IntPtr _bitmap;
    private readonly IntPtr _old;

    public int Width { get; }
    public int Height { get; }
    public int Stride => Width * 4;
    public int Length => Stride * Height;
    public IntPtr Bits { get; }
    public IntPtr Hdc { get; }

    public FrameCanvas(int width, int height)
    {
        Width = width;
        Height = height;
        Hdc = Native.CreateCompatibleDC(IntPtr.Zero);
        var header = new Native.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // negative = top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        _bitmap = Native.CreateDIBSection(Hdc, ref header, Native.DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
        if (_bitmap == IntPtr.Zero || bits == IntPtr.Zero)
        {
            Native.DeleteDC(Hdc);
            throw new InvalidOperationException($"Could not allocate a {width}×{height} frame");
        }
        Bits = bits;
        _old = Native.SelectObject(Hdc, _bitmap);
    }

    public unsafe Span<byte> Span => new((void*)Bits, Length);

    public unsafe Span<byte> Row(int y) => new((byte*)Bits + (long)y * Stride, Stride);

    public void Dispose()
    {
        Native.SelectObject(Hdc, _old);
        Native.DeleteObject(_bitmap);
        Native.DeleteDC(Hdc);
    }
}
