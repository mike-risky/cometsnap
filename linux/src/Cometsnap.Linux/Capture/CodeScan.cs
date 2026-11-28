using Avalonia.Media.Imaging;
using Cometsnap.Core.Scanning;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Platform;

namespace Cometsnap.Linux.Capture;

/// <summary>
/// Scan a QR code: drag over a QR code (or a barcode) and ZXing reads it, offline. What it holds
/// goes on the clipboard. A web link gets an "Open link" button, never opened on its own.
/// </summary>
public sealed class CodeScan
{
    private readonly CometApp _app;
    private bool _busy;

    public CodeScan(CometApp app) => _app = app;

    public void Start() => _ = RunAsync();

    private async Task RunAsync()
    {
        if (_busy) return;
        var picked = await _app.Capture.PickAsync(OverlayPurpose.Qr);
        if (picked is not { } p) return;
        Bitmap image;
        try { image = p.Screen.Crop(p.Rect); }
        finally { p.Screen.Dispose(); }
        await ScanAsync(image);
    }

    public async Task ScanAsync(Bitmap image)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var codes = await Task.Run(() => Scan(image));
            if (codes.Count == 0)
            {
                _app.Toast(T("No QR code found") + " · " + T("Select just the code, a bit bigger"), 4000);
                return;
            }
            var text = string.Join("\n", codes.Select(c => c.Text));
            await _app.Clipboard.SetTextAsync(text);
            var first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (first.Length > 60) first = first[..60] + "…";
            var title = codes[0].Format == "QR code" ? T("QR code copied") : T("Code copied");
            if (CodeScanner.LinkOf(codes[0].Text) is { } link)
                _app.Toast(title + " · " + first, 8000, "🔗 " + T("Open link"), () => Tools.Open(link));
            else
                _app.Toast(title + " · " + first, 4000);
        }
        catch (Exception ex)
        {
            Log.Error("QR scan failed", ex);
            _app.Toast(T("Could not scan the code"), 4000);
        }
        finally { _busy = false; }
    }

    /// <summary>The QR code in a normal capture, if any (QR only, quick). Safe on any thread.</summary>
    public static ScannedCode? FindQr(Bitmap image)
    {
        var (pixels, w, h) = Pixels(image);
        return CodeScanner.FindQr(pixels, w, h, w * 4);
    }

    private static IReadOnlyList<ScannedCode> Scan(Bitmap image)
    {
        var (pixels, w, h) = Pixels(image);
        return CodeScanner.Scan(pixels, w, h, w * 4);
    }

    private static unsafe (byte[] Pixels, int W, int H) Pixels(Bitmap image)
    {
        var w = image.PixelSize.Width;
        var h = image.PixelSize.Height;
        var pixels = new byte[w * h * 4];
        fixed (byte* p = pixels)
            image.CopyPixels(new Avalonia.PixelRect(0, 0, w, h), (IntPtr)p, pixels.Length, w * 4);
        return (pixels, w, h);
    }
}
