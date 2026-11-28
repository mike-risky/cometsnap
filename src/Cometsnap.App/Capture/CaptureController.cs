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
            var pickImage = _pickImage;
            _pickImage = null;
            if (TakePick() is { } pick) pick(rect);
            else if (pickImage is not null) pickImage(image, rect);
            else Deliver(image, rect);
        };
        _overlay.Cancelled += () => { _pick = null; _pickImage = null; };
        _overlay.SaveRequested += image => { _pick = null; _pickImage = null; SaveAs(image); };
        _overlay.EditRequested += (image, doc) => { _pick = null; _pickImage = null; _app.OpenInEditor(image, doc); };
        // Wheel with the button held: a scrolling capture (not while picking what to record).
        _overlay.CanScroll = () => _pick is null && !_app.Scrolling.IsRunning;
        _overlay.ScrollStarted += (rect, delta) =>
        {
            var pickImage = _pickImage; // "Copy text" reads the whole long page
            _pickImage = null;
            _app.Scrolling.RunHeld(rect, delta, pickImage);
        };
        _overlay.ColorPicked += (color, text) => (_colorToast ??= new ColorToast()).ShowFor(color, text);
        _overlay.Prewarm();
        StartKeepingWarm();
    }

    // ------------------------------------------------------------------ keep warm

    /// <summary>How long the overlay may sit unused before we run it once more in the background.</summary>
    private static readonly TimeSpan WarmAfter = TimeSpan.FromMinutes(4);
    private DispatcherTimer? _warmTimer;
    private long _lastUsed = Environment.TickCount64;

    /// <summary>
    /// After a quiet spell Windows pages an idle tray app out, so the next overlay is slow to
    /// open and the first drag could be missed. Every few idle minutes, and when the PC wakes or
    /// is unlocked, run the overlay's open path once, invisibly, so it stays ready.
    /// </summary>
    private void StartKeepingWarm()
    {
        _warmTimer = new DispatcherTimer(DispatcherPriority.Background, _app.Dispatcher) { Interval = WarmAfter };
        _warmTimer.Tick += (_, _) =>
        {
            if (Environment.TickCount64 - _lastUsed >= (long)WarmAfter.TotalMilliseconds) Warm();
        };
        _warmTimer.Start();

        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume) WarmSoon();
        };
        Microsoft.Win32.SystemEvents.SessionSwitch += (_, e) =>
        {
            if (e.Reason is Microsoft.Win32.SessionSwitchReason.SessionUnlock or Microsoft.Win32.SessionSwitchReason.ConsoleConnect)
                WarmSoon();
        };
    }

    /// <summary>System events arrive on their own thread: warm up on the UI thread a moment later.</summary>
    private void WarmSoon() => _app.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
    {
        var once = new DispatcherTimer(DispatcherPriority.Background, _app.Dispatcher) { Interval = TimeSpan.FromSeconds(3) };
        once.Tick += (_, _) => { once.Stop(); Warm(); };
        once.Start();
    });

    private void Warm()
    {
        if (_overlay.IsCapturing || _app.Scrolling is { IsRunning: true }) return;
        try
        {
            // The same native paths a capture uses: a tiny screen grab, the window list, the overlay.
            FrozenScreen.Capture(new PixelRect(0, 0, 1, 1));
            WindowFinder.Snapshot(_pid);
            Monitors.All();
            _overlay.Rewarm();
        }
        catch
        {
            // Only a warm-up: the real capture reports its own errors.
        }
        _lastUsed = Environment.TickCount64;
    }

    public bool IsBusy => _overlay.IsCapturing;

    private ColorToast? _colorToast;

    private Action<PixelRect>? _pick;
    private Action<BitmapSource, PixelRect>? _pickImage;

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
        _pickImage = null;
        StartRegion();
        if (!_overlay.IsCapturing) _pick = null;
    }

    /// <summary>
    /// Opens the usual overlay and hands the picked pixels to <paramref name="picked"/> instead of
    /// copying and saving them. Used to read the text in an area.
    /// </summary>
    public void PickArea(Action<BitmapSource, PixelRect> picked)
    {
        if (_overlay.IsCapturing) return;
        _pick = null;
        _pickImage = picked;
        StartRegion();
        if (!_overlay.IsCapturing) _pickImage = null;
    }

    /// <summary>"Copy text": drag over some text (or click a window) and its text goes on the clipboard.</summary>
    public void StartTextCapture() => PickArea((image, rect) => _ = _app.CopyTextAsync(image, rect));

    /// <summary>"Scan a QR code": drag over a code (or click a window) and what it says goes on the clipboard.</summary>
    public void StartQrScan() => PickArea((image, rect) => _ = _app.ScanCodesAsync(image, rect));

    /// <summary>Copies, saves and announces an image made elsewhere (a scrolling capture), like a normal capture.</summary>
    public void DeliverImage(BitmapSource image, PixelRect from) => Deliver(image, from);

    /// <summary>Region / window capture with the frozen-screen overlay.</summary>
    public void StartRegion() => StartRegion(scrollHint: false);

    /// <summary>Color picker: the loupe opens on the frozen screen, a click copies the color as #RRGGBB.</summary>
    public void StartColorPicker()
    {
        if (_overlay.IsCapturing) return;
        CancelDelay();
        try
        {
            _lastUsed = Environment.TickCount64;
            _overlay.Begin(FrozenScreen.CaptureVirtualScreen(), Array.Empty<WindowInfo>(), Monitors.All(), _app.Settings, pickColor: true);
        }
        catch (Exception ex)
        {
            Log.Error("Color picker failed", ex);
            _overlay.Cancel();
        }
    }

    /// <summary>
    /// Region / window capture with the frozen-screen overlay. Holding the button and scrolling
    /// turns it into a scrolling capture; <paramref name="scrollHint"/> says so on the size tag.
    /// </summary>
    public void StartRegion(bool scrollHint)
    {
        if (_overlay.IsCapturing) return;
        // Read before the (possibly slow) screen grab: a press made after this point is the
        // start of a drag, even if it lands before the overlay is on screen.
        var buttonHeld = Win32.IsPrimaryButtonDown();
        CancelDelay();
        _lastUsed = Environment.TickCount64;
        var sw = Stopwatch.StartNew();
        try
        {
            var screen = FrozenScreen.CaptureVirtualScreen();
            var grabbed = sw.ElapsedMilliseconds;
            var windows = WindowFinder.Snapshot(_pid);
            var monitors = Monitors.All();
            _overlay.Begin(screen, windows, monitors, _app.Settings, scrollHint, buttonHeldAtStart: buttonHeld);
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

    /// <summary>Copies like a normal capture, then asks where to save it.</summary>
    private void SaveAs(BitmapSource image) => Deliver(image, null, askWhere: true);

    /// <summary>The Save dialog, after the clipboard copy. The file joins the recent captures.</summary>
    private void AskAndSave(BitmapSource image)
    {
        var settings = _app.Settings;
        var jpgFirst = SnapNaming.ExtensionFor(settings.ImageFormat) == ".jpg";
        var png = L.T("PNG image") + " (*.png)|*.png";
        var jpg = L.T("JPEG image") + " (*.jpg)|*.jpg";
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = L.T("Save capture"),
            FileName = SnapNaming.SuggestedName(settings.FileNamePattern, DateTime.Now, _app.PeekNameCounter()),
            DefaultExt = jpgFirst ? ".jpg" : ".png",
            Filter = jpgFirst ? jpg + "|" + png : png + "|" + jpg,
            InitialDirectory = Directory.Exists(_app.LastSaveFolder) ? _app.LastSaveFolder : _app.CapturesFolder,
        };
        if (dialog.ShowDialog() != true)
        {
            if (settings.OpenEditorAfterCapture) _app.OpenInEditor(image, new Cometsnap.Editor.AnnotationDocument());
            return;
        }
        try
        {
            CaptureStore.SaveImage(image, dialog.FileName);
            _app.LastSaveFolder = Path.GetDirectoryName(dialog.FileName);
            _app.TakeNameCounter();
            CaptureSaved?.Invoke(dialog.FileName);
            CaptureStored?.Invoke(dialog.FileName, image);
            if (settings.OpenEditorAfterCapture) _app.OpenInEditor(dialog.FileName);
        }
        catch (Exception ex)
        {
            Log.Error("Save as failed", ex);
            System.Windows.MessageBox.Show(L.T("Could not save the capture:") + "\n" + ex.Message, "Cometsnap");
        }
    }

    private void Deliver(BitmapSource image, PixelRect? from, bool askWhere = false)
    {
        var settings = _app.Settings;
        var sw = Stopwatch.StartNew();
        if (settings.CopyToClipboardOnCapture && !ClipboardService.SetImage(image))
            Log.Error("Could not copy the capture to the clipboard");
        Log.Info($"Capture {image.PixelWidth}×{image.PixelHeight} on clipboard in {sw.ElapsedMilliseconds} ms");

        if (from is { } rect) CaptureTaken?.Invoke(image, rect);

        // The card and the comet show first; the dialog or the editor come right after.
        if (askWhere || settings.AskWhereToSave)
            _app.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => AskAndSave(image));
        else if (settings.AutoSave)
            _ = SaveAndAnnounce(image, settings.OpenEditorAfterCapture);
        else if (settings.OpenEditorAfterCapture)
            _app.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => _app.OpenInEditor(image, new Cometsnap.Editor.AnnotationDocument()));
    }

    private async Task SaveAndAnnounce(BitmapSource image, bool thenEdit)
    {
        var s = _app.Settings;
        var path = await _store.SaveAsync(image, DateTime.Now, s.FileNamePattern, SnapNaming.ExtensionFor(s.ImageFormat), _app.TakeNameCounter());
        if (path is null)
        {
            if (thenEdit) await _app.Dispatcher.InvokeAsync(() => _app.OpenInEditor(image, new Cometsnap.Editor.AnnotationDocument()));
            return;
        }
        await _app.Dispatcher.InvokeAsync(() =>
        {
            CaptureSaved?.Invoke(path);
            CaptureStored?.Invoke(path, image);
            if (thenEdit) _app.OpenInEditor(path);
        });
    }
}
