namespace Cometsnap.Video.Audio;

internal static class PcmConvert
{
    /// <summary>Float samples (-1..1) to 16-bit PCM, clipping instead of wrapping around.</summary>
    public static void ToInt16(ReadOnlySpan<float> source, Span<short> target)
    {
        for (var i = 0; i < source.Length; i++)
        {
            var v = source[i] * 32767f;
            target[i] = v >= 32767f ? short.MaxValue : v <= -32768f ? short.MinValue : (short)v;
        }
    }
}
