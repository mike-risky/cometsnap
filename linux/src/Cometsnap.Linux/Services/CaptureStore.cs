using Avalonia.Media.Imaging;
using Cometsnap.Linux.Core;

namespace Cometsnap.Linux.Services;

/// <summary>Writes captures to disk in the background, after the clipboard copy.</summary>
public sealed class CaptureStore
{
    private readonly Func<string> _root;

    public CaptureStore(Func<string> root) => _root = root;

    public string Root => _root();

    public static byte[] EncodePng(Bitmap image)
    {
        using var ms = new MemoryStream();
        image.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Saves already-encoded PNG bytes as the next snap_*.png. Returns the path, or null on failure.</summary>
    public string? SavePng(byte[] png, DateTime when) =>
        Save(png, when, Cometsnap.Core.Storage.FileNamePattern.Default, SnapNaming.Extension, 1);

    /// <summary>Saves under the user's name pattern, as PNG or JPEG (from the extension). Returns the path, or null.</summary>
    public string? Save(byte[] png, DateTime when, string? pattern, string extension, int counter)
    {
        try
        {
            var path = SnapNaming.NextFreePath(_root(), when, pattern, extension, counter);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteAtomic(path, ForPath(png, path));
            return path;
        }
        catch (Exception ex)
        {
            Log.Error("Saving capture failed", ex);
            return null;
        }
    }

    /// <summary>The bytes to write at <paramref name="path"/>: the PNG as is, or re-encoded as JPEG (quality 92) for .jpg.</summary>
    public static byte[] ForPath(byte[] png, string path)
    {
        if (!MediaKinds.IsJpeg(path)) return png;
        using var bitmap = SkiaSharp.SKBitmap.Decode(png);
        if (bitmap is null) return png;
        // JPEG has no transparency: flatten on white.
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(bitmap.Width, bitmap.Height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul));
        if (surface is null) return png;
        surface.Canvas.Clear(SkiaSharp.SKColors.White);
        surface.Canvas.DrawBitmap(bitmap, 0, 0);
        using var flat = surface.Snapshot();
        using var data = flat.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 92);
        return data?.ToArray() ?? png;
    }

    public static void WriteAtomic(string path, byte[] data)
    {
        var tmp = path + ".part";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }
}
