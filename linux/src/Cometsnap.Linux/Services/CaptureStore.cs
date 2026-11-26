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
    public string? SavePng(byte[] png, DateTime when)
    {
        try
        {
            var path = SnapNaming.NextFreePath(_root(), when);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteAtomic(path, png);
            return path;
        }
        catch (Exception ex)
        {
            Log.Error("Saving capture failed", ex);
            return null;
        }
    }

    public static void WriteAtomic(string path, byte[] data)
    {
        var tmp = path + ".part";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }
}
