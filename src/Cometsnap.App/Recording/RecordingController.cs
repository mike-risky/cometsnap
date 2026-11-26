using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Cometsnap.App.Tray;
using Cometsnap.Core.Capture;
using Cometsnap.Core.Diagnostics;
using Cometsnap.Core.Geometry;
using Cometsnap.Core.Native;
using Cometsnap.Video;

namespace Cometsnap.App.Recording;

/// <summary>
/// "Transmissions": records a region or a whole monitor to MP4 or GIF. Pick the region with the
/// usual capture overlay, recording starts at once; stop with the same hotkey, the pill, or a
/// click on the tray icon (which pulses red meanwhile). The file lands next to the captures and
/// goes on the clipboard, ready to paste.
/// </summary>
public sealed class RecordingController : IDisposable
{
    private readonly CometApp _app;
    private readonly TrayIcon _tray;
    private readonly DispatcherTimer _ticker;
    private RecordingIcons? _icons;
    private RecordingFrame? _frame;
    private RecordingPill? _pill;
    private ScreenRecorder? _recorder;
    private int _iconFrame;
    private bool _starting;

    public RecordingController(CometApp app, TrayIcon tray)
    {
        _app = app;
        _tray = tray;
        VideoLog.Info = Log.Info;
        VideoLog.Error = Log.Error;
        _ticker = new DispatcherTimer(DispatcherPriority.Normal, app.Dispatcher) { Interval = TimeSpan.FromMilliseconds(120) };
        _ticker.Tick += (_, _) => Tick();
    }

    public bool IsRecording => _recorder is not null;

    /// <summary>Raised on the UI thread when recording starts or stops.</summary>
    public event Action? StateChanged;

    /// <summary>Hotkey / menu entry: pick a region and record it. While recording, stops instead.</summary>
    public void ToggleRegion(RecordingFormat format)
    {
        if (IsRecording)
        {
            Stop();
            return;
        }
        if (_starting) return;
        _app.Capture.PickRegion(rect => Start(rect, format, fullscreen: false));
    }

    /// <summary>Records the monitor under the mouse.</summary>
    public void ToggleFullscreen(RecordingFormat format)
    {
        if (IsRecording)
        {
            Stop();
            return;
        }
        Win32.GetCursorPos(out var p);
        Start(Monitors.Containing(Monitors.All(), p.X, p.Y), format, fullscreen: true);
    }

    private void Start(PixelRect rect, RecordingFormat format, bool fullscreen)
    {
        if (IsRecording || _starting) return;
        var min = format == RecordingFormat.Gif ? 16 : 32;
        if (rect.Width < min || rect.Height < min)
        {
            _tray.ShowBalloon("Too small to record", $"Pick an area of at least {min} × {min} pixels.");
            return;
        }
        // Picked a whole monitor (F key, or a maximized window filling it): no frame around it.
        fullscreen |= Monitors.All().Any(m => m.Contains(rect) && rect.Contains(m));
        _starting = true;
        var s = _app.Settings;
        var path = NextPath(format);
        var options = new RecordingOptions
        {
            Region = new ScreenRect(rect.X, rect.Y, rect.Width, rect.Height),
            OutputPath = path,
            Format = format,
            FramesPerSecond = s.RecordFramesPerSecond,
            GifFramesPerSecond = s.RecordGifFramesPerSecond,
            ShowCursor = s.RecordShowCursor,
            SystemAudio = s.RecordSystemAudio,
            Microphone = s.RecordMicrophone,
        };

        // The overlay just closed; give the screen one frame to repaint before the first video frame.
        _ = _app.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await Task.Delay(60);
                _recorder = await Task.Run(() => ScreenRecorder.Start(options));
                _recorder.Failed += ex => _app.Dispatcher.InvokeAsync(() =>
                {
                    _tray.ShowBalloon("Recording stopped", "Something went wrong while recording. What was recorded so far is being saved.");
                    Stop();
                });
            }
            catch (Exception ex)
            {
                Log.Error("Could not start recording", ex);
                _tray.ShowBalloon("Could not start recording", ex.Message);
                _starting = false;
                return;
            }
            _starting = false;
            ShowRecordingUi(rect, format, fullscreen);
            StateChanged?.Invoke();
        });
    }

    private void ShowRecordingUi(PixelRect rect, RecordingFormat format, bool fullscreen)
    {
        _icons ??= new RecordingIcons();
        _iconFrame = 0;
        _ticker.Start();
        Tick();

        if (!fullscreen)
        {
            _frame ??= new RecordingFrame();
            _frame.ShowAround(rect);
        }
        if (_pill is null)
        {
            _pill = new RecordingPill();
            _pill.StopClicked += Stop;
        }
        var kind = format == RecordingFormat.Gif ? "GIF"
            : _app.Settings.RecordMicrophone ? "MIC" : "";
        _pill.ShowFor(rect, kind);
        _app.SetTrayStatus($"Cometsnap: recording. Click to stop ({_app.Settings.RecordHotkey})");
    }

    private void Tick()
    {
        if (_recorder is null || _icons is null) return;
        _tray.SetIcon(_icons[_iconFrame++]);
        _pill?.SetElapsed(_recorder.Elapsed);
    }

    /// <summary>Stops the recording; the file is finished in the background.</summary>
    public void Stop()
    {
        var recorder = _recorder;
        if (recorder is null) return;
        _recorder = null;
        _ticker.Stop();
        _tray.SetIcon(IntPtr.Zero);
        _app.SetTrayStatus(null);
        _frame?.Hide();
        _pill?.HideNow();
        StateChanged?.Invoke();
        _ = FinishAsync(recorder);
    }

    private async Task FinishAsync(ScreenRecorder recorder)
    {
        try
        {
            var result = await recorder.StopAsync();
            AddToRecent(result.Path, recorder);
            Delivered(result);
        }
        catch (Exception ex)
        {
            Log.Error("Could not finish the recording", ex);
            _tray.ShowBalloon("Recording failed", ex.InnerException?.Message ?? ex.Message);
        }
    }

    /// <summary>Shows the recording in the flyout grid, with its poster frame as thumbnail.</summary>
    private void AddToRecent(string path, ScreenRecorder recorder)
    {
        try
        {
            if (recorder.PosterFrame is { } bgra)
            {
                var r = recorder.Region;
                var image = BitmapSource.Create(r.Width, r.Height, 96, 96, PixelFormats.Bgr32, null, bgra, r.Width * 4);
                image.Freeze();
                _app.Thumbnails.Prime(path, image);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not make the recording thumbnail", ex);
        }
        _app.Recent.Add(path);
    }

    private void Delivered(RecordingResult result)
    {
        var copied = _app.Settings.RecordCopyToClipboard && CopyFile(result.Path);
        var length = result.Duration.TotalHours >= 1 ? result.Duration.ToString(@"h\:mm\:ss") : result.Duration.ToString(@"m\:ss");
        var size = result.Bytes >= 1 << 20
            ? (result.Bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
            : Math.Max(1, result.Bytes / 1024) + " KB";
        var what = result.Format == RecordingFormat.Gif ? "GIF" : "Video";
        _tray.ShowBalloon($"{what} saved ({length}, {size})",
            (copied ? "Copied: paste it anywhere. " : "") + Path.GetFileName(result.Path));
        Log.Info($"Recording saved to {result.Path}");
    }

    private static bool CopyFile(string path)
    {
        for (var i = 0; i < 5; i++)
        {
            try
            {
                Clipboard.SetFileDropList(new StringCollection { path });
                return true;
            }
            catch (Exception)
            {
                Thread.Sleep(40);
            }
        }
        return false;
    }

    /// <summary><c>&lt;captures&gt;\2026-11\rec_2026-11-26_08-56-12.mp4</c></summary>
    private string NextPath(RecordingFormat format)
    {
        var now = DateTime.Now;
        var folder = Path.Combine(_app.CapturesFolder, now.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        var ext = format == RecordingFormat.Gif ? ".gif" : ".mp4";
        var name = "rec_" + now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        var path = Path.Combine(folder, name + ext);
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{name}_{i}{ext}");
        return path;
    }

    /// <summary>On quit: stop and wait for the file to be finished, so nothing is lost.</summary>
    public void Dispose()
    {
        var recorder = _recorder;
        _recorder = null;
        _ticker.Stop();
        if (recorder is not null)
        {
            try { recorder.StopAsync().Wait(TimeSpan.FromSeconds(15)); }
            catch (Exception ex) { Log.Error("Could not finish the recording on exit", ex); }
        }
        _tray.SetIcon(IntPtr.Zero);
        _icons?.Dispose();
    }
}
