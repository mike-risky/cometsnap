using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Cometsnap.Video.Editing;

namespace Cometsnap.App.Trimming;

/// <summary>What the trimmer shows: an MP4 through Windows' player, or a GIF drawn frame by frame.</summary>
internal interface IClipPreview : IDisposable
{
    UIElement View { get; }
    TimeSpan Position { get; set; }
    bool IsPlaying { get; }
    void Play();
    void Pause();

    /// <summary>The clip could not be shown (the message says why).</summary>
    event Action<string>? Failed;
}

/// <summary>MP4 preview with sound, through WPF's MediaElement.</summary>
internal sealed class VideoPreview : IClipPreview
{
    private readonly MediaElement _media;
    private TimeSpan _wanted;
    private bool _opened;

    public event Action<string>? Failed;

    public VideoPreview(string path)
    {
        _media = new MediaElement
        {
            LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual,
            ScrubbingEnabled = true, Stretch = Stretch.Uniform, Volume = 0.8,
        };
        RenderOptions.SetBitmapScalingMode(_media, BitmapScalingMode.HighQuality);
        _media.MediaOpened += (_, _) =>
        {
            _opened = true;
            // Play/pause once so the first picture shows instead of a black box.
            _media.Play();
            _media.Pause();
            _media.Position = _wanted;
        };
        _media.MediaEnded += (_, _) => { IsPlaying = false; _media.Pause(); };
        _media.MediaFailed += (_, e) => Failed?.Invoke(e.ErrorException?.Message ?? "");
        _media.Source = new Uri(path);
        _media.Play();
        _media.Pause();
    }

    public UIElement View => _media;
    public bool IsPlaying { get; private set; }

    public TimeSpan Position
    {
        get => _opened ? _media.Position : _wanted;
        set
        {
            _wanted = value;
            if (_opened) _media.Position = value;
        }
    }

    public void Play()
    {
        IsPlaying = true;
        _media.Play();
    }

    public void Pause()
    {
        IsPlaying = false;
        _media.Pause();
    }

    public void Dispose()
    {
        try
        {
            _media.Stop();
            _media.Close();
            _media.Source = null;
        }
        catch { /* closing anyway */ }
    }
}

/// <summary>GIF preview: frames decoded on demand and timed with a stopwatch.</summary>
internal sealed class GifPreview : IClipPreview
{
    private readonly GifReader _reader;
    private readonly WriteableBitmap _bitmap;
    private readonly Image _image;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private TimeSpan _from;
    private int _shown = -1;

    public event Action<string>? Failed { add { } remove { } }

    public GifPreview(GifReader reader)
    {
        _reader = reader;
        _bitmap = new WriteableBitmap(reader.Width, reader.Height, 96, 96, PixelFormats.Bgra32, null);
        _image = new Image { Source = _bitmap, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        _timer.Tick += (_, _) => Show(Position);
        Show(TimeSpan.Zero);
    }

    public UIElement View => _image;
    public bool IsPlaying => _clock.IsRunning;

    public TimeSpan Position
    {
        get
        {
            var t = _from + _clock.Elapsed;
            if (t >= _reader.Duration)
            {
                Pause();
                _from = _reader.Duration;
                return _reader.Duration;
            }
            return t;
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
        var pixels = _reader.Render(i);
        _bitmap.WritePixels(new Int32Rect(0, 0, _reader.Width, _reader.Height), pixels, _reader.Width * 4, 0);
    }

    public void Dispose() => _timer.Stop();
}
