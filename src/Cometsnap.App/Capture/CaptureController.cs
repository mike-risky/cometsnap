using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Cometsnap.App.Services;
using Cometsnap.Core.Capture;
using Cometsnap.Core.Diagnostics;
using Cometsnap.Core.Geometry;
using Cometsnap.Core.Native;
using Cometsnap.Core.Storage;
using Cometsnap.Core.Localization;

namespace Cometsnap.App.Capture;

/// <summary>Runs every kind of capture and delivers the result (clipboard first, disk in the background).</summary>
public sealed class CaptureController
{
    private readonly CometApp _app;
    private readonly CaptureOverlay _overlay;
    private readonly CaptureStore _store;
    private readonly uint _pid = (uint)Environment.ProcessId;
    private DispatcherTimer? _delayTimer;

    /// <summary>Raised on the UI thread with the path of every saved capture.</summary>
    public event Action<string>? CaptureSaved;

    /// <summary>
    /// Raised on the UI thread right after a capture was taken (before saving), with the
    /// virtual-screen rectangle it came from.
    /// </summary>
    public event Action<BitmapSource, PixelRect>? CaptureTaken;

    /// <summary>Raised on the UI thread when a capture (and its in-memory image) reached the disk.</summary>
    public event Action<string, BitmapSource>? CaptureStored;

    public CaptureController(CometApp app)
    {
        _app = app;
        _store = new CaptureStore(() => app.CapturesFolder);

        _overlay = new CaptureOverlay();
        _overlay.Confirmed += (image, rect) =>
        {
            if (TakePick() is { } pick) pick(rect);
            else Deliver(image, rect);
        };
        _overlay.Cancelled += () => _pick = null;
        _overlay.SaveRequested += image => { _pick = null; SaveAs(image); };
        _overlay.EditRequested += (image, doc) => { _pick = null; _app.OpenInEditor(image, doc); };
        _overlay.Prewarm();
    }

    public bool IsBusy => _overlay.IsCapturing;

    private Action<PixelRect>? _pick;

    private Action<PixelRect>? TakePick()
    {
        var pick = _pick;
        _pick = null;
        return pick;
    }

    /// <summary>
    /// Opens the usual region / window overlay, but only reports the chosen rectangle
    /// (nothing is copied or saved). Used to pick what to record.
    /// </summary>
    public void PickRegion(Action<PixelRect> picked)
    {
        if (_overlay.IsCapturing) return;
        _pick = picked;
        StartRegion();
        if (!_overlay.IsCapturing) _pick = null;
    }

    /// <summary>Region / window capture with the frozen-screen overlay.</summary>
    public void StartRegion()
    {
        if (_overlay.IsCapturing) return;
        CancelDelay();
        var sw = Stopwatch.StartNew();
        try
        {
            var screen = FrozenScreen.CaptureVirtualScreen();
            var grabbed = sw.ElapsedMilliseconds;
            var windows = WindowFinder.Snapshot(_pid);
            var monitors = Monitors.All();
            _overlay.Begin(screen, windows, monitors, _app.Settings);
            Log.Info($"Overlay ready in {sw.ElapsedMilliseconds} ms (grab {grabbed} ms, {windows.Count} windows, {screen.Bounds})");
        }
        catch (Exception ex)
        {
            Log.Error("Region capture failed", ex);
            _overlay.Cancel();
        }
    }

    /// <summary>Instant capture of the monitor under the mouse cursor.</summary>
    public void CaptureFullscreen()
    {
        if (_overlay.IsCapturing) return;
        try
        {
            Win32.GetCursorPos(out var p);
            var monitor = Monitors.Containing(Monitors.All(), p.X, p.Y);
            Deliver(FrozenScreen.Capture(monitor).CropAll(), monitor);
        }
        catch (Exception ex)
        {
            Log.Error("Fullscreen capture failed", ex);
        }
    }

    /// <summary>Instant capture of the foreground window (true bounds, no shadow).</summary>
    public void CaptureActiveWindow()
    {
        if (_overlay.IsCapturing) return;
        try
        {
            var hwnd = Win32.GetForegroundWindow();
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            if (hwnd == IntPtr.Zero || pid == _pid)
            {
                CaptureFullscreen();
                return;
            }
            var bounds = WindowFinder.GetBounds(hwnd).Intersect(Monitors.VirtualScreen());
            if (bounds.IsEmpty)
            {
                CaptureFullscreen();
                return;
            }
            Deliver(FrozenScreen.Capture(bounds).CropAll(), bounds);
        }
        catch (Exception ex)
        {
            Log.Error("Active window capture failed", ex);
        }
    }

    /// <summary>Opens the region overlay after a delay, to capture menus and hover states.</summary>
    public void StartDelayed(int seconds)
    {
        CancelDelay();
        var remaining = seconds;
        _app.SetTrayStatus(L.F("Cometsnap: capture in {0} s", remaining));
        _delayTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) =>
        {
            remaining--;
            if (remaining > 0)
            {
                _app.SetTrayStatus(L.F("Cometsnap: capture in {0} s", remaining));
                return;
            }
            CancelDelay();
            StartRegion();
        }, _app.Dispatcher);
        _delayTimer.Start();
    }

    private void CancelDelay()
    {
        if (_delayTimer is null) return;
        _delayTimer.Stop();
        _delayTimer = null;
        _app.SetTrayStatus(null);
    }

    /// <summary>Copies like a normal capture, then asks where to save an extra copy.</summary>
    private void SaveAs(BitmapSource image)
    {
        Deliver(image, null);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = L.T("Save capture"),
            FileName = SnapNaming.BaseName(DateTime.Now),
            DefaultExt = ".png",
            Filter = L.T("PNG image") + " (*.png)|*.png|" + L.T("JPEG image") + " (*.jpg)|*.jpg",
            InitialDirectory = _app.CapturesFolder,
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            if (dialog.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || dialog.FileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var fs = File.Create(dialog.FileName);
                encoder.Save(fs);
            }
            else
            {
                CaptureStore.SavePng(image, dialog.FileName);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Save as failed", ex);
            System.Windows.MessageBox.Show(L.T("Could not save the capture:") + "\n" + ex.Message, "Cometsnap");
        }
    }

    private void Deliver(BitmapSource image, PixelRect? from)
    {
        var settings = _app.Settings;
        var sw = Stopwatch.StartNew();
        if (settings.CopyToClipboardOnCapture && !ClipboardService.SetImage(image))
            Log.Error("Could not copy the capture to the clipboard");
        Log.Info($"Capture {image.PixelWidth}×{image.PixelHeight} on clipboard in {sw.ElapsedMilliseconds} ms");

        if (from is { } rect) CaptureTaken?.Invoke(image, rect);
        if (settings.AutoSave) _ = SaveAndAnnounce(image);
    }

    private async Task SaveAndAnnounce(BitmapSource image)
    {
        var path = await _store.SaveAsync(image, DateTime.Now);
        if (path is null) return;
        await _app.Dispatcher.InvokeAsync(() =>
        {
            CaptureSaved?.Invoke(path);
            CaptureStored?.Invoke(path, image);
        });
    }
}
