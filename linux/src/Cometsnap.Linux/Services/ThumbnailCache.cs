using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Platform;

namespace Cometsnap.Linux.Services;

/// <summary>
/// Small thumbnails for the flyout: decoded once at thumbnail size, kept in memory, and on disk
/// for recordings (a frame grabbed with ffmpeg), so the flyout never decodes full images.
/// </summary>
public sealed class ThumbnailCache
{
    public const int Width = 320;
    private readonly ConcurrentDictionary<string, (DateTime Stamp, Bitmap Thumb)> _memory = new();
    private readonly string _diskFolder;

    public ThumbnailCache(string diskFolder) => _diskFolder = diskFolder;

    public Bitmap? TryGet(string path) =>
        _memory.TryGetValue(path, out var e) && e.Stamp == Stamp(path) ? e.Thumb : null;

    public Task<Bitmap?> GetAsync(string path) => Task.Run(() => Load(path));

    public void Forget(string path) => _memory.TryRemove(path, out _);

    private Bitmap? Load(string path)
    {
        try
        {
            var stamp = Stamp(path);
            if (_memory.TryGetValue(path, out var e) && e.Stamp == stamp) return e.Thumb;
            Bitmap? thumb = null;
            if (MediaKinds.IsVideo(path)) thumb = VideoFrame(path, stamp);
            else
            {
                using var fs = File.OpenRead(path);
                thumb = Bitmap.DecodeToWidth(fs, Width, BitmapInterpolationMode.MediumQuality);
            }
            if (thumb is null) return null;
            _memory[path] = (stamp, thumb);
            if (_memory.Count > 120)
                foreach (var k in _memory.Keys.Take(40)) _memory.TryRemove(k, out _);
            return thumb;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private Bitmap? VideoFrame(string path, DateTime stamp)
    {
        var cached = Path.Combine(_diskFolder, $"{Math.Abs(path.GetHashCode()):x}_{stamp.Ticks:x}.png");
        if (!File.Exists(cached))
        {
            var ffmpeg = Tools.Ffmpeg;
            if (ffmpeg is null) return null;
            Directory.CreateDirectory(_diskFolder);
            var psi = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var a in new[] { "-v", "error", "-y", "-ss", "0.3", "-i", path, "-frames:v", "1", "-vf", $"scale={Width}:-2", cached })
                psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null || !p.WaitForExit(8000) || !File.Exists(cached)) return null;
        }
        using var fs = File.OpenRead(cached);
        return new Bitmap(fs);
    }

    private static DateTime Stamp(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }
}
