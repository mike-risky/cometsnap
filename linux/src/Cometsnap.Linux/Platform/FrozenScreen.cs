using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PixelRect = Cometsnap.Linux.Core.PixelRect;

namespace Cometsnap.Linux.Platform;

/// <summary>
/// The whole desktop, grabbed the instant the hotkey fired, as BGRA pixels.
/// <see cref="Bounds"/> is where it sits in desktop coordinates (the overlay's coordinate system).
/// On X11 one desktop unit is one pixel. On Wayland the portal image can be sharper than the
/// XWayland desktop (scaled displays): <see cref="Scale"/> image pixels per desktop unit.
/// </summary>
public sealed class FrozenScreen : IDisposable
{
    public PixelRect Bounds { get; }
    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public int Stride { get; }
    public byte[] Pixels { get; }
    public double Scale { get; }
    private Bitmap? _bitmap;

    public FrozenScreen(PixelRect bounds, int pixelWidth, int pixelHeight, byte[] pixels, int stride)
    {
        Bounds = bounds;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        Pixels = pixels;
        Stride = stride;
        Scale = bounds.Width > 0 ? (double)pixelWidth / bounds.Width : 1;
    }

    public FrozenScreen(PixelRect bounds, byte[] pixels, int stride)
        : this(bounds, bounds.Width, bounds.Height, pixels, stride) { }

    /// <summary>The image for drawing (created once, on first use).</summary>
    public unsafe Bitmap Bitmap
    {
        get
        {
            if (_bitmap is not null) return _bitmap;
            fixed (byte* p = Pixels)
                _bitmap = new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Opaque, (IntPtr)p,
                    new Avalonia.PixelSize(PixelWidth, PixelHeight), new Vector(96, 96), Stride);
            return _bitmap;
        }
    }

    /// <summary>Image pixel under a desktop point.</summary>
    public (int X, int Y) ToImage(double desktopX, double desktopY) =>
        ((int)Math.Floor((desktopX - Bounds.X) * Scale), (int)Math.Floor((desktopY - Bounds.Y) * Scale));

    /// <summary>The image rectangle of a desktop rectangle.</summary>
    public PixelRect ToImage(PixelRect desktop)
    {
        var x0 = (int)Math.Round((desktop.X - Bounds.X) * Scale);
        var y0 = (int)Math.Round((desktop.Y - Bounds.Y) * Scale);
        var x1 = (int)Math.Round((desktop.Right - Bounds.X) * Scale);
        var y1 = (int)Math.Round((desktop.Bottom - Bounds.Y) * Scale);
        return PixelRect.FromEdges(x0, y0, x1, y1).Intersect(new PixelRect(0, 0, PixelWidth, PixelHeight));
    }

    /// <summary>The color of an image pixel (0xRRGGBB), or null outside.</summary>
    public uint? ColorAtImage(int x, int y)
    {
        if (x < 0 || y < 0 || x >= PixelWidth || y >= PixelHeight) return null;
        var i = y * Stride + x * 4;
        return (uint)(Pixels[i + 2] << 16 | Pixels[i + 1] << 8 | Pixels[i]);
    }

    /// <summary>Copies the pixels of a desktop rectangle into a new bitmap (clipped to the grab).</summary>
    public unsafe Bitmap Crop(PixelRect desktopRect)
    {
        var r = ToImage(desktopRect);
        if (r.IsEmpty) r = new PixelRect(0, 0, Math.Min(1, PixelWidth), Math.Min(1, PixelHeight));
        var wb = new WriteableBitmap(new Avalonia.PixelSize(r.Width, r.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var fb = wb.Lock())
        {
            var rowBytes = r.Width * 4;
            fixed (byte* src = Pixels)
            {
                for (var y = 0; y < r.Height; y++)
                {
                    var s = src + (r.Y + y) * Stride + r.X * 4;
                    var d = (byte*)fb.Address + y * fb.RowBytes;
                    Buffer.MemoryCopy(s, d, fb.RowBytes, rowBytes);
                }
            }
        }
        return wb;
    }

    public void Dispose() => _bitmap?.Dispose();
}
