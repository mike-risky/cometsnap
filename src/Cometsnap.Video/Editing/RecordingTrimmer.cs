using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cometsnap.Video.Encoding;
using Vortice.MediaFoundation;

namespace Cometsnap.Video.Editing;

/// <summary>
/// Writes the kept parts of a recording to a new file: MP4 is decoded and re-encoded (so cuts can
/// land on any frame, not just keyframes), GIF is decoded and re-encoded with the same small-diff
/// encoder the recorder uses.
/// </summary>
public static class RecordingTrimmer
{
    /// <summary>How long a recording lasts (MP4 or GIF).</summary>
    public static TimeSpan ProbeDuration(string path)
    {
        if (IsGif(path)) return GifReader.Open(path).Duration;
        MediaFactory.MFStartup(true).CheckError();
        using var reader = MediaFactory.MFCreateSourceReaderFromURL(path, null);
        var hns = reader.GetPresentationAttribute<ulong>(SourceReaderIndex.MediaSource,
            new MediaAttributeKey<ulong>(PresentationDescriptionAttributeKeys.Duration));
        return TimeSpan.FromTicks((long)hns);
    }

    /// <summary>The picture size of a recording (MP4 or GIF); (0, 0) when it can't be read.</summary>
    public static (int Width, int Height) ProbeSize(string path)
    {
        try
        {
            if (IsGif(path))
            {
                var r = GifReader.Open(path);
                return (r.Width, r.Height);
            }
            MediaFactory.MFStartup(true).CheckError();
            using var reader = MediaFactory.MFCreateSourceReaderFromURL(path, null);
            using var native = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, 0);
            MediaFactory.MFGetAttributeSize(native, MediaTypeAttributeKeys.FrameSize, out var w, out var h).CheckError();
            return DisplaySize(native) ?? ((int)w, (int)h);
        }
        catch (Exception ex)
        {
            VideoLog.E("Could not read the video size", ex);
            return (0, 0);
        }
    }

    /// <summary>Writes the kept parts of <paramref name="source"/> to <paramref name="output"/>.</summary>
    public static Task<RecordingResult> ExportAsync(string source, string output, CutList cuts,
        IProgress<double>? progress = null, CancellationToken cancel = default) =>
        Task.Run(() =>
        {
            var watch = Stopwatch.StartNew();
            var gif = IsGif(source);
            var hasAudio = gif ? ExportGif(source, output, cuts, progress, cancel) : ExportMp4(source, output, cuts, progress, cancel);
            VideoLog.I($"Trimmed {Path.GetFileName(source)} to {cuts.KeptLength.TotalSeconds:0.0} s in {watch.Elapsed.TotalSeconds:0.0} s");
            progress?.Report(1);
            return new RecordingResult(output, cuts.KeptLength, new FileInfo(output).Length,
                gif ? RecordingFormat.Gif : RecordingFormat.Mp4, hasAudio);
        }, cancel);

    private static bool IsGif(string path) => path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ GIF

    private static bool ExportGif(string source, string output, CutList cuts, IProgress<double>? progress, CancellationToken cancel)
    {
        var reader = GifReader.Open(source);
        var bytes = new byte[reader.Width * reader.Height * 4];
        using (var writer = new GifWriter(new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16), reader.Width, reader.Height))
        {
            for (var i = 0; i < reader.FrameCount; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var start = reader.FrameStart(i);
                if (start >= cuts.TrimEnd) break;
                var times = cuts.FrameOutputTimes(start, start + reader.FrameLength(i));
                if (times.Count == 0) continue;
                var pixels = reader.Render(i);
                var px = MemoryMarshal.Cast<byte, uint>(bytes.AsSpan());
                for (var p = 0; p < px.Length; p++) px[p] = pixels[p] | 0xFF000000u;
                foreach (var t in times) writer.AddFrame(bytes, t);
                if ((i & 15) == 0) progress?.Report(Math.Min(0.99, (start - cuts.TrimStart).TotalSeconds / Math.Max(0.001, (cuts.TrimEnd - cuts.TrimStart).TotalSeconds)));
            }
            writer.Finish(cuts.KeptLength);
        }
        return false;
    }

    // ------------------------------------------------------------------ MP4

    private static bool ExportMp4(string source, string output, CutList cuts, IProgress<double>? progress, CancellationToken cancel)
    {
        MediaFactory.MFStartup(true).CheckError();
        using var attributes = MediaFactory.MFCreateAttributes(2);
        attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, 1u);
        using var reader = MediaFactory.MFCreateSourceReaderFromURL(source, attributes);

        reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
        reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);

        // ---- video: decode to RGB32 at the size the file shows
        int width, height, fps, bitrate;
        using (var native = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, 0))
        {
            MediaFactory.MFGetAttributeSize(native, MediaTypeAttributeKeys.FrameSize, out var w, out var h).CheckError();
            (width, height) = ((int)w, (int)h);
            if (DisplaySize(native) is { } shown) (width, height) = shown;
            fps = 30;
            if (MediaFactory.MFGetAttributeRatio(native, MediaTypeAttributeKeys.FrameRate, out var num, out var den).Success && num > 0 && den > 0)
                fps = (int)Math.Clamp(Math.Round((double)num / den), 1, 120);
            bitrate = (int)MediaFactory.MFGetAttributeUInt32(native, MediaTypeAttributeKeys.AvgBitrate, 0);
        }
        width &= ~1;
        height &= ~1;
        if (bitrate <= 0) bitrate = EncoderMath.DefaultBitrate(width, height, fps);

        using (var rgb = MediaFactory.MFCreateMediaType())
        {
            rgb.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            rgb.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
            reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, rgb);
        }
        int frameW, frameH, stride;
        using (var current = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream))
        {
            MediaFactory.MFGetAttributeSize(current, MediaTypeAttributeKeys.FrameSize, out var w, out var h).CheckError();
            (frameW, frameH) = ((int)w, (int)h);
            // RGB in Media Foundation is bottom-up unless the type says otherwise.
            stride = current.GetUInt32(MediaTypeAttributeKeys.DefaultStride, out var s).Success ? unchecked((int)s) : -frameW * 4;
        }
        width = Math.Min(width, frameW & ~1);
        height = Math.Min(height, frameH & ~1);

        // ---- audio: 16-bit PCM; the recorder always writes 48 kHz stereo
        var audio = false;
        try
        {
            reader.SetStreamSelection(SourceReaderIndex.FirstAudioStream, true);
            using var pcm = MediaFactory.MFCreateMediaType();
            pcm.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
            pcm.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
            pcm.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
            pcm.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)Mp4Writer.AudioSampleRate);
            pcm.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)Mp4Writer.AudioChannels);
            reader.SetCurrentMediaType(SourceReaderIndex.FirstAudioStream, pcm);
            using var current = reader.GetCurrentMediaType(SourceReaderIndex.FirstAudioStream);
            audio = MediaFactory.MFGetAttributeUInt32(current, MediaTypeAttributeKeys.AudioSamplesPerSecond, 0) == Mp4Writer.AudioSampleRate
                && MediaFactory.MFGetAttributeUInt32(current, MediaTypeAttributeKeys.AudioNumChannels, 0) == Mp4Writer.AudioChannels
                && MediaFactory.MFGetAttributeUInt32(current, MediaTypeAttributeKeys.AudioBitsPerSample, 0) == 16;
            if (!audio) VideoLog.I("Trim: the sound is not 48 kHz stereo, leaving it out");
        }
        catch (Exception ex)
        {
            VideoLog.I("Trim: no sound track (" + ex.Message + ")");
        }
        if (!audio)
        {
            try { reader.SetStreamSelection(SourceReaderIndex.FirstAudioStream, false); }
            catch { /* there is none */ }
        }

        var frame = new byte[width * height * 4];
        var frameLength = TimeSpan.FromTicks(EncoderMath.ToHns(1.0 / fps));
        var span = Math.Max(0.001, (cuts.TrimEnd - cuts.TrimStart).TotalSeconds);
        using var writer = Mp4Writer.Create(output, width, height, fps, bitrate, audio);

        bool videoDone = false, audioDone = !audio;
        long videoAt = 0, audioAt = 0;
        var end = cuts.TrimEnd.Ticks;
        while (!videoDone || !audioDone)
        {
            cancel.ThrowIfCancellationRequested();
            var readVideo = !videoDone && (audioDone || videoAt <= audioAt);
            var index = readVideo ? SourceReaderIndex.FirstVideoStream : SourceReaderIndex.FirstAudioStream;
            using var sample = reader.ReadSample(index, SourceReaderControlFlag.None, out _, out var flags, out var timestamp);
            if ((flags & (SourceReaderFlag.EndOfStream | SourceReaderFlag.Error)) != 0 || timestamp >= end)
            {
                if (readVideo) videoDone = true; else audioDone = true;
                if ((flags & SourceReaderFlag.Error) != 0) throw new InvalidOperationException("Could not read the recording.");
                continue;
            }
            if (sample is null) continue;

            if (readVideo)
            {
                videoAt = timestamp;
                var length = sample.SampleDuration > 0 ? TimeSpan.FromTicks(sample.SampleDuration) : frameLength;
                var from = TimeSpan.FromTicks(timestamp);
                var times = cuts.FrameOutputTimes(from, from + length);
                if (times.Count > 0)
                {
                    CopyFrame(sample, frame, width, height, stride, frameH);
                    var handle = GCHandle.Alloc(frame, GCHandleType.Pinned);
                    try { foreach (var t in times) writer.WriteVideo(handle.AddrOfPinnedObject(), t.Ticks); }
                    finally { handle.Free(); }
                }
                progress?.Report(Math.Min(0.99, (from - cuts.TrimStart).TotalSeconds / span));
            }
            else
            {
                audioAt = timestamp;
                WriteAudio(sample, timestamp, cuts, writer);
            }
        }
        writer.Finish();
        return audio;
    }

    /// <summary>The visible size (minimum display aperture) when the decoder pads the picture.</summary>
    private static (int Width, int Height)? DisplaySize(IMFMediaType type)
    {
        try
        {
            var blob = type.GetBlob(MediaTypeAttributeKeys.MinimumDisplayAperture);
            if (blob is not { Length: >= 16 }) return null;
            var cx = BitConverter.ToInt32(blob, 8);
            var cy = BitConverter.ToInt32(blob, 12);
            return cx > 0 && cy > 0 ? (cx, cy) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Copies the top-left width × height of a decoded RGB32 frame, top-down, into <paramref name="frame"/>.</summary>
    private static unsafe void CopyFrame(IMFSample sample, byte[] frame, int width, int height, int stride, int frameHeight)
    {
        using var buffer = sample.ConvertToContiguousBuffer();
        var row = width * 4;
        var twoD = buffer.QueryInterfaceOrNull<IMF2DBuffer>();
        fixed (byte* dst = frame)
        {
            if (twoD is not null)
            {
                using (twoD)
                {
                    twoD.Lock2D(out var scan0, out var pitch);
                    try
                    {
                        for (var y = 0; y < height; y++)
                            Unsafe.CopyBlockUnaligned(dst + (long)y * row, (byte*)scan0 + (long)y * pitch, (uint)row);
                    }
                    finally
                    {
                        twoD.Unlock2D();
                    }
                }
                return;
            }

            buffer.Lock(out var data, out _, out var length);
            try
            {
                var abs = Math.Abs(stride);
                for (var y = 0; y < height; y++)
                {
                    // Bottom-up: the first row in memory is the bottom of the picture.
                    var srcRow = stride < 0 ? frameHeight - 1 - y : y;
                    var offset = (long)srcRow * abs;
                    if (offset + row > length) break;
                    Unsafe.CopyBlockUnaligned(dst + (long)y * row, (byte*)data + offset, (uint)row);
                }
            }
            finally
            {
                buffer.Unlock();
            }
        }
    }

    private static unsafe void WriteAudio(IMFSample sample, long timestamp, CutList cuts, Mp4Writer writer)
    {
        using var buffer = sample.ConvertToContiguousBuffer();
        buffer.Lock(out var data, out _, out var length);
        try
        {
            var channels = Mp4Writer.AudioChannels;
            var frames = length / (2 * channels);
            if (frames <= 0) return;
            var from = TimeSpan.FromTicks(timestamp);
            var to = from + TimeSpan.FromTicks(frames * 10_000_000L / Mp4Writer.AudioSampleRate);
            var all = new ReadOnlySpan<short>((void*)data, frames * channels);
            foreach (var part in cuts.Overlap(from, to))
            {
                var first = (int)Math.Round((part.SourceStart - from).TotalSeconds * Mp4Writer.AudioSampleRate);
                var last = (int)Math.Round((part.SourceEnd - from).TotalSeconds * Mp4Writer.AudioSampleRate);
                first = Math.Clamp(first, 0, frames);
                last = Math.Clamp(last, first, frames);
                if (last > first) writer.WriteAudio(all.Slice(first * channels, (last - first) * channels), part.OutputStart.Ticks);
            }
        }
        finally
        {
            buffer.Unlock();
        }
    }
}
