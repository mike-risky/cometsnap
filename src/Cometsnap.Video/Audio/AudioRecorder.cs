using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Cometsnap.Video.Audio;

/// <summary>
/// Captures what the PC plays (WASAPI loopback) and/or the default microphone, and mixes them
/// into one 48 kHz stereo float stream that the recorder pulls at wall-clock pace.
/// </summary>
internal sealed class AudioRecorder : IDisposable
{
    private readonly List<IWaveIn> _inputs = new();
    private readonly MixingSampleProvider _mixer;
    private WasapiOut? _silence;

    /// <summary>True when at least one source is actually being captured.</summary>
    public bool IsActive => _inputs.Count > 0;

    public AudioRecorder(bool systemAudio, bool microphone)
    {
        _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Mp4Writer48k, 2)) { ReadFully = true };
        if (systemAudio) TryAdd("system sound", () => new WasapiLoopbackCapture());
        if (microphone) TryAdd("microphone", () => new WasapiCapture(
            new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console)));
        if (systemAudio && _inputs.Count > 0) StartSilence();
    }

    private const int Mp4Writer48k = Encoding.Mp4Writer.AudioSampleRate;

    private void TryAdd(string name, Func<IWaveIn> create)
    {
        try
        {
            var input = create();
            var buffer = new BufferedWaveProvider(input.WaveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(5),
                DiscardOnBufferOverflow = true,
                ReadFully = true,
            };
            input.DataAvailable += (_, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            input.RecordingStopped += (_, e) =>
            {
                if (e.Exception is not null) VideoLog.E($"Audio capture ({name}) stopped", e.Exception);
            };

            ISampleProvider chain = new StereoAdapter(buffer.ToSampleProvider());
            if (chain.WaveFormat.SampleRate != Mp4Writer48k)
                chain = new WdlResamplingSampleProvider(chain, Mp4Writer48k);
            _mixer.AddMixerInput(chain);
            _inputs.Add(input);
            VideoLog.I($"Audio: {name} at {input.WaveFormat}");
        }
        catch (Exception ex)
        {
            VideoLog.E($"Audio: {name} unavailable", ex);
        }
    }

    /// <summary>
    /// Windows sends nothing to a loopback capture while nothing plays, which would make the
    /// stream jump in time. Playing silence keeps it running continuously.
    /// </summary>
    private void StartSilence()
    {
        try
        {
            _silence = new WasapiOut(AudioClientShareMode.Shared, 100);
            _silence.Init(new SilenceProvider(WaveFormat.CreateIeeeFloatWaveFormat(Mp4Writer48k, 2)));
            _silence.Play();
        }
        catch (Exception ex)
        {
            VideoLog.E("Audio: could not keep the loopback stream alive", ex);
            _silence?.Dispose();
            _silence = null;
        }
    }

    public void Start()
    {
        foreach (var input in _inputs.ToArray())
        {
            try { input.StartRecording(); }
            catch (Exception ex)
            {
                VideoLog.E("Audio: could not start a capture", ex);
                _inputs.Remove(input);
                input.Dispose();
            }
        }
    }

    /// <summary>Fills <paramref name="buffer"/> with mixed stereo samples (silence where nothing arrived).</summary>
    public void Read(float[] buffer, int count) => _mixer.Read(buffer, 0, count);

    public void Dispose()
    {
        foreach (var input in _inputs)
        {
            try { input.StopRecording(); } catch { /* already stopped */ }
            input.Dispose();
        }
        _inputs.Clear();
        _silence?.Stop();
        _silence?.Dispose();
    }

    /// <summary>Mono becomes stereo, surround keeps front left/right.</summary>
    private sealed class StereoAdapter : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private float[] _scratch = Array.Empty<float>();

        public StereoAdapter(ISampleProvider source)
        {
            _source = source;
            _channels = source.WaveFormat.Channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            if (_channels == 2) return _source.Read(buffer, offset, count);
            var frames = count / 2;
            var need = frames * _channels;
            if (_scratch.Length < need) _scratch = new float[need];
            var read = _source.Read(_scratch, 0, need) / _channels;
            for (var i = 0; i < read; i++)
            {
                var l = _scratch[i * _channels];
                var r = _channels == 1 ? l : _scratch[i * _channels + 1];
                buffer[offset + 2 * i] = l;
                buffer[offset + 2 * i + 1] = r;
            }
            return read * 2;
        }
    }
}
