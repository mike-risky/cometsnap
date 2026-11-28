using System.IO;
using System.Windows.Media.Imaging;
using Cometsnap.Core.Diagnostics;

namespace Cometsnap.Core.Storage;

/// <summary>Writes captures to disk in the background, after the clipboard copy.</summary>
public sealed class CaptureStore
{
    private readonly Func<string> _root;

    /// <summary>Raised on a thread-pool thread once a capture is on disk.</summary>
    public event Action<string>? Saved;

    public CaptureStore(Func<string> root) => _root = root;

    public Task<string?> SaveAsync(BitmapSource image, DateTime when) =>
        SaveAsync(image, when, FileNamePattern.Default, SnapNaming.Extension, 1);

    /// <summary>Saves under the user's name pattern, as PNG or JPEG (from the extension).</summary>
    public Task<string?> SaveAsync(BitmapSource image, DateTime when, string? pattern, string extension, int counter)
    {
        if (!image.IsFrozen) image.Freeze();
        return Task.Run(() =>
        {
            try
            {
                var path = SnapNaming.NextFreePath(_root(), when, pattern, extension, counter);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                SaveImage(image, path);
                Saved?.Invoke(path);
                return path;
            }
            catch (Exception ex)
            {
                Log.Error("Saving capture failed", ex);
                return null;
            }
        });
    }

    public static void SavePng(BitmapSource image, string path) => Write(image, path, new PngBitmapEncoder());

    /// <summary>PNG, or JPEG (quality 92) when the path ends in .jpg / .jpeg.</summary>
    public static void SaveImage(BitmapSource image, string path)
    {
        // JPEG has no transparency: a styled capture without a backdrop goes on white, not black.
        if (MediaKinds.IsJpeg(path)) Write(Styling.StyledBitmaps.Flatten(image), path, new JpegBitmapEncoder { QualityLevel = 92 });
        else Write(image, path, new PngBitmapEncoder());
    }

    private static void Write(BitmapSource image, string path, BitmapEncoder encoder)
    {
        encoder.Frames.Add(BitmapFrame.Create(image));
        var tmp = path + ".part";
        using (var fs = File.Create(tmp))
            encoder.Save(fs);
        File.Move(tmp, path, overwrite: true);
    }
}
