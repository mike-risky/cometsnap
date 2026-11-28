using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Cometsnap.Core.Styling;

namespace Cometsnap.Linux.Services;

/// <summary>The Settings preview of a style: a small pretend app window, styled for real.</summary>
public static class StylePreview
{
    private const int W = 640, H = 380;
    private static byte[]? _sample;

    public static unsafe Bitmap Render(SnapStyle style, uint accent)
    {
        var src = _sample ??= Sample();
        var styled = StyleRenderer.Render(src, W, H, W * 4, style, accent);
        var px = styled.Bgra;
        for (var i = 0; i < px.Length; i += 4)
        {
            var a = px[i + 3];
            if (a == 255) continue;
            px[i] = (byte)(px[i] * a / 255);
            px[i + 1] = (byte)(px[i + 1] * a / 255);
            px[i + 2] = (byte)(px[i + 2] * a / 255);
        }
        fixed (byte* p = px)
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, (IntPtr)p, new PixelSize(styled.Width, styled.Height), new Vector(96, 96), styled.Stride);
    }

    /// <summary>Light page, title bar with three dots, a heading, text lines, a picture and a button.</summary>
    private static byte[] Sample()
    {
        var p = new byte[W * H * 4];
        void Rect(int x, int y, int w, int h, uint rgb, int radius = 0)
        {
            for (var yy = y; yy < y + h; yy++)
            for (var xx = x; xx < x + w; xx++)
            {
                if (radius > 0 && StyleRenderer.RoundedBoxDistance(xx + 0.5 - (x + w / 2.0), yy + 0.5 - (y + h / 2.0), w / 2.0, h / 2.0, radius) > 0) continue;
                var i = (yy * W + xx) * 4;
                p[i] = (byte)rgb; p[i + 1] = (byte)(rgb >> 8); p[i + 2] = (byte)(rgb >> 16); p[i + 3] = 255;
            }
        }
        Rect(0, 0, W, H, 0xF7F8FC);
        Rect(0, 0, W, 44, 0xE6E9F3);
        Rect(17, 15, 14, 14, 0xE2553A, 7);
        Rect(39, 15, 14, 14, 0xFFD60A, 7);
        Rect(61, 15, 14, 14, 0x3D6BFF, 7);
        Rect(36, 76, 260, 26, 0x07113A, 6);
        for (var i = 0; i < 4; i++) Rect(36, 126 + i * 30, 300 - i * 40, 14, 0xC9CFE3, 7);
        Rect(376, 76, 228, 166, 0x3D6BFF, 12);
        Rect(456, 125, 68, 68, 0xFFD60A, 34);
        Rect(36, 280, 140, 44, 0xE2553A, 10);
        Rect(376, 270, 228, 14, 0xC9CFE3, 7);
        Rect(376, 300, 170, 14, 0xC9CFE3, 7);
        return p;
    }
}
