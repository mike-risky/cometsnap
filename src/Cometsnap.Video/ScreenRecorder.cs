using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Cometsnap.Video.Audio;
using Cometsnap.Video.Capture;
using Cometsnap.Video.Encoding;

namespace Cometsnap.Video;

/// <summary>
/// Records a region of the screen to MP4 (H.264 + AAC) or GIF. <see cref="Start"/> sets
/// everything up and returns once frames are flowing; <see cref="StopAsync"/> finishes the file.
/// Capture, audio and GIF encoding each run on their own background thread.
/// </summary>
public sealed class ScreenRecorder : IDisposable
{
    private const double AudioLagSeconds = 0.2; // read audio slightly behind real time so buffers never run dry

    private readonly RecordingOptions _options;
    private readonly ScreenRect _region;
    private readonly Stopwatch _clock = new();
    private readonly CursorPainter _cursor = new();
    private readonly FrameCanvas _canvas;
    private IFrameSource _source;

    private readonly Mp4Writer? _mp4;
    private readonly int _scale = 1;
    private readonly IntPtr _scaled;
    private readonly AudioRecorder? _audio;
    private long _audioFrames;

    private readonly GifWriter? _gif;
    private readonly BlockingCollection<(byte[] Pixels, TimeSpan Time)>? _gifQueue;
    private readonly ConcurrentBag<byte[]> _gifPool = new();
    private Thread? _gifThread;

    private Thread? _videoThread;
    private Thread? _audioThread;
    private volatile bool _stopping;
    private Exception? _failure;
    private TimeSpan _duration;
    private Task<RecordingResult>? _stopTask;

    /// <summary>Raised (on a background thread) if recording breaks; call <see cref="StopAsync"/> to keep what was recorded.</summary>
    public event Action<Exception>? Failed;

    public TimeSpan Elapsed => _stopping ? _duration : _clock.Elapsed;
    public bool IsRunning => !_stopping;
    public RecordingFormat Format => _options.Format;
    public ScreenRect Region => _region;
    public string CaptureMethod => _source.Name;

    private ScreenRecorder(RecordingOptions options)
    {
        _options = options;
        _region = options.Format == RecordingFormat.Mp4 ? EncoderMath.EvenSize(options.Region) : options.Region;
        _source = (IFrameSource?)DuplicationFrameSource.TryCreate(_region) ?? new GdiFrameSource(_region);
        _canvas = new FrameCanvas(_region.Width, _region.Height);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);

        try
        {
            if (options.Format == RecordingFormat.Mp4)
            {
                _scale = EncoderMath.DownscaleFactor(_region.Width, _region.Height);
                var w = (_region.Width / _scale) & ~1;
                var h = (_region.Height / _scale) & ~1;
                if (_scale > 1) _scaled = Marshal.AllocHGlobal(w * h * 4);

                if (options.SystemAudio || options.Microphone)
                {
                    _audio = new AudioRecorder(options.SystemAudio, options.Microphone);
                    if (!_audio.IsActive)
                    {
                        _audio.Dispose();
                        _audio = null;
                    }
                }
                var fps = Math.Clamp(options.FramesPerSecond, 5, 60);
                var bitrate = options.VideoBitrate > 0 ? options.VideoBitrate : EncoderMath.DefaultBitrate(w, h, fps);
                _mp4 = Mp4Writer.Create(options.OutputPath, w, h, fps, bitrate, _audio is not null);
            }
            else
            {
                _gif = new GifWriter(new FileStream(options.OutputPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16),
                    _region.Width, _region.Height);
                _gifQueue = new BlockingCollection<(byte[], TimeSpan)>(boundedCapacity: 8);
            }
        }
        catch
        {
            DisposeResources();
            throw;
        }
        VideoLog.I($"Recording {_region.Width}×{_region.Height} at ({_region.X},{_region.Y}) to {options.Format} using {_source.Name}");
    }

    /// <summary>Starts recording right away. Throws if the file or the encoder cannot be set up.</summary>
    public static ScreenRecorder Start(RecordingOptions options)
    {
        var recorder = new ScreenRecorder(options);
        recorder.Run();
        return recorder;
    }

    private void Run()
    {
        _audio?.Start();
        _clock.Start();
        _videoThread = new Thread(VideoLoop) { IsBackground = true, Name = "Cometsnap video", Priority = ThreadPriority.AboveNormal };
        _videoThread.Start();
        if (_audio is not null)
        {
            _audioThread = new Thread(AudioLoop) { IsBackground = true, Name = "Cometsnap audio", Priority = ThreadPriority.AboveNormal };
            _audioThread.Start();
        }
        if (_gif is not null)
        {
            _gifThread = new Thread(GifLoop) { IsBackground = true, Name = "Cometsnap GIF", Priority = ThreadPriority.BelowNormal };
            _gifThread.Start();
        }
    }

    // ------------------------------------------------------------------ video

    private void VideoLoop()
    {
        var fps = _options.Format == RecordingFormat.Gif
            ? Math.Clamp(_options.GifFramesPerSecond, 2, 30)
            : Math.Clamp(_options.FramesPerSecond, 5, 60);
        var interval = 1.0 / fps;
        Native.timeBeginPeriod(1);
        try
        {
            long frame = 0;
            while (!_stopping)
            {
                var due = frame * interval;
                var wait = due - _clock.Elapsed.TotalSeconds;
                if (wait > 0.002) Thread.Sleep(TimeSpan.FromSeconds(wait - 0.001));
                while (_clock.Elapsed.TotalSeconds < due) Thread.SpinWait(50);
                if (_stopping) break;

                var now = _clock.Elapsed;
                CaptureFrame();
                Deliver(now);

                // Fell behind (slow encoder or huge region)? Skip ahead instead of bursting.
                frame++;
                var behind = (long)(_clock.Elapsed.TotalSeconds / interval);
                if (behind > frame) frame = behind;
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            Native.timeEndPeriod(1);
        }
    }

    private void CaptureFrame()
    {
        if (!_source.Grab(_canvas))
        {
            VideoLog.I($"{_source.Name} capture stopped working, switching to GDI");
            _source.Dispose();
            _source = new GdiFrameSource(_region);
            _source.Grab(_canvas);
        }
        if (_options.ShowCursor) _cursor.Paint(_canvas, _region.X, _region.Y);
    }

    private void Deliver(TimeSpan time)
    {
        if (_mp4 is not null)
        {
            var pixels = _canvas.Bits;
            if (_scale > 1)
            {
                Downscale(_canvas, _scale, _scaled);
                pixels = _scaled;
            }
            _mp4.WriteVideo(pixels, time.Ticks); // TimeSpan ticks are 100 ns, like Media Foundation
        }
        else if (_gifQueue is not null)
        {
            if (!_gifPool.TryTake(out var copy) || copy.Length != _canvas.Length) copy = new byte[_canvas.Length];
            _canvas.Span.CopyTo(copy);
            if (!_gifQueue.TryAdd((copy, time))) _gifPool.Add(copy); // encoder busy: drop this frame
        }
    }

    /// <summary>Box filter by an integer factor, into an even-sized BGRA buffer.</summary>
    private static unsafe void Downscale(FrameCanvas src, int f, IntPtr dst)
    {
        var w = (src.Width / f) & ~1;
        var h = (src.Height / f) & ~1;
        var s = (byte*)src.Bits;
        var d = (byte*)dst;
        var n = f * f;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                int b = 0, g = 0, r = 0;
                for (var yy = 0; yy < f; yy++)
                {
                    var p = s + (long)(y * f + yy) * src.Stride + x * f * 4;
                    for (var xx = 0; xx < f; xx++, p += 4)
                    {
                        b += p[0]; g += p[1]; r += p[2];
                    }
                }
                var o = d + ((long)y * w + x) * 4;
                o[0] = (byte)(b / n); o[1] = (byte)(g / n); o[2] = (byte)(r / n); o[3] = 255;
            }
        }
    }

    // ------------------------------------------------------------------ audio

    private void AudioLoop()
    {
        const int rate = Mp4Writer.AudioSampleRate;
        var floats = new float[rate * 2];
        var shorts = new short[rate * 2];
        try
        {
            while (!_stopping)
            {
                Thread.Sleep(10);
                PumpAudio(_clock.Elapsed.TotalSeconds - AudioLagSeconds, floats, shorts);
            }
            PumpAudio(_duration.TotalSeconds, floats, shorts);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private void PumpAudio(double untilSeconds, float[] floats, short[] shorts)
    {
        const int rate = Mp4Writer.AudioSampleRate;
        var target = (long)(untilSeconds * rate);
        while (_audioFrames < target)
        {
            var frames = (int)Math.Min(target - _audioFrames, rate);
            var count = frames * Mp4Writer.AudioChannels;
            _audio!.Read(floats, count);
            PcmConvert.ToInt16(floats.AsSpan(0, count), shorts);
            _mp4!.WriteAudio(shorts.AsSpan(0, count), _audioFrames * 10_000_000L / rate);
            _audioFrames += frames;
        }
    }

    // ------------------------------------------------------------------ GIF

    private void GifLoop()
    {
        try
        {
            foreach (var (pixels, time) in _gifQueue!.GetConsumingEnumerable())
            {
                _gif!.AddFrame(pixels, time);
                _gifPool.Add(pixels);
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    // ------------------------------------------------------------------ stop

    private void Fail(Exception ex)
    {
        if (_failure is not null) return;
        _failure = ex;
        VideoLog.E("Recording failed", ex);
        Failed?.Invoke(ex);
    }

    /// <summary>Stops and finishes the file (in the background). Safe to call more than once.</summary>
    public Task<RecordingResult> StopAsync()
    {
        lock (_clock)
        {
            if (_stopTask is not null) return _stopTask;
            _duration = _clock.Elapsed;
            _stopping = true;
            _stopTask = Task.Run(Finish);
            return _stopTask;
        }
    }

    private RecordingResult Finish()
    {
        _videoThread?.Join();
        _audioThread?.Join();
        _gifQueue?.CompleteAdding();
        _gifThread?.Join();
        try
        {
            _mp4?.Finish();
            _gif?.Finish(_duration);
        }
        finally
        {
            DisposeResources();
        }
        var bytes = File.Exists(_options.OutputPath) ? new FileInfo(_options.OutputPath).Length : 0;
        VideoLog.I($"Recording finished: {_duration:mm\\:ss\\.f}, {bytes / 1024} KB{(_gif is not null ? $", {_gif.FramesWritten} GIF frames" : "")}");
        if (bytes == 0 && _failure is not null) throw new InvalidOperationException("The recording could not be saved.", _failure);
        return new RecordingResult(_options.OutputPath, _duration, bytes, _options.Format, _audio is not null);
    }

    private int _disposed;

    private void DisposeResources()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _audio?.Dispose();
        _mp4?.Dispose();
        _gif?.Dispose();
        _gifQueue?.Dispose();
        _source.Dispose();
        _canvas.Dispose();
        if (_scaled != IntPtr.Zero) Marshal.FreeHGlobal(_scaled);
    }

    public void Dispose()
    {
        if (_stopTask is null) StopAsync().Wait();
        else _stopTask.Wait();
    }
}
