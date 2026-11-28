using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cometsnap.Core.Styling;

/// <summary>WPF side of <see cref="StyleRenderer"/>: styles and flattens <see cref="BitmapSource"/>s. Safe on any thread.</summary>
public static class StyledBitmaps
{
    public static BitmapSource Apply(BitmapSource image, SnapStyle style, double scale = 1)
    {
        var (pixels, w, h) = Pixels(image);
        var styled = StyleRenderer.Render(pixels, w, h, w * 4, style, scale);
        return ToBitmap(styled);
    }

    /// <summary>The image on a solid color (no transparency): for JPG files and apps that drop alpha.</summary>
    public static BitmapSource Flatten(BitmapSource image, uint rgb = 0xFFFFFF)
    {
        if (!HasAlpha(image)) return image;
        var (pixels, w, h) = Pixels(image);
        return ToBitmap(StyleRenderer.Flatten(new StyledPixels(w, h, pixels), rgb));
    }

    /// <summary>True for formats that can hold transparency (a styled capture without a backdrop).</summary>
    public static bool HasAlpha(BitmapSource image) =>
        image.Format == PixelFormats.Bgra32 || image.Format == PixelFormats.Pbgra32;

    private static (byte[] Pixels, int Width, int Height) Pixels(BitmapSource image)
    {
        BitmapSource src = image.Format == PixelFormats.Bgra32 || image.Format == PixelFormats.Bgr32
            ? image
            : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var w = src.PixelWidth;
        var h = src.PixelHeight;
        var pixels = new byte[w * h * 4];
        src.CopyPixels(pixels, w * 4, 0);
        return (pixels, w, h);
    }

    private static BitmapSource ToBitmap(StyledPixels p)
    {
        var bmp = BitmapSource.Create(p.Width, p.Height, 96, 96, PixelFormats.Bgra32, null, p.Bgra, p.Stride);
        bmp.Freeze();
        return bmp;
    }
}
