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
/// Scrolling capture (X11): pick a window or an area, Cometsnap turns the mouse wheel for you and
/// glues the frames into one tall image. Stops at the end of the page, on Esc, or when the mouse moves.
/// Wayland does not let apps scroll other windows, so there it says so.
/// </summary>
public sealed class ScrollingCapture
{
    public const int MaxHeight = 30000;
    private readonly CometApp _app;
    private readonly ScreenGrabber _grabber = new();
    private bool _running;
    private bool _stop;

    public ScrollingCapture(CometApp app) => _app = app;

    public void Start()
    {
        if (_running) { _stop = true; return; }
        if (DesktopSession.IsWayland && !ScreenGrabber.ForceX11)
        {
            _app.Toast(T("Scrolling capture needs an X11 session for now: Wayland does not let apps scroll other windows."), 6000);
            return;
        }
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        var picked = await _app.Capture.PickAsync(OverlayPurpose.Scroll);
        if (picked is not { } p) return;
        p.Screen.Dispose();
        var rect = p.Rect;
        if (rect.Width < 40 || rect.Height < 60)
        {
            _app.Toast(T("Pick a window or an area of at least 60 pixels high."), 4000);
            return;
        }
        _running = true;
        _stop = false;
        var userPointer = X11.PointerPosition();
        try
        {
            await Task.Delay(150);
            var stitcher = new ScrollStitcher(rect.Width, rect.Height);
            var frame = await SettledAsync(rect, null, 0);
            if (frame is null) return;
            stitcher.Add(frame);
            _app.Toast(T("Scrolling…") + " · " + T("Esc to stop"), 1800);

            var parkX = rect.CenterX;
            var parkY = rect.CenterY;
            XTest.Warp(parkX, parkY);
            await Task.Delay(60);

            var clicks = 3;
            var unchanged = 0;
            for (var step = 0; step < 400 && !ShouldStop(parkX, parkY); step++)
            {
                XTest.WheelDown(clicks);
                frame = await SettledAsync(rect, frame, 140);
                if (frame is null || ShouldStop(parkX, parkY)) break;
                var result = stitcher.Add(frame);
                if (result == StitchStep.Unchanged)
                {
                    if (++unchanged >= 2) break;
                    await Task.Delay(250);
                    continue;
                }
                unchanged = 0;
                if (result == StitchStep.Lost) break;
                var target = Math.Max(20, stitcher.ScrollingHeight / 2);
                clicks = Math.Clamp((int)Math.Round(clicks * (double)target / Math.Max(1, stitcher.LastShift)), 1, 10);
                if (stitcher.Height >= MaxHeight) break;
            }

            var pixels = stitcher.Compose(out var height);
            height = Math.Min(height, MaxHeight);
            using var tall = new FrozenScreen(new PixelRect(0, 0, rect.Width, height), pixels, rect.Width * 4);
            var image = tall.Crop(new PixelRect(0, 0, rect.Width, height));
            Log.Info($"Scrolling capture: {rect.Width}×{height} (header {stitcher.Header}, footer {stitcher.Footer})");
            if (height <= rect.Height)
                _app.Toast(T("Nothing scrolled") + " · " + T("Cometsnap kept what was visible. Pick the part of the window that scrolls, and keep the mouse still."), 6000);
            _app.OnCaptured(image, rect);
        }
        catch (Exception ex)
        {
            Log.Error("Scrolling capture failed", ex);
            _app.Toast(T("Scrolling capture failed"), 4000);
        }
        finally
        {
            _running = false;
            if (userPointer is { } u) XTest.Warp(u.X, u.Y);
        }
    }

    private bool ShouldStop(int parkX, int parkY)
    {
        if (_stop || XTest.EscapeDown()) return true;
        var pt = X11.PointerPosition();
        return pt is { } q && (Math.Abs(q.X - parkX) > 12 || Math.Abs(q.Y - parkY) > 12);
    }

    private async Task<byte[]?> SettledAsync(PixelRect rect, byte[]? previous, int minimumWait)
    {
        if (minimumWait > 0) await Task.Delay(minimumWait);
        var current = Grab(rect);
        if (current is null) return null;
        var waited = minimumWait;
        while (waited < 1500)
        {
            await Task.Delay(70);
            waited += 70;
            var next = Grab(rect);
            if (next is null) return current;
            var same = next.AsSpan().SequenceEqual(current);
            current = next;
            if (same && (previous is null || waited >= 400 || !next.AsSpan().SequenceEqual(previous))) break;
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

    /// <summary>Fake mouse wheel and pointer moves through the XTEST extension.</summary>
    private static class XTest
    {
        [DllImport("libXtst.so.6")] private static extern int XTestFakeButtonEvent(IntPtr display, uint button, int isPress, ulong delay);
        [DllImport("libX11.so.6")] private static extern int XQueryKeymap(IntPtr display, byte[] keys);
        [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(IntPtr display, ulong keysym);

        public static void WheelDown(int clicks)
        {
            var d = X11.Helper;
            if (d == IntPtr.Zero) return;
            for (var i = 0; i < clicks; i++)
            {
                XTestFakeButtonEvent(d, 5, 1, 0);
                XTestFakeButtonEvent(d, 5, 0, 0);
            }
            X11.XFlush(d);
        }

        public static void Warp(int x, int y)
        {
            var d = X11.Helper;
            if (d == IntPtr.Zero) return;
            X11.XWarpPointer(d, IntPtr.Zero, X11.XDefaultRootWindow(d), 0, 0, 0, 0, x, y);
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
