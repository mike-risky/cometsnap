using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Platform;
using Cometsnap.Linux.Recording;

namespace Cometsnap.Linux.Trimming;

/// <summary>What a recording is made of, as far as the trimmer needs to know.</summary>
public sealed record ClipInfo(TimeSpan Duration, int Width, int Height, bool HasAudio);

/// <summary>
/// Writes the kept parts of a recording to a new file with ffmpeg: one trim (and atrim) per kept
/// part, joined with concat. MP4 is re-encoded, so a cut can land on any frame; GIF gets a fresh
/// palette, like the recorder makes it.
/// </summary>
public static partial class RecordingTrimmer
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Length, picture size and whether there is sound. Throws when the file can't be read.</summary>
    public static ClipInfo Probe(string path)
    {
        if (MediaKinds.IsGif(path))
        {
            var gif = GifReader.Open(path);
            return new ClipInfo(gif.Duration, gif.Width, gif.Height, false);
        }
        var ffmpeg = Tools.Ffmpeg ?? throw new InvalidOperationException(T("Trimming needs ffmpeg. Install it (for example: sudo apt install ffmpeg) and try again."));
        var psi = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in new[] { "-hide_banner", "-nostdin", "-i", path }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg did not start");
        var err = p.StandardError.ReadToEndAsync();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit(10000);
        return ParseProbe(err.Result);
    }

    /// <summary>Reads what <c>ffmpeg -i</c> prints about a file.</summary>
    public static ClipInfo ParseProbe(string text)
    {
        var d = DurationRegex().Match(text);
        if (!d.Success) throw new InvalidDataException("Not a video file.");
        var duration = new TimeSpan(0, int.Parse(d.Groups[1].Value, Inv), int.Parse(d.Groups[2].Value, Inv), 0)
                       + TimeSpan.FromSeconds(double.Parse(d.Groups[3].Value, Inv));
        int w = 0, h = 0;
        foreach (var line in text.Split('\n'))
        {
            if (!line.Contains("Video:", StringComparison.Ordinal)) continue;
            var s = SizeRegex().Match(line);
            if (s.Success) { w = int.Parse(s.Groups[1].Value, Inv); h = int.Parse(s.Groups[2].Value, Inv); }
            break;
        }
        var audio = text.Split('\n').Any(l => l.Contains("Stream #", StringComparison.Ordinal) && l.Contains("Audio:", StringComparison.Ordinal));
        return new ClipInfo(duration, w, h, audio);
    }

    /// <summary>The ffmpeg arguments that write the kept parts of <paramref name="source"/> to <paramref name="output"/>.</summary>
    public static List<string> BuildArguments(string source, string output, CutList cuts, bool gif, bool hasAudio, IEnumerable<string> videoCodec)
    {
        var kept = cuts.Kept();
        if (kept.Count == 0) throw new InvalidOperationException("Nothing is kept.");
        var graph = new StringBuilder();
        var joined = new StringBuilder();
        for (var i = 0; i < kept.Count; i++)
        {
            var from = Seconds(kept[i].SourceStart);
            var to = Seconds(kept[i].SourceEnd);
            graph.Append(Inv, $"[0:v]trim=start={from}:end={to},setpts=PTS-STARTPTS[v{i}];");
            joined.Append(Inv, $"[v{i}]");
            if (hasAudio)
            {
                graph.Append(Inv, $"[0:a]atrim=start={from}:end={to},asetpts=PTS-STARTPTS[a{i}];");
                joined.Append(Inv, $"[a{i}]");
            }
        }
        graph.Append(joined);
        if (gif)
        {
            graph.Append(Inv, $"concat=n={kept.Count}:v=1:a=0[c];");
            graph.Append("[c]split[s0][s1];[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle[v]");
        }
        else
        {
            graph.Append(Inv, $"concat=n={kept.Count}:v=1:a={(hasAudio ? 1 : 0)}[v]{(hasAudio ? "[a]" : "")}");
        }

        var args = new List<string> { "-hide_banner", "-nostdin", "-v", "error", "-y", "-i", source, "-filter_complex", graph.ToString(), "-map", "[v]" };
        if (gif)
        {
            args.AddRange(new[] { "-loop", "0", "-f", "gif" });
        }
        else
        {
            if (hasAudio) args.AddRange(new[] { "-map", "[a]" });
            args.AddRange(videoCodec);
            args.AddRange(new[] { "-pix_fmt", "yuv420p" });
            if (hasAudio) args.AddRange(new[] { "-c:a", "aac", "-b:a", "160k" });
            args.AddRange(new[] { "-movflags", "+faststart", "-f", "mp4" });
        }
        args.AddRange(new[] { "-progress", "pipe:1", "-nostats", output });
        return args;
    }

    /// <summary>Writes the kept parts of <paramref name="source"/> to <paramref name="output"/>.</summary>
    public static async Task ExportAsync(string source, string output, CutList cuts, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        var ffmpeg = Tools.Ffmpeg ?? throw new InvalidOperationException(T("Trimming needs ffmpeg. Install it (for example: sudo apt install ffmpeg) and try again."));
        var gif = MediaKinds.IsGif(source);
        var info = gif ? new ClipInfo(cuts.Duration, 0, 0, false) : await Task.Run(() => Probe(source), cancel);
        var args = BuildArguments(source, output, cuts, gif, info.HasAudio, gif ? Array.Empty<string>() : ScreenRecorder.VideoEncoderArgs(ffmpeg, forGif: false));
        var psi = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Log.Info("Trimming: ffmpeg " + string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg did not start");
        var errors = p.StandardError.ReadToEndAsync(cancel);
        var total = Math.Max(1, cuts.KeptLength.TotalMilliseconds * 1000);
        using var kill = cancel.Register(() => { try { p.Kill(true); } catch { /* gone */ } });
        while (await p.StandardOutput.ReadLineAsync(cancel) is { } line)
        {
            if (!line.StartsWith("out_time_us=", StringComparison.Ordinal) && !line.StartsWith("out_time_ms=", StringComparison.Ordinal)) continue;
            if (long.TryParse(line.AsSpan(12), NumberStyles.Integer, Inv, out var us) && us > 0)
                progress?.Report(Math.Clamp(us / total, 0, 1));
        }
        await p.WaitForExitAsync(cancel);
        var stderr = await errors;
        if (p.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length == 0)
        {
            var last = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? $"ffmpeg exit code {p.ExitCode}";
            Log.Error("Trimming failed: " + stderr);
            throw new IOException(last);
        }
        progress?.Report(1);
    }

    private static string Seconds(TimeSpan t) => t.TotalSeconds.ToString("0.######", Inv);

    [GeneratedRegex(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)")]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"[ ,](\d{2,5})x(\d{2,5})[ ,\[]")]
    private static partial Regex SizeRegex();
}
