using System.Runtime.CompilerServices;
using Vortice.MediaFoundation;

namespace Cometsnap.Video.Encoding;

/// <summary>
/// H.264 + AAC in an MP4 file, through the Media Foundation sink writer. Uses the GPU encoder
/// when there is one and falls back to the Windows software encoder otherwise.
/// Thread-safe: video and audio are written from different threads.
/// </summary>
internal sealed class Mp4Writer : IDisposable
{
    public const int AudioSampleRate = 48000;
    public const int AudioChannels = 2;

    private static readonly object StartupLock = new();
    private static bool _started;

    private readonly object _lock = new();
    private readonly IMFSinkWriter _writer;
    private readonly int _videoStream;
    private readonly int _audioStream = -1;
    private readonly int _width, _height;
    private readonly long _frameDuration;
    private bool _finished;

    public bool HasAudio => _audioStream >= 0;
    public bool Hardware { get; }

    private Mp4Writer(IMFSinkWriter writer, int videoStream, int audioStream, int width, int height, int fps, bool hardware)
    {
        _writer = writer;
        _videoStream = videoStream;
        _audioStream = audioStream;
        _width = width;
        _height = height;
        _frameDuration = EncoderMath.ToHns(1.0 / fps);
        Hardware = hardware;
    }

    /// <summary>Creates the file. Tries the GPU encoder first, then software.</summary>
    public static Mp4Writer Create(string path, int width, int height, int fps, int bitrate, bool audio)
    {
        EnsureStarted();
        Exception? last = null;
        foreach (var hardware in new[] { true, false })
        {
            try
            {
                var w = TryCreate(path, width, height, fps, bitrate, audio, hardware);
                VideoLog.I($"MP4 writer ready ({width}×{height} @ {fps} fps, {bitrate / 1000} kbit/s, {(hardware ? "hardware" : "software")} encoder, audio: {audio})");
                return w;
            }
            catch (Exception ex)
            {
                last = ex;
                VideoLog.E($"Could not set up the {(hardware ? "hardware" : "software")} encoder", ex);
                try { File.Delete(path); } catch { /* best effort */ }
            }
        }
        throw new InvalidOperationException("No H.264 encoder is available on this PC.", last);
    }

    private static void EnsureStarted()
    {
        lock (StartupLock)
        {
            if (_started) return;
            MediaFactory.MFStartup(true).CheckError();
            _started = true;
        }
    }

    private static Mp4Writer TryCreate(string path, int width, int height, int fps, int bitrate, bool audio, bool hardware)
    {
        using var attributes = MediaFactory.MFCreateAttributes(4);
        attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, hardware ? 1u : 0u);
        attributes.Set(SinkWriterAttributeKeys.ReadwriteDisableConverters, 0u);
        attributes.Set(TranscodeAttributeKeys.TranscodeContainertype, TranscodeContainerTypeGuids.Mpeg4);

        var writer = MediaFactory.MFCreateSinkWriterFromURL(path, null, attributes);
        try
        {
            // ---- video: H.264 out, RGB32 in
            using var videoOut = MediaFactory.MFCreateMediaType();
            videoOut.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            videoOut.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
            videoOut.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)bitrate);
            videoOut.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
            videoOut.Set(MediaTypeAttributeKeys.Mpeg2Profile, 77u); // eAVEncH264VProfile_Main
            MediaFactory.MFSetAttributeSize(videoOut, MediaTypeAttributeKeys.FrameSize, (uint)width, (uint)height).CheckError();
            MediaFactory.MFSetAttributeRatio(videoOut, MediaTypeAttributeKeys.FrameRate, (uint)fps, 1).CheckError();
            MediaFactory.MFSetAttributeRatio(videoOut, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1).CheckError();
            var videoStream = writer.AddStream(videoOut);

            using var videoIn = MediaFactory.MFCreateMediaType();
            videoIn.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            videoIn.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
            videoIn.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
            MediaFactory.MFSetAttributeSize(videoIn, MediaTypeAttributeKeys.FrameSize, (uint)width, (uint)height).CheckError();
            MediaFactory.MFSetAttributeRatio(videoIn, MediaTypeAttributeKeys.FrameRate, (uint)fps, 1).CheckError();
            MediaFactory.MFSetAttributeRatio(videoIn, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1).CheckError();
            writer.SetInputMediaType(videoStream, videoIn, null);

            // ---- audio: AAC out, 16-bit PCM in
            var audioStream = -1;
            if (audio)
            {
                using var audioOut = MediaFactory.MFCreateMediaType();
                audioOut.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                audioOut.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
                audioOut.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
                audioOut.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)AudioSampleRate);
                audioOut.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)AudioChannels);
                audioOut.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 24000u); // 192 kbit/s
                audioStream = writer.AddStream(audioOut);

                using var audioIn = MediaFactory.MFCreateMediaType();
                audioIn.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                audioIn.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
                audioIn.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
                audioIn.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)AudioSampleRate);
                audioIn.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)AudioChannels);
                audioIn.Set(MediaTypeAttributeKeys.AudioBlockAlignment, (uint)(AudioChannels * 2));
                audioIn.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)(AudioSampleRate * AudioChannels * 2));
                writer.SetInputMediaType(audioStream, audioIn, null);
            }

            writer.BeginWriting();
            return new Mp4Writer(writer, videoStream, audioStream, width, height, fps, hardware);
        }
        catch
        {
            writer.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Writes one top-down BGRA frame (stride = width × 4). Media Foundation expects RGB32 bottom-up
    /// by default, so rows are flipped while copying.
    /// </summary>
    public unsafe void WriteVideo(IntPtr topDownBgra, long timestamp)
    {
        var stride = _width * 4;
        var size = stride * _height;
        using var buffer = MediaFactory.MFCreateMemoryBuffer(size);
        buffer.Lock(out var dst, out _, out _);
        try
        {
            var s = (byte*)topDownBgra;
            var d = (byte*)dst;
            for (var y = 0; y < _height; y++)
                Unsafe.CopyBlockUnaligned(d + (long)(_height - 1 - y) * stride, s + (long)y * stride, (uint)stride);
        }
        finally
        {
            buffer.Unlock();
        }
        buffer.CurrentLength = size;
        Write(_videoStream, buffer, timestamp, _frameDuration);
    }

    /// <summary>Writes interleaved 16-bit stereo PCM at 48 kHz.</summary>
    public unsafe void WriteAudio(ReadOnlySpan<short> samples, long timestamp)
    {
        if (_audioStream < 0 || samples.IsEmpty) return;
        var bytes = samples.Length * 2;
        using var buffer = MediaFactory.MFCreateMemoryBuffer(bytes);
        buffer.Lock(out var dst, out _, out _);
        try
        {
            samples.CopyTo(new Span<short>((void*)dst, samples.Length));
        }
        finally
        {
            buffer.Unlock();
        }
        buffer.CurrentLength = bytes;
        var frames = samples.Length / AudioChannels;
        Write(_audioStream, buffer, timestamp, frames * 10_000_000L / AudioSampleRate);
    }

    private void Write(int stream, IMFMediaBuffer buffer, long timestamp, long duration)
    {
        using var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = timestamp;
        sample.SampleDuration = duration;
        lock (_lock)
        {
            if (_finished) return;
            _writer.WriteSample(stream, sample);
        }
    }

    /// <summary>Flushes the encoders and writes the MP4 index. Can take a moment for long recordings.</summary>
    public void Finish()
    {
        lock (_lock)
        {
            if (_finished) return;
            _finished = true;
            _writer.Finalize();
        }
    }

    public void Dispose()
    {
        try { Finish(); }
        catch (Exception ex) { VideoLog.E("Finalizing the MP4 failed", ex); }
        _writer.Dispose();
    }
}
