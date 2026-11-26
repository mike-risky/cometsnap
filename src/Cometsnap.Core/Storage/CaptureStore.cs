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

    public Task<string?> SaveAsync(BitmapSource image, DateTime when)
    {
        if (!image.IsFrozen) image.Freeze();
        return Task.Run(() =>
        {
            try
            {
                var path = SnapNaming.NextFreePath(_root(), when);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                SavePng(image, path);
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

    public static void SavePng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        var tmp = path + ".part";
        using (var fs = File.Create(tmp))
            encoder.Save(fs);
        File.Move(tmp, path, overwrite: true);
    }
}
