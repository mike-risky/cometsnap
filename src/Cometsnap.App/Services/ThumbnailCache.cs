using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using Cometsnap.Core.Diagnostics;

namespace Cometsnap.App.Services;

/// <summary>
/// Small thumbnails for the flyout, cached in memory and as JPEGs on disk,
/// so the flyout never decodes a full-size capture.
/// </summary>
public sealed class ThumbnailCache
{
    public const int Width = 240;

    private readonly string _folder;
    private readonly ConcurrentDictionary<string, BitmapSource> _memory = new(StringComparer.OrdinalIgnoreCase);

    public ThumbnailCache(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(folder);
    }

    public BitmapSource? TryGet(string path) => _memory.TryGetValue(path, out var b) ? b : null;

    /// <summary>Puts a thumbnail made from an in-memory capture (no disk round-trip).</summary>
    public void Prime(string path, BitmapSource image)
    {
        try
        {
            var scale = Math.Min(1.0, (double)Width / image.PixelWidth);
            var thumb = new TransformedBitmap(image, new System.Windows.Media.ScaleTransform(scale, scale));
            var frozen = new WriteableBitmap(thumb);
            frozen.Freeze();
            _memory[path] = frozen;
            SaveDiskThumb(path, frozen);
        }
        catch (Exception ex)
        {
            Log.Error("Thumbnail priming failed", ex);
        }
    }

    public Task<BitmapSource?> GetAsync(string path) => Task.Run(() => Load(path));

    public void Forget(string path)
    {
        _memory.TryRemove(path, out _);
        try { File.Delete(DiskPath(path)); } catch { }
    }

    private BitmapSource? Load(string path)
    {
        if (_memory.TryGetValue(path, out var cached)) return cached;
        try
        {
            if (!File.Exists(path)) return null;
            var disk = DiskPath(path);
            BitmapSource thumb;
            if (File.Exists(disk) && File.GetLastWriteTimeUtc(disk) >= File.GetLastWriteTimeUtc(path))
            {
                thumb = Decode(disk, 0);
            }
            else
            {
                // Videos have no pixels WPF can decode: ask Windows for a frame instead.
                var made = Cometsnap.Core.Storage.MediaKinds.IsVideo(path) ? ShellThumbnail.TryGet(path, Width) : Decode(path, Width);
                if (made is null) return null;
                thumb = made;
                SaveDiskThumb(path, thumb);
            }
            _memory[path] = thumb;
            return thumb;
        }
        catch (Exception ex)
        {
            Log.Error($"Thumbnail failed for {path}", ex);
            return null;
        }
    }

    private static BitmapSource Decode(string file, int decodeWidth)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
        bmp.UriSource = new Uri(file);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private void SaveDiskThumb(string path, BitmapSource thumb)
    {
        try
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
            encoder.Frames.Add(BitmapFrame.Create(thumb));
            using var fs = File.Create(DiskPath(path));
            encoder.Save(fs);
        }
        catch (Exception ex)
        {
            Log.Error("Could not write thumbnail cache", ex);
        }
    }

    private string DiskPath(string path)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant())));
        return Path.Combine(_folder, hash[..20] + ".jpg");
    }
}
