using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Platform;
using PixelRect = Cometsnap.Linux.Core.PixelRect;

namespace Cometsnap.Linux.Recording;

public enum RecordingFormat
{
    Mp4,
    Gif,
}

public sealed record RecordingOptions
{
    public required PixelRect Region { get; init; }
    public required string OutputPath { get; init; }
    public RecordingFormat Format { get; init; } = RecordingFormat.Mp4;
    public int FramesPerSecond { get; init; } = 30;
    public int GifFramesPerSecond { get; init; } = 15;
    public bool ShowCursor { get; init; } = true;
    public bool SystemAudio { get; init; } = true;
    public bool Microphone { get; init; }
    public string? RestoreToken { get; init; }
}

public sealed record RecordingResult(string Path, TimeSpan Duration, long Bytes, RecordingFormat Format, bool HasAudio);

/// <summary>
/// Records the screen with ffmpeg (H.264 + AAC in MP4, or a high-quality palette GIF).
/// X11: ffmpeg grabs the region itself (x11grab). Wayland: the screen-cast portal hands us a
/// PipeWire stream, GStreamer turns it into raw frames and pipes them to ffmpeg.
/// Sound: what plays on the computer (the default output's monitor) and optionally the microphone,
/// through PulseAudio / PipeWire.
/// </summary>
public sealed class ScreenRecorder : IAsyncDisposable
{
    private Process? _ffmpeg;
    private IntPtr _gst;
    private Portal.CastSession? _cast;
    private readonly List<Process> _audioHelpers = new();
    private readonly List<string> _fifos = new();
    private readonly Stopwatch _clock = new();
    private readonly StringBuilder _log = new();
    private RecordingOptions? _options;
    private string? _videoPath;
    private bool _hasAudio;

    public TimeSpan Elapsed => _clock.Elapsed;
    public bool IsRecording => _ffmpeg is { HasExited: false };

    /// <summary>A new restore token from the portal (Wayland), to save for the next recording.</summary>
    public string? NewRestoreToken { get; private set; }

    public static string? MissingToolMessage()
    {
        if (Tools.Ffmpeg is null) return T("Recording needs ffmpeg. Install it (for example: sudo apt install ffmpeg) and try again.");
        if (DesktopSession.IsWayland && !ScreenGrabber.ForceX11 && !GStreamer.HasElement("pipewiresrc"))
            return T("Recording on Wayland needs the GStreamer PipeWire plugin (gstreamer1.0-pipewire / gst-plugin-pipewire).");
        return null;
    }

    public async Task StartAsync(RecordingOptions options)
    {
        _options = options;
        var ffmpeg = Tools.Ffmpeg ?? throw new InvalidOperationException(MissingToolMessage());
        var gif = options.Format == RecordingFormat.Gif;
        _videoPath = gif ? Path.Combine(Path.GetTempPath(), $"cometsnap-{Environment.ProcessId}-{DateTime.Now.Ticks}.mp4") : options.OutputPath;
        Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);

        var fps = gif ? options.GifFramesPerSecond : options.FramesPerSecond;
        var r = options.Region;
        var w = r.Width / 2 * 2;
        var h = r.Height / 2 * 2;
        if (w < 2 || h < 2) throw new ArgumentException("The area is too small to record.");

        var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-y", "-nostdin" };
        var filters = new List<string>();
        var inputs = 0;

        if (DesktopSession.IsWayland && !ScreenGrabber.ForceX11)
        {
            var (fifo, crop) = await StartWaylandVideoAsync(options, fps);
            args.AddRange(new[] { "-thread_queue_size", "512", "-f", "yuv4mpegpipe", "-i", fifo });
            if (crop is not null) filters.Add(crop);
        }
        else
        {
            var display = Environment.GetEnvironmentVariable("DISPLAY") ?? ":0";
            args.AddRange(new[]
            {
                "-thread_queue_size", "512", "-f", "x11grab", "-framerate", fps.ToString(CultureInfo.InvariantCulture),
                "-video_size", $"{w}x{h}", "-draw_mouse", options.ShowCursor ? "1" : "0",
                "-i", $"{display}+{r.X},{r.Y}",
            });
        }
        inputs++;

        var audioInputs = new List<int>();
        if (!gif)
        {
            if (options.SystemAudio && AddAudioInput(args, ffmpeg, "@DEFAULT_MONITOR@")) audioInputs.Add(inputs++);
            if (options.Microphone && AddAudioInput(args, ffmpeg, "@DEFAULT_SOURCE@")) audioInputs.Add(inputs++);
        }
        _hasAudio = audioInputs.Count > 0;

        filters.Add("scale=trunc(iw/2)*2:trunc(ih/2)*2");
        if (audioInputs.Count == 2)
        {
            args.AddRange(new[] { "-filter_complex", $"[0:v]{string.Join(",", filters)}[v];[{audioInputs[0]}:a][{audioInputs[1]}:a]amix=inputs=2:duration=first:dropout_transition=0[a]", "-map", "[v]", "-map", "[a]" });
        }
        else
        {
            args.AddRange(new[] { "-vf", string.Join(",", filters), "-map", "0:v" });
            if (audioInputs.Count == 1) args.AddRange(new[] { "-map", $"{audioInputs[0]}:a" });
        }

        args.AddRange(VideoEncoderArgs(ffmpeg, gif));
        args.AddRange(new[] { "-r", fps.ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p" });
        if (_hasAudio) args.AddRange(new[] { "-c:a", "aac", "-b:a", "160k" });
        args.AddRange(new[] { "-movflags", "+faststart", _videoPath });

        var psi = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true };
        // -nostdin is not used: 'q' on stdin is the clean way to stop ffmpeg.
        args.Remove("-nostdin");
        foreach (var a in args) psi.ArgumentList.Add(a);
        Log.Info("ffmpeg " + string.Join(' ', args));
        _ffmpeg = Process.Start(psi) ?? throw new InvalidOperationException("Could not start ffmpeg.");
        _ffmpeg.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (_log) if (_log.Length < 20000) _log.AppendLine(e.Data); };
        _ffmpeg.OutputDataReceived += (_, _) => { };
        _ffmpeg.BeginErrorReadLine();
        _ffmpeg.BeginOutputReadLine();
        _clock.Restart();

        // A failure to open the screen shows up within a moment.
        await Task.Delay(400);
        if (_ffmpeg.HasExited)
        {
            var log = _log.ToString();
            await CleanupAsync();
            throw new InvalidOperationException("Recording could not start: " + LastLines(log));
        }
    }

    /// <summary>Stops, finishes the file (and the GIF), and returns what was recorded.</summary>
    public async Task<RecordingResult> StopAsync()
    {
        var options = _options ?? throw new InvalidOperationException("Not recording.");
        var duration = _clock.Elapsed;
        _clock.Stop();

        // Wayland: end the GStreamer stream first, so ffmpeg sees the end of the video.
        if (_gst != IntPtr.Zero)
        {
            var gst = _gst;
            _gst = IntPtr.Zero;
            await Task.Run(() => GStreamer.Stop(gst));
        }
        foreach (var helper in _audioHelpers) TryKill(helper);

        if (_ffmpeg is { HasExited: false } p)
        {
            try
            {
                await p.StandardInput.WriteAsync("q");
                await p.StandardInput.FlushAsync();
                p.StandardInput.Close();
            }
            catch (IOException) { /* already closing */ }
            var exited = await Task.WhenAny(p.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(15)));
            if (!p.HasExited) TryKill(p);
        }
        var log = _log.ToString();
        await CleanupAsync();

        if (_videoPath is null || !File.Exists(_videoPath) || new FileInfo(_videoPath).Length == 0)
            throw new InvalidOperationException("The recording is empty. " + LastLines(log));

        var output = _videoPath;
        if (options.Format == RecordingFormat.Gif)
        {
            await MakeGifAsync(_videoPath, options.OutputPath, options.GifFramesPerSecond);
            try { File.Delete(_videoPath); } catch { /* temp */ }
            output = options.OutputPath;
        }
        return new RecordingResult(output, duration, new FileInfo(output).Length, options.Format, _hasAudio);
    }

    private static async Task MakeGifAsync(string video, string gif, int fps)
    {
        var ffmpeg = Tools.Ffmpeg!;
        var psi = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in new[]
                 {
                     "-hide_banner", "-loglevel", "error", "-y", "-i", video, "-vf",
                     $"fps={fps},scale='min(iw,1600)':-1:flags=lanczos,split[s0][s1];[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle",
                     "-loop", "0", gif,
                 })
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0 || !File.Exists(gif)) throw new InvalidOperationException("Making the GIF failed: " + LastLines(err));
    }

    // ------------------------------------------------------------------ Wayland video

    private async Task<(string Fifo, string? Crop)> StartWaylandVideoAsync(RecordingOptions options, int fps)
    {
        var portal = await Portal.GetAsync();
        var cast = await portal.StartScreenCastAsync(options.ShowCursor, options.RestoreToken)
                   ?? throw new OperationCanceledException("Screen sharing was cancelled.");
        _cast = cast;
        NewRestoreToken = cast.RestoreToken;
        var stream = cast.Streams.FirstOrDefault() ?? throw new InvalidOperationException("The desktop shared no screen.");

        var fifo = MakeFifo("video");
        var fd = cast.PipeWireFd is { IsInvalid: false } h ? $" fd={h.DangerousGetHandle()}" : "";
        var pipeline = $"pipewiresrc{fd} path={stream.NodeId} do-timestamp=true keepalive-time=1000 always-copy=true ! " +
                       $"videoconvert ! videorate ! video/x-raw,format=I420,framerate={fps}/1 ! y4menc ! filesink location=\"{fifo}\" sync=false";
        Log.Info("gst " + pipeline);

        // filesink blocks opening the FIFO until ffmpeg reads it: start it on a worker.
        var element = GStreamer.Launch(pipeline);
        _gst = element;
        _ = Task.Run(() => GStreamer.gst_element_set_state(element, GStreamer.StatePlaying));

        // Crop to the picked area, in proportions of the shared monitor (its pixels may be scaled).
        string? crop = null;
        var r = options.Region;
        if (stream.Width > 0 && stream.Height > 0)
        {
            var mon = new PixelRect(stream.X, stream.Y, stream.Width, stream.Height);
            var full = r.Width >= mon.Width - 2 && r.Height >= mon.Height - 2;
            if (!full)
            {
                string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
                var x = (double)(r.X - mon.X) / mon.Width;
                var y = (double)(r.Y - mon.Y) / mon.Height;
                var cw = (double)r.Width / mon.Width;
                var ch = (double)r.Height / mon.Height;
                crop = $"crop=trunc(iw*{F(cw)}/2)*2:trunc(ih*{F(ch)}/2)*2:trunc(iw*{F(Math.Max(0, x))}):trunc(ih*{F(Math.Max(0, y))})";
            }
        }
        return (fifo, crop);
    }

    // ------------------------------------------------------------------ audio

    private static bool? _ffmpegHasPulse;

    /// <summary>True when a PulseAudio (or pipewire-pulse) server is reachable, so sound can be recorded.</summary>
    private static bool SoundServerRunning()
    {
        var server = Environment.GetEnvironmentVariable("PULSE_SERVER");
        if (!string.IsNullOrEmpty(server)) return true;
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(runtime)) return false;
        return File.Exists(Path.Combine(runtime, "pulse", "native")) || File.Exists(Path.Combine(runtime, "pipewire-0"));
    }

    private bool AddAudioInput(List<string> args, string ffmpeg, string device)
    {
        if (!SoundServerRunning())
        {
            Log.Info("No sound server found: recording without sound");
            return false;
        }
        _ffmpegHasPulse ??= FfmpegSupports(ffmpeg, "-devices", " pulse ");
        if (_ffmpegHasPulse == true)
        {
            args.AddRange(new[] { "-thread_queue_size", "1024", "-f", "pulse", "-i", device });
            return true;
        }
        // The bundled static ffmpeg has no PulseAudio input: pipe raw sound from parec / pw-record.
        var helper = DesktopSession.Which("parec") is { } parec
            ? (parec, $"--device={device} --format=s16le --rate=48000 --channels=2 --raw")
            : DesktopSession.Which("pw-record") is { } pw
                ? (pw, $"--target={(device == "@DEFAULT_MONITOR@" ? "@DEFAULT_MONITOR@" : "@DEFAULT_SOURCE@")} --format=s16 --rate=48000 --channels=2 -")
                : default;
        if (helper.Item1 is null) return false;
        try
        {
            var fifo = MakeFifo("audio");
            var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, RedirectStandardError = true };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"exec \"{helper.Item1}\" {helper.Item2} > \"{fifo}\"");
            var p = Process.Start(psi);
            if (p is null) return false;
            _audioHelpers.Add(p);
            args.AddRange(new[] { "-thread_queue_size", "1024", "-f", "s16le", "-ar", "48000", "-ac", "2", "-i", fifo });
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Audio capture helper failed", ex);
            return false;
        }
    }

    // ------------------------------------------------------------------ helpers

    private static readonly Dictionary<string, bool> EncoderCache = new();

    private static IEnumerable<string> VideoEncoderArgs(string ffmpeg, bool forGif)
    {
        if (!EncoderCache.TryGetValue("x264", out var x264)) EncoderCache["x264"] = x264 = FfmpegSupports(ffmpeg, "-encoders", "libx264");
        if (x264)
            return forGif
                ? new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", "16", "-tune", "animation" }
                : new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", "22", "-tune", "zerolatency" };
        if (!EncoderCache.TryGetValue("openh264", out var oh)) EncoderCache["openh264"] = oh = FfmpegSupports(ffmpeg, "-encoders", "libopenh264");
        if (oh) return new[] { "-c:v", "libopenh264", "-b:v", "8M" };
        return new[] { "-c:v", "mpeg4", "-q:v", "3" };
    }

    private static bool FfmpegSupports(string ffmpeg, string listFlag, string needle)
    {
        try
        {
            var psi = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add(listFlag);
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return output.Contains(needle, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int mkfifo(string path, uint mode);

    private string MakeFifo(string kind)
    {
        var dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Path.GetTempPath();
        var path = Path.Combine(dir, $"cometsnap-{Environment.ProcessId}-{kind}-{_fifos.Count}.fifo");
        if (File.Exists(path)) File.Delete(path);
        if (mkfifo(path, 0x180 /* 0600 */) != 0) throw new IOException($"mkfifo failed ({Marshal.GetLastWin32Error()})");
        _fifos.Add(path);
        return path;
    }

    private static void TryKill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        catch { /* gone */ }
    }

    private async Task CleanupAsync()
    {
        if (_gst != IntPtr.Zero)
        {
            var gst = _gst;
            _gst = IntPtr.Zero;
            await Task.Run(() => GStreamer.Stop(gst));
        }
        foreach (var h in _audioHelpers) TryKill(h);
        _audioHelpers.Clear();
        if (_cast is not null)
        {
            try { await (await Portal.GetAsync()).CloseSessionAsync(_cast.Handle); } catch { /* closed */ }
            _cast.PipeWireFd?.Dispose();
            _cast = null;
        }
        foreach (var f in _fifos)
        {
            try { File.Delete(f); } catch { /* ignore */ }
        }
        _fifos.Clear();
        _ffmpeg?.Dispose();
        _ffmpeg = null;
    }

    private static string LastLines(string log)
    {
        var lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" ", lines.TakeLast(3));
    }

    public async ValueTask DisposeAsync()
    {
        if (_ffmpeg is { HasExited: false } p) TryKill(p);
        await CleanupAsync();
    }
}
