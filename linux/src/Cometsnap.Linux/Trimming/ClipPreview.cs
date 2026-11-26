using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Platform;

namespace Cometsnap.Linux.Trimming;

/// <summary>What the trimmer shows: a GIF drawn frame by frame, or an MP4 decoded by ffmpeg.</summary>
internal interface IClipPreview : IDisposable
{
    Control View { get; }
    TimeSpan Position { get; set; }
    bool IsPlaying { get; }
    void Play();
    void Pause();

    /// <summary>The clip could not be shown (the message says why).</summary>
    event Action<string>? Failed;
}

/// <summary>A picture the preview paints into, scaled to fit.</summary>
internal sealed class PreviewSurface
{
    public PreviewSurface(int width, int height)
    {
        Width = width;
        Height = height;
        Bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        Image = new Image { Source = Bitmap, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapInterpolationMode(Image, BitmapInterpolationMode.HighQuality);
    }

    public int Width { get; }
    public int Height { get; }
    public WriteableBitmap Bitmap { get; }
    public Image Image { get; }

    public void Show(byte[] bgra)
    {
        using (var fb = Bitmap.Lock())
        {
            var row = Width * 4;
            if (fb.RowBytes == row) Marshal.Copy(bgra, 0, fb.Address, Math.Min(bgra.Length, row * Height));
            else
                for (var y = 0; y < Height; y++)
                    Marshal.Copy(bgra, y * row, fb.Address + y * fb.RowBytes, row);
        }
        Image.InvalidateVisual();
    }

    public void Show(uint[] bgra)
    {
        using (var fb = Bitmap.Lock())
        {
            var ints = MemoryMarshal.Cast<uint, int>(bgra.AsSpan());
            for (var y = 0; y < Height; y++)
            {
                var src = ints.Slice(y * Width, Width).ToArray();
                Marshal.Copy(src, 0, fb.Address + y * fb.RowBytes, Width);
            }
        }
        Image.InvalidateVisual();
    }
}

/// <summary>GIF preview: frames decoded on demand and timed with a stopwatch.</summary>
internal sealed class GifPreview : IClipPreview
{
    private readonly GifReader _reader;
    private readonly PreviewSurface _surface;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
    private readonly Stopwatch _clock = new();
    private TimeSpan _from;
    private int _shown = -1;

    public event Action<string>? Failed { add { } remove { } }

    public GifPreview(GifReader reader)
    {
        _reader = reader;
        _surface = new PreviewSurface(reader.Width, reader.Height);
        RenderOptions.SetBitmapInterpolationMode(_surface.Image, BitmapInterpolationMode.None);
        _timer.Tick += (_, _) => Show(Position);
        Show(TimeSpan.Zero);
    }

    public Control View => _surface.Image;
    public bool IsPlaying => _clock.IsRunning;

    public TimeSpan Position
    {
        get
        {
            var t = _from + _clock.Elapsed;
            if (t < _reader.Duration) return t;
            Pause();
            _from = _reader.Duration;
            return _reader.Duration;
        }
        set
        {
            _from = value < TimeSpan.Zero ? TimeSpan.Zero : value > _reader.Duration ? _reader.Duration : value;
            if (_clock.IsRunning) _clock.Restart();
            else _clock.Reset();
            Show(_from);
        }
    }

    public void Play()
    {
        if (_from >= _reader.Duration) _from = TimeSpan.Zero;
        _clock.Restart();
        _timer.Start();
    }

    public void Pause()
    {
        if (_clock.IsRunning) _from += _clock.Elapsed;
        _clock.Reset();
        _timer.Stop();
    }

    private void Show(TimeSpan t)
    {
        var i = _reader.FrameAt(t);
        if (i == _shown) return;
        _shown = i;
        _surface.Show(_reader.Render(i));
    }

    public void Dispose() => _timer.Stop();
}

/// <summary>
/// MP4 preview without a media framework: ffmpeg decodes scaled-down frames into a pipe.
/// Paused, each move of the playhead asks for one frame (only the latest wish is honoured);
/// playing, a stream of frames is shown against a clock. The preview has no sound.
/// </summary>
internal sealed class VideoPreview : IClipPreview
{
    private const int Fps = 30;
    private readonly string _source;
    private readonly string _ffmpeg;
    private readonly TimeSpan _duration;
    private readonly PreviewSurface _surface;
    private readonly int _frameBytes;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(12) };
    private readonly Stopwatch _clock = new();
    private readonly object _lock = new();
    private TimeSpan _from;
    private bool _playing;
    private bool _clockStarted;
    private int _generation;
    private Process? _stream;
    private BlockingCollection<(int Index, byte[] Pixels)>? _frames;
    private (int Index, byte[] Pixels)? _waiting;
    private bool _stillBusy;
    private TimeSpan? _stillWanted;
    private bool _disposed;
    private bool _failed;

    public event Action<string>? Failed;

    public VideoPreview(string source, string ffmpeg, ClipInfo info)
    {
        _source = source;
        _ffmpeg = ffmpeg;
        _duration = info.Duration;
        var (w, h) = PreviewSize(info.Width, info.Height);
        _surface = new PreviewSurface(w, h);
        _frameBytes = w * h * 4;
        _timer.Tick += (_, _) => OnTick();
        RequestStill(TimeSpan.Zero);
    }

    /// <summary>At most 1280 × 720, even sizes, same shape as the video.</summary>
    public static (int W, int H) PreviewSize(int width, int height)
    {
        if (width <= 0 || height <= 0) (width, height) = (1280, 720);
        var scale = Math.Min(1.0, Math.Min(1280.0 / width, 720.0 / height));
        var w = Math.Max(2, (int)Math.Round(width * scale / 2) * 2);
        var h = Math.Max(2, (int)Math.Round(height * scale / 2) * 2);
        return (w, h);
    }

    public Control View => _surface.Image;
    public bool IsPlaying => _playing;

    public TimeSpan Position
    {
        get
        {
            if (!_playing || !_clockStarted) return _from;
            var t = _from + _clock.Elapsed;
            return t < _duration ? t : _duration;
        }
        set
        {
            _from = value < TimeSpan.Zero ? TimeSpan.Zero : value > _duration ? _duration : value;
            if (_playing) StartStream();
            else RequestStill(_from);
        }
    }

    public void Play()
    {
        if (_from >= _duration) _from = TimeSpan.Zero;
        _playing = true;
        StartStream();
        _timer.Start();
    }

    public void Pause()
    {
        if (!_playing) return;
        _from = Position;
        _playing = false;
        _timer.Stop();
        StopStream();
    }

    // ------------------------------------------------------------------ playing

    private void StartStream()
    {
        StopStream();
        var generation = ++_generation;
        _clock.Reset();
        _clockStarted = false;
        _waiting = null;
        var frames = new BlockingCollection<(int, byte[])>(6);
        _frames = frames;
        var p = Launch(_from, single: false);
        if (p is null) return;
        _stream = p;
        var output = p.StandardOutput.BaseStream;
        new Thread(() =>
        {
            var index = 0;
            try
            {
                while (true)
                {
                    var buffer = new byte[_frameBytes];
                    if (!ReadExactly(output, buffer)) break;
                    if (generation != _generation) break;
                    frames.Add((index++, buffer));
                }
            }
            catch (Exception) { /* stream stopped */ }
            finally
            {
                try { frames.CompleteAdding(); } catch (ObjectDisposedException) { }
            }
        }) { IsBackground = true, Name = "trim-preview" }.Start();
    }

    private void StopStream()
    {
        _generation++;
        var p = _stream;
        _stream = null;
        if (p is not null)
        {
            try { if (!p.HasExited) p.Kill(); } catch { /* gone */ }
            p.Dispose();
        }
        var frames = _frames;
        _frames = null;
        if (frames is not null)
        {
            // Unblock the reader if it waits for room, then let it finish.
            while (frames.TryTake(out _)) { }
        }
    }

    private void OnTick()
    {
        if (!_playing || _frames is not { } frames) return;
        if (!_clockStarted)
        {
            // The clock starts with the first frame, so ffmpeg's start-up time isn't skipped over.
            if (!frames.TryTake(out var first)) { if (frames.IsCompleted) EndReached(); return; }
            _surface.Show(first.Pixels);
            _clock.Restart();
            _clockStarted = true;
            return;
        }
        var due = (int)(_clock.Elapsed.TotalSeconds * Fps);
        (int Index, byte[] Pixels)? show = null;
        if (_waiting is { } w)
        {
            if (w.Index > due) return;
            show = w;
            _waiting = null;
        }
        while (frames.TryTake(out var f))
        {
            if (f.Index > due) { _waiting = f; break; }
            show = f;
        }
        if (show is { } s) _surface.Show(s.Pixels);
        else if (frames.IsCompleted && _waiting is null) EndReached();
    }

    private void EndReached()
    {
        _from = _duration;
        _playing = false;
        _timer.Stop();
        StopStream();
    }

    // ------------------------------------------------------------------ paused

    private void RequestStill(TimeSpan at)
    {
        _stillWanted = at;
        if (_stillBusy) return;
        _ = StillLoopAsync();
    }

    private async Task StillLoopAsync()
    {
        _stillBusy = true;
        try
        {
            while (_stillWanted is { } at && !_disposed)
            {
                _stillWanted = null;
                // Near the very end there may be no frame left after the seek: step back a little.
                var seek = at >= _duration - TimeSpan.FromMilliseconds(60) ? _duration - TimeSpan.FromMilliseconds(80) : at;
                if (seek < TimeSpan.Zero) seek = TimeSpan.Zero;
                var pixels = await Task.Run(() => DecodeOne(seek));
                if (_disposed) return;
                if (pixels is not null && !_playing && _stillWanted is null) _surface.Show(pixels);
                else if (pixels is null && !_failed && _stillWanted is null)
                {
                    _failed = true;
                    Failed?.Invoke("ffmpeg returned no picture");
                }
            }
        }
        finally
        {
            _stillBusy = false;
        }
    }

    private byte[]? DecodeOne(TimeSpan at)
    {
        using var p = Launch(at, single: true);
        if (p is null) return null;
        var buffer = new byte[_frameBytes];
        var ok = ReadExactly(p.StandardOutput.BaseStream, buffer);
        try { if (!p.WaitForExit(3000)) p.Kill(); } catch { /* gone */ }
        return ok ? buffer : null;
    }

    private Process? Launch(TimeSpan at, bool single)
    {
        var psi = new ProcessStartInfo(_ffmpeg) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = false };
        var args = new List<string> { "-hide_banner", "-nostdin", "-v", "quiet", "-ss", at.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture), "-i", _source, "-an", "-sn" };
        args.AddRange(single
            ? new[] { "-frames:v", "1", "-vf", $"scale={_surface.Width}:{_surface.Height}:flags=bilinear" }
            : new[] { "-vf", $"fps={Fps},scale={_surface.Width}:{_surface.Height}:flags=bilinear" });
        args.AddRange(new[] { "-pix_fmt", "bgra", "-f", "rawvideo", "pipe:1" });
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            return Process.Start(psi);
        }
        catch (Exception ex)
        {
            Log.Error("Trim preview: ffmpeg did not start", ex);
            if (!_failed) { _failed = true; Failed?.Invoke(ex.Message); }
            return null;
        }
    }

    private static bool ReadExactly(Stream s, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = s.Read(buffer, read, buffer.Length - read);
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }

    public void Dispose()
    {
        _disposed = true;
        _playing = false;
        _timer.Stop();
        StopStream();
    }
}
