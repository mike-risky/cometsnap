using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cometsnap.Core.Diagnostics;
using Cometsnap.Core.Text;

namespace Cometsnap.App.Services;

/// <summary>Reads QR codes and barcodes in a capture, on a worker thread, offline.</summary>
public static class QrService
{
    /// <summary>
    /// <paramref name="quick"/> = the silent check after each capture (QR codes only, fast).
    /// Never throws: a problem just means nothing was found.
    /// </summary>
    public static async Task<IReadOnlyList<ScannedCode>> ScanAsync(BitmapSource image, bool quick)
    {
        try
        {
            if (quick && (long)image.PixelWidth * image.PixelHeight > QrCodes.QuickScanMaxPixels) return [];
            var source = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var width = source.PixelWidth;
            var height = source.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            source.CopyPixels(pixels, stride, 0);
            var sw = Stopwatch.StartNew();
            var codes = await Task.Run(() => QrCodes.Scan(pixels, width, height, stride, quick));
            if (!quick || codes.Count > 0) Log.Info($"QR scan ({(quick ? "quick" : "full")}): {codes.Count} code(s) in {sw.ElapsedMilliseconds} ms, {width}x{height}");
            return codes;
        }
        catch (Exception ex)
        {
            Log.Error("QR scan failed", ex);
            return [];
        }
    }

    /// <summary>Opens a web address in the default browser (only ever after a click).</summary>
    public static void OpenLink(string link)
    {
        if (QrCodes.OpenableLink(link) is not { } safe) return;
        try { Process.Start(new ProcessStartInfo(safe) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Could not open the link", ex); }
    }
}
