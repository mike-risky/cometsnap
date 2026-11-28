using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Cometsnap.Core.Styling;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Theme;

namespace Cometsnap.Linux.Services;

/// <summary>Applies the share-ready style (Settings > Share-ready style) to a capture. Safe on any thread.</summary>
public static class SnapStyler
{
    public static Bitmap Apply(Bitmap image, AppSettings settings) =>
        Apply(image, SnapStyles.Get(settings.SnapStyle), CaptureLook.Theme.Accent);

    public static unsafe Bitmap Apply(Bitmap image, SnapStyle style, uint accent)
    {
        var w = image.PixelSize.Width;
        var h = image.PixelSize.Height;
        var pixels = new byte[w * h * 4];
        fixed (byte* p = pixels)
            image.CopyPixels(new Avalonia.PixelRect(0, 0, w, h), (IntPtr)p, pixels.Length, w * 4);
        var styled = StyleRenderer.Render(pixels, w, h, w * 4, style, accent);

        // Avalonia draws premultiplied pixels best: convert the renderer's straight alpha.
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
}
