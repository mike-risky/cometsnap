using Avalonia.Media.Imaging;
using Cometsnap.Core.Text;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Tray;

namespace Cometsnap.Linux.Capture;

/// <summary>
/// Scan a QR code: drag over a code (or click a window) and ZXing reads it, offline (same code as
/// on Windows). What it says goes on the clipboard and a small window offers Copy / Open link.
/// </summary>
public sealed class QrScan
{
    private readonly CometApp _app;
    private bool _busy;

    public QrScan(CometApp app) => _app = app;

    public void Start() => _ = RunAsync();

    private async Task RunAsync()
    {
        if (_busy) return;
        var picked = await _app.Capture.PickAsync(OverlayPurpose.Qr);
        if (picked is not { } p) return;
        byte[] pixels;
        int width, height;
        Bitmap? thumb = null;
        try
        {
            var r = p.Screen.ToImage(p.Rect);
            if (r.IsEmpty) return;
            (width, height) = (r.Width, r.Height);
            pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
                Buffer.BlockCopy(p.Screen.Pixels, (r.Y + y) * p.Screen.Stride + r.X * 4, pixels, y * width * 4, width * 4);
            if (_app.Settings.ShowCometAnimation) thumb = p.Screen.Crop(p.Rect);
        }
        finally { p.Screen.Dispose(); }

        _busy = true;
        try
        {
            var codes = await Task.Run(() => QrCodes.Scan(pixels, width, height, width * 4));
            Log.Info($"QR scan: {codes.Count} code(s), {width}x{height}");
            if (codes.Count == 0)
            {
                _app.Toast(T("No QR code found") + " · " + T("Drag a box around the code, a little bigger than the code itself."), 4500);
                return;
            }
            await Show(codes);
            if (thumb is not null) _app.LaunchComet(p.Rect, thumb);
        }
        catch (Exception ex)
        {
            Log.Error("QR scan failed", ex);
            _app.Toast(T("No QR code found"), 3000);
        }
        finally { _busy = false; }
    }

    /// <summary>Copies the first code (a Wi-Fi code gives its password) and shows what the codes say.</summary>
    public async Task Show(IReadOnlyList<ScannedCode> codes)
    {
        if (codes.Count == 0) return;
        await _app.Clipboard.SetTextAsync(codes[0].ClipboardText);
        var window = new QrWindow(_app, codes);
        window.Show();
        window.Activate();
    }
}
