using System.Diagnostics;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Platform;
using Cometsnap.Linux.Trimming;

namespace Cometsnap.Linux.Tests;

public class TrimExportTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void Reads_what_ffmpeg_says_about_a_video()
    {
        const string text = """
            Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'rec.mp4':
              Duration: 00:01:02.50, start: 0.000000, bitrate: 1032 kb/s
              Stream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p(progressive), 1600x900, 900 kb/s, 30 fps, 30 tbr
              Stream #0:1[0x2](und): Audio: aac (LC) (mp4a / 0x6134706D), 48000 Hz, stereo, fltp, 128 kb/s
            """;
        var info = RecordingTrimmer.ParseProbe(text);
        Assert.Equal(S(62.5), info.Duration);
        Assert.Equal((1600, 900), (info.Width, info.Height));
        Assert.True(info.HasAudio);
    }

    [Fact]
    public void One_trim_per_kept_part_joined_with_concat()
    {
        var cuts = new CutList(S(10)) { TrimStart = S(1), TrimEnd = S(9) };
        Assert.True(cuts.AddCut(S(4), S(5)));
        var args = RecordingTrimmer.BuildArguments("in.mp4", "out.mp4", cuts, gif: false, hasAudio: true, new[] { "-c:v", "libx264" });
        var graph = args[args.IndexOf("-filter_complex") + 1];
        Assert.Contains("[0:v]trim=start=1:end=4,setpts=PTS-STARTPTS[v0]", graph);
        Assert.Contains("[0:a]atrim=start=5:end=9,asetpts=PTS-STARTPTS[a1]", graph);
        Assert.Contains("[v0][a0][v1][a1]concat=n=2:v=1:a=1[v][a]", graph);
        Assert.Contains("[a]", args);
        Assert.Equal("out.mp4", args[^1]);

        var gif = RecordingTrimmer.BuildArguments("in.gif", "out.gif", cuts, gif: true, hasAudio: false, Array.Empty<string>());
        var gifGraph = gif[gif.IndexOf("-filter_complex") + 1];
        Assert.DoesNotContain("atrim", gifGraph);
        Assert.Contains("palettegen", gifGraph);
    }

    [Fact]
    public void Preview_is_at_most_720p_and_even()
    {
        Assert.Equal((1280, 720), VideoPreview.PreviewSize(2560, 1440));
        Assert.Equal((640, 360), VideoPreview.PreviewSize(640, 360));
        var (w, h) = VideoPreview.PreviewSize(1366, 767);
        Assert.True(w % 2 == 0 && h % 2 == 0 && w <= 1280 && h <= 720);
    }

    [Fact]
    public async Task Trimming_an_mp4_keeps_only_the_kept_parts()
    {
        if (Tools.Ffmpeg is not { } ffmpeg) return; // nothing to test with
        var dir = Directory.CreateTempSubdirectory("cometsnap-trim-");
        try
        {
            var src = Path.Combine(dir.FullName, "rec.mp4");
            Run(ffmpeg, "-v", "error", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=30:duration=6", "-f", "lavfi", "-i", "sine=frequency=440:duration=6",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", src);
            var info = RecordingTrimmer.Probe(src);
            Assert.InRange(info.Duration.TotalSeconds, 5.9, 6.1);
            Assert.True(info.HasAudio);

            var cuts = new CutList(info.Duration) { TrimStart = S(1), TrimEnd = S(5) };
            Assert.True(cuts.AddCut(S(2), S(3)));
            var output = Path.Combine(dir.FullName, "out.mp4");
            var reported = new List<double>();
            await RecordingTrimmer.ExportAsync(src, output, cuts, new SyncProgress(reported.Add));
            var trimmed = RecordingTrimmer.Probe(output);
            Assert.InRange(trimmed.Duration.TotalSeconds, 2.85, 3.2);
            Assert.Equal((320, 240), (trimmed.Width, trimmed.Height));
            Assert.True(trimmed.HasAudio);
            Assert.Equal(1, reported[^1]);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Trimming_a_gif_keeps_only_the_kept_parts()
    {
        if (Tools.Ffmpeg is not { } ffmpeg) return;
        var dir = Directory.CreateTempSubdirectory("cometsnap-trim-");
        try
        {
            var src = Path.Combine(dir.FullName, "rec.gif");
            Run(ffmpeg, "-v", "error", "-f", "lavfi", "-i", "testsrc=size=160x120:rate=10:duration=4", src);
            var reader = GifReader.Open(src);
            Assert.InRange(reader.Duration.TotalSeconds, 3.9, 4.1);

            var cuts = new CutList(reader.Duration) { TrimEnd = S(3) };
            Assert.True(cuts.AddCut(S(1), S(2)));
            var output = Path.Combine(dir.FullName, "out.gif");
            await RecordingTrimmer.ExportAsync(src, output, cuts);
            var trimmed = GifReader.Open(output);
            Assert.InRange(trimmed.Duration.TotalSeconds, 1.85, 2.15);
            Assert.Equal((160, 120), (trimmed.Width, trimmed.Height));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Recording_sidecars_live_in_the_hidden_folder()
    {
        var path = Path.Combine("/captures", "2026-11", "rec_2026-11-26_08-56-12.mp4");
        Assert.Equal("/captures/2026-11/.cometsnap/rec_2026-11-26_08-56-12.original.mp4", EditSidecar.OriginalRecordingFor(path));
        Assert.Equal("/captures/2026-11/.cometsnap/rec_2026-11-26_08-56-12.cuts.json", EditSidecar.CutsFor(path));
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    private static void Run(string program, params string[] args)
    {
        var psi = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, err);
    }
}
