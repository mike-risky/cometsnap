using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Cometsnap.Core.Capture;
using Cometsnap.Core.Text;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Platform;
using Cometsnap.Linux.Services;
using PixelRect = Cometsnap.Linux.Core.PixelRect;

namespace Cometsnap.Linux.Capture;

/// <summary>
/// Copy text: drag over some text and Tesseract (the open source OCR engine, installed with the
/// distro's package manager) reads it, offline. The text goes on the clipboard.
/// </summary>
public sealed class TextCapture
{
    private readonly CometApp _app;
    private bool _busy;

    public TextCapture(CometApp app) => _app = app;

    public void Start() => _ = RunAsync();

    private async Task RunAsync()
    {
        if (_busy) return;
        var picked = await _app.Capture.PickAsync(OverlayPurpose.Text);
        if (picked is not { } p) return;
        Bitmap image;
        try { image = p.Screen.Crop(p.Rect); }
        finally { p.Screen.Dispose(); }
        await CopyTextAsync(image);
    }

    public async Task CopyTextAsync(Bitmap image)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var tesseract = FindTesseract();
            if (tesseract is null)
            {
                _app.Toast(T("Copy text needs Tesseract. Install it with: sudo apt install tesseract-ocr (or your distro's tesseract package)"), 8000);
                return;
            }
            _app.Toast(T("Reading the text…"), 1500);
            var text = await Task.Run(() => Read(tesseract, image));
            if (text.Length == 0)
            {
                _app.Toast(T("No text found") + " · " + T("Try a bigger area, or zoom in on small text."), 4000);
                return;
            }
            await _app.Clipboard.SetTextAsync(text);
            var first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (first.Length > 60) first = first[..60] + "…";
            _app.Toast(T("Text copied") + " · " + first, 3500);
        }
        catch (Exception ex)
        {
            Log.Error("Text recognition failed", ex);
            _app.Toast(T("Could not read the text"), 4000);
        }
        finally { _busy = false; }
    }

    private static string? FindTesseract()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin").Split(':'))
        {
            var path = Path.Combine(dir, "tesseract");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>Tesseract language codes for the app language and English, among those installed.</summary>
    private static string Languages(string tesseract)
    {
        var installed = Run(tesseract, "--list-langs").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        var wanted = Loc.Language switch
        {
            "fr" => "fra", "de" => "deu", "it" => "ita", "es" => "spa", "pt" => "por", "zh" => "chi_sim", "hi" => "hin", _ => "eng",
        };
        var langs = new List<string>();
        if (installed.Contains(wanted)) langs.Add(wanted);
        if (wanted != "eng" && installed.Contains("eng")) langs.Add("eng");
        return langs.Count == 0 ? "" : string.Join('+', langs);
    }

    private static string Read(string tesseract, Bitmap image)
    {
        var file = Path.Combine(Path.GetTempPath(), $"cometsnap-ocr-{Environment.ProcessId}.png");
        try
        {
            // Small screen text reads much better when it is scaled up first.
            var size = image.PixelSize;
            using var big = size.Width < 1600 && size.Height < 8000
                ? image.CreateScaledBitmap(new PixelSize(size.Width * 2, size.Height * 2), BitmapInterpolationMode.HighQuality)
                : null;
            File.WriteAllBytes(file, CaptureStore.EncodePng(big ?? image));
            var langs = Languages(tesseract);
            var output = Run(tesseract, $"\"{file}\" stdout --psm 3" + (langs.Length > 0 ? $" -l {langs}" : ""));
            var lines = output.Replace("\f", "").Split('\n').Select(OcrText.TidyLine).ToList();
            // Tesseract marks paragraphs with an empty line; squeeze longer runs and trim the ends.
            var text = string.Join('\n', lines);
            while (text.Contains("\n\n\n")) text = text.Replace("\n\n\n", "\n\n");
            return text.Trim();
        }
        finally
        {
            try { File.Delete(file); } catch { /* temp file */ }
        }
    }

    private static string Run(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(30000);
        return output;
    }
}

/// <summary>
/// Scrolling capture (X11), the natural way: drag over an area (or press on a window) and, still
/// holding the button, scroll. The overlay steps aside, the page scrolls as usual, and every new
/// row is glued under the previous ones. Letting go finishes, Esc cancels. Wayland does not let
/// apps grab the screen this fast or pass the wheel on, so there it says so.
/// </summary>
public sealed class ScrollingCapture
{
    public const int MaxHeight = 30000;
    public const int MinWidth = 40, MinHeight = 60;
    private readonly CometApp _app;
    private readonly ScreenGrabber _grabber = new();
    private bool _running;

    public ScrollingCapture(CometApp app) => _app = app;

    public bool IsRunning => _running;

    public static bool Supported => !DesktopSession.IsWayland || ScreenGrabber.ForceX11;

    /// <summary>Hotkey / menu: the usual capture, with a reminder to hold the button and scroll.</summary>
    public void Start()
    {
        if (_running) return;
        if (!Supported)
        {
            _app.Toast(T("Scrolling capture needs an X11 session for now: Wayland does not let apps scroll other windows."), 6000);
            return;
        }
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        // Let go without scrolling: an ordinary screenshot.
        var picked = await _app.Capture.PickAsync(OverlayPurpose.Scroll);
        if (picked is { } p) _app.Capture.Deliver(p.Screen, p.Rect);
    }

    /// <summary>The wheel turned with the button held over <paramref name="rect"/> in the overlay.</summary>
    public void RunHeld(PixelRect rect, int firstDirection, bool forText)
    {
        if (_running) return;
        _ = RunHeldAsync(rect, firstDirection, forText);
    }

    private async Task RunHeldAsync(PixelRect rect, int firstDirection, bool forText)
    {
        _running = true;
        try
        {
            // The overlay just went away: let the screen repaint.
            await Task.Delay(60);
            var frame = Grab(rect);
            if (frame is null) return;
            var stitcher = new ScrollStitcher(rect.Width, rect.Height);
            stitcher.Add(frame);
            XTest.Wheel(firstDirection); // the overlay took the first turn of the wheel
            _app.Toast(T("Scroll, then let go") + " · " + T("Esc cancels"), 1800);

            var lost = false;
            while (true)
            {
                await Task.Delay(40);
                if (XTest.EscapeDown())
                {
                    Log.Info("Scrolling capture cancelled");
                    return;
                }
                var released = !X11.PrimaryButtonDown();
                var next = released ? await SettledAsync(rect) : Grab(rect);
                if (next is not null && !next.AsSpan().SequenceEqual(frame))
                {
                    frame = next;
                    var step = stitcher.Add(next);
                    if (step == StitchStep.Appended) lost = false;
                    else if (step == StitchStep.Lost && !lost)
                    {
                        lost = true;
                        _app.Toast(T("Too fast: scroll back a little"), 1500);
                    }
                }
                if (released || stitcher.Height >= MaxHeight) break;
            }

            var pixels = stitcher.Compose(out var height);
            height = Math.Min(height, MaxHeight);
            using var tall = new FrozenScreen(new PixelRect(0, 0, rect.Width, height), pixels, rect.Width * 4);
            var image = tall.Crop(new PixelRect(0, 0, rect.Width, height));
            Log.Info($"Scrolling capture: {rect.Width}×{height} (header {stitcher.Header}, footer {stitcher.Footer})");
            if (forText) await _app.CopyText.CopyTextAsync(image);
            else _app.OnCaptured(image, rect);
        }
        catch (Exception ex)
        {
            Log.Error("Scrolling capture failed", ex);
            _app.Toast(T("Scrolling capture failed"), 4000);
        }
        finally
        {
            _running = false;
        }
    }

    /// <summary>Grabs until two grabs in a row are the same (smooth scrolling has finished), at most ~0.6 s.</summary>
    private async Task<byte[]?> SettledAsync(PixelRect rect)
    {
        var current = Grab(rect);
        if (current is null) return null;
        for (var waited = 0; waited < 600; waited += 60)
        {
            await Task.Delay(60);
            var next = Grab(rect);
            if (next is null) return current;
            var same = next.AsSpan().SequenceEqual(current);
            current = next;
            if (same) break;
        }
        return current;
    }

    private byte[]? Grab(PixelRect rect)
    {
        var shot = _grabber.GrabAsync(rect).GetAwaiter().GetResult();
        if (shot is null || shot.PixelWidth != rect.Width || shot.PixelHeight != rect.Height) { shot?.Dispose(); return null; }
        var pixels = shot.Pixels;
        shot.Dispose();
        return pixels;
    }

    /// <summary>A fake wheel click through the XTEST extension, and the Esc key state.</summary>
    private static class XTest
    {
        [DllImport("libXtst.so.6")] private static extern int XTestFakeButtonEvent(IntPtr display, uint button, int isPress, ulong delay);
        [DllImport("libX11.so.6")] private static extern int XQueryKeymap(IntPtr display, byte[] keys);
        [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(IntPtr display, ulong keysym);

        /// <summary>One click of the wheel: 1 = up (button 4), -1 = down (button 5).</summary>
        public static void Wheel(int direction)
        {
            var d = X11.Helper;
            if (d == IntPtr.Zero || direction == 0) return;
            var button = direction > 0 ? 4u : 5u;
            XTestFakeButtonEvent(d, button, 1, 0);
            XTestFakeButtonEvent(d, button, 0, 0);
            X11.XFlush(d);
        }

        public static bool EscapeDown()
        {
            var d = X11.Helper;
            if (d == IntPtr.Zero) return false;
            var keys = new byte[32];
            XQueryKeymap(d, keys);
            var code = XKeysymToKeycode(d, 0xFF1B /* XK_Escape */);
            return code != 0 && (keys[code / 8] & (1 << (code % 8))) != 0;
        }
    }
}
