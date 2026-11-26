using System.Globalization;
using Avalonia.Threading;
using Cometsnap.Linux.Capture;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Platform;
using PixelRect = Cometsnap.Linux.Core.PixelRect;

namespace Cometsnap.Linux.Recording;

/// <summary>
/// "Transmissions": pick a window or an area with the usual capture overlay, recording starts at
/// once; stop with the same hotkey, the pill, or the tray. The result is copied as a file and shows
/// up in the flyout.
/// </summary>
public sealed class RecordingController
{
    private readonly CometApp _app;
    private ScreenRecorder? _recorder;
    private RecordingPill? _pill;
    private DispatcherTimer? _tick;
    private RecordingFormat _format;
    private bool _starting;

    public RecordingController(CometApp app) => _app = app;

    public bool IsRecording => _recorder is not null;

    public event Action<bool>? StateChanged;

    public void ToggleRegion(RecordingFormat format)
    {
        if (IsRecording) { _ = StopAsync(); return; }
        _ = StartRegionAsync(format);
    }

    public void ToggleFullscreen(RecordingFormat format)
    {
        if (IsRecording) { _ = StopAsync(); return; }
        var monitors = _app.Capture.Monitors();
        var pointer = X11.PointerPosition();
        var monitor = monitors.FirstOrDefault(m => pointer is { } p && m.Contains(p.X, p.Y), monitors[0]);
        _ = StartAsync(monitor, monitor, format);
    }

    public void Stop()
    {
        if (IsRecording) _ = StopAsync();
    }

    private async Task StartRegionAsync(RecordingFormat format)
    {
        if (_starting) return;
        if (ScreenRecorder.MissingToolMessage() is { } missing) { _app.Toast(missing, 7000); return; }
        var picked = await _app.Capture.PickAsync(OverlayPurpose.Record);
        if (picked is not { } p) return;
        p.Screen.Dispose();
        var monitors = _app.Capture.Monitors();
        var monitor = monitors.OrderByDescending(m => Area(m.Intersect(p.Rect))).First();
        // A recording stays on one monitor.
        var region = p.Rect.Intersect(monitor);
        await StartAsync(region, monitor, format);
    }

    private static long Area(PixelRect r) => (long)r.Width * r.Height;

    private async Task StartAsync(PixelRect region, PixelRect monitor, RecordingFormat format)
    {
        if (_starting || IsRecording) return;
        if (ScreenRecorder.MissingToolMessage() is { } missing) { _app.Toast(missing, 7000); return; }
        _starting = true;
        var recorder = new ScreenRecorder();
        try
        {
            var s = _app.Settings;
            await recorder.StartAsync(new RecordingOptions
            {
                Region = region,
                OutputPath = NextPath(format),
                Format = format,
                FramesPerSecond = s.RecordFramesPerSecond,
                GifFramesPerSecond = s.RecordGifFramesPerSecond,
                ShowCursor = s.RecordShowCursor,
                SystemAudio = s.RecordSystemAudio,
                Microphone = s.RecordMicrophone,
                RestoreToken = s.ScreenCastRestoreToken,
            });
            if (recorder.NewRestoreToken is { Length: > 0 } token && token != s.ScreenCastRestoreToken)
            {
                s.ScreenCastRestoreToken = token;
                _app.SaveSettings();
            }
            _recorder = recorder;
            _format = format;
            _pill ??= CreatePill();
            _pill.ShowFor(region, monitor, format == RecordingFormat.Gif ? "GIF" : "MP4", _app.ScalingAt(monitor));
            _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => _pill?.SetElapsed(recorder.Elapsed));
            _tick.Start();
            StateChanged?.Invoke(true);
            Log.Info($"Recording {format} {region}");
        }
        catch (OperationCanceledException)
        {
            await recorder.DisposeAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Recording failed to start", ex);
            await recorder.DisposeAsync();
            _app.Toast(ex.Message, 7000);
        }
        finally
        {
            _starting = false;
        }
    }

    private RecordingPill CreatePill()
    {
        var pill = new RecordingPill();
        pill.StopClicked += Stop;
        return pill;
    }

    private async Task StopAsync()
    {
        var recorder = _recorder;
        if (recorder is null) return;
        _recorder = null;
        _tick?.Stop();
        _pill?.HideNow();
        StateChanged?.Invoke(false);
        if (_format == RecordingFormat.Gif) _app.Toast(T("Making the GIF…"), 2500);
        try
        {
            var result = await recorder.StopAsync();
            var copied = _app.Settings.RecordCopyToClipboard && await _app.Clipboard.SetFilesAsync(new[] { result.Path });
            _app.Recent.Add(result.Path);
            var mb = result.Bytes / 1024.0 / 1024.0;
            _app.Toast((copied ? T("Copied: paste it anywhere.") + " " : T("Saved:") + " ") + $"{Path.GetFileName(result.Path)} · {result.Duration:m\\:ss} · {mb:0.#} MB", 5000);
        }
        catch (Exception ex)
        {
            Log.Error("Recording failed", ex);
            _app.Toast(ex.Message, 7000);
        }
        finally
        {
            await recorder.DisposeAsync();
        }
    }

    /// <summary><c>&lt;captures&gt;/2026-11/rec_2026-11-26_08-56-12.mp4</c></summary>
    private string NextPath(RecordingFormat format)
    {
        var now = DateTime.Now;
        var ext = format == RecordingFormat.Gif ? ".gif" : ".mp4";
        var folder = Path.Combine(_app.CapturesFolder, now.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        var name = "rec_" + now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        var path = Path.Combine(folder, name + ext);
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{name}_{i}{ext}");
        return path;
    }
}
