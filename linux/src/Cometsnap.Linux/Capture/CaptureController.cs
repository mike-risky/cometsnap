using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Platform;
using PixelRect = Cometsnap.Linux.Core.PixelRect;

namespace Cometsnap.Linux.Capture;

/// <summary>
/// Hotkey → grab the desktop → overlay → (release) → copy + save. The overlay windows are created
/// once per monitor and reused.
/// </summary>
public sealed class CaptureController
{
    private readonly CometApp _app;
    private readonly ScreenGrabber _grabber = new();
    private readonly List<OverlayWindow> _overlays = new();
    private bool _busy;

    public CaptureController(CometApp app) => _app = app;

    public bool IsBusy => _busy;

    /// <summary>All monitors in desktop pixels.</summary>
    public IReadOnlyList<PixelRect> Monitors()
    {
        var screens = _app.Screens?.All;
        if (screens is null || screens.Count == 0) return new[] { new PixelRect(0, 0, 1920, 1080) };
        return screens.Select(s => new PixelRect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height)).ToList();
    }

    public static PixelRect Union(IReadOnlyList<PixelRect> rects)
    {
        var l = rects.Min(r => r.X);
        var t = rects.Min(r => r.Y);
        var r = rects.Max(r => r.Right);
        var b = rects.Max(r => r.Bottom);
        return PixelRect.FromEdges(l, t, r, b);
    }

    /// <summary>Creates the overlay windows ahead of time (off-screen work only), so the first capture is instant.</summary>
    public void Prewarm()
    {
        try { EnsureOverlays(Monitors()); }
        catch (Exception ex) { Log.Error("Overlay prewarm failed", ex); }
    }

    private void EnsureOverlays(IReadOnlyList<PixelRect> monitors)
    {
        if (_overlays.Count == monitors.Count && _overlays.Select(o => o.Monitor).SequenceEqual(monitors)) return;
        foreach (var o in _overlays) o.Close();
        _overlays.Clear();
        foreach (var m in monitors) _overlays.Add(new OverlayWindow(m));
    }

    public void StartRegion() => _ = CaptureAsync();

    public async Task CaptureAsync()
    {
        var picked = await PickAsync(OverlayPurpose.Capture);
        if (picked is { } p) Deliver(p.Screen, p.Rect);
    }

    public void StartDelayed(int seconds) => _ = DelayedAsync(seconds);

    private async Task DelayedAsync(int seconds)
    {
        _app.Toast(F("Capture in {0} s…", seconds), 1200);
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        await CaptureAsync();
    }

    /// <summary>The monitor under the mouse, captured at once (no overlay).</summary>
    public void CaptureFullscreen() => _ = FullscreenAsync();

    private async Task FullscreenAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var monitors = Monitors();
            var screen = await _grabber.GrabAsync(Union(monitors));
            if (screen is null) { _app.Toast(T("Could not grab the screen"), 3000); return; }
            var pointer = X11.PointerPosition();
            var monitor = monitors.FirstOrDefault(m => pointer is { } pt && m.Contains(pt.X, pt.Y), monitors[0]);
            Deliver(screen, monitor);
        }
        finally { _busy = false; }
    }

    /// <summary>Freezes the screen and lets the user pick a window or an area. Null when cancelled.</summary>
    public async Task<(FrozenScreen Screen, PixelRect Rect)?> PickAsync(OverlayPurpose purpose)
    {
        if (_busy) return null;
        _busy = true;
        try
        {
            var monitors = Monitors();
            var desktop = Union(monitors);
            var windowsTask = Task.Run(() => _app.Settings.WindowDetection ? WindowFinder.List(desktop) : Array.Empty<WindowInfo>());
            var screen = await _grabber.GrabAsync(desktop);
            if (screen is null)
            {
                _app.Toast(_grabber.UsesPortal
                    ? T("The desktop did not allow the screenshot. Allow Cometsnap in the screenshot permission dialog and try again.")
                    : T("Could not grab the screen"), 5000);
                return null;
            }
            var windows = await windowsTask;

            EnsureOverlays(monitors);
            var session = new OverlaySession(screen, windows, monitors, _app.Settings, purpose);
            var done = new TaskCompletionSource<PixelRect?>();
            session.Finished += r => done.TrySetResult(r);

            var pointer = X11.PointerPosition();
            OverlayWindow? focus = null;
            foreach (var o in _overlays)
            {
                o.Begin(session);
                if (pointer is { } pt && o.Monitor.Contains(pt.X, pt.Y)) focus = o;
            }
            if (pointer is { } p0) session.MoveTo(p0.X, p0.Y);
            (focus ?? _overlays[0]).Focus();
            // Some window managers hand focus over a moment later.
            DispatcherTimer.RunOnce(() => (focus ?? _overlays[0]).Focus(), TimeSpan.FromMilliseconds(80));

            var rect = await done.Task;
            foreach (var o in _overlays) o.End();
            if (rect is not { IsEmpty: false } r) { screen.Dispose(); return null; }
            return (screen, r);
        }
        catch (Exception ex)
        {
            Log.Error("Capture failed", ex);
            foreach (var o in _overlays) o.End();
            return null;
        }
        finally { _busy = false; }
    }

    /// <summary>Copy + save + Edit card. The image is on the clipboard before the PNG reaches the disk.</summary>
    private void Deliver(FrozenScreen screen, PixelRect rect)
    {
        Bitmap image;
        try { image = screen.Crop(rect); }
        finally { screen.Dispose(); }
        _app.Sound.Click();
        _app.OnCaptured(image, rect);
    }
}
