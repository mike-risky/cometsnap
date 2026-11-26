using System.IO;

namespace Cometsnap.Core.Storage;

/// <summary>
/// Resolves where Cometsnap keeps its settings, captures and caches.
/// If a file named <c>portable.txt</c> sits next to the executable, everything
/// is stored beside the exe (USB-friendly). Otherwise the usual user folders are used.
/// </summary>
public sealed class AppPaths
{
    public const string PortableMarker = "portable.txt";

    public bool IsPortable { get; }
    public string ExeDirectory { get; }
    public string DataDirectory { get; }
    public string DefaultCapturesDirectory { get; }
    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public string ThumbnailCacheDirectory => Path.Combine(DataDirectory, "thumbs");
    public string LogFile => Path.Combine(DataDirectory, "cometsnap.log");

    public AppPaths(string exeDirectory, string appDataRoot, string picturesRoot)
    {
        ExeDirectory = exeDirectory;
        IsPortable = File.Exists(Path.Combine(exeDirectory, PortableMarker));

        if (IsPortable)
        {
            DataDirectory = Path.Combine(exeDirectory, "CometsnapData");
            DefaultCapturesDirectory = Path.Combine(exeDirectory, "Captures");
        }
        else
        {
            DataDirectory = Path.Combine(appDataRoot, "Cometsnap");
            DefaultCapturesDirectory = Path.Combine(picturesRoot, "Cometsnap");
        }
    }

    public static AppPaths ForCurrentProcess()
    {
        var exe = Environment.ProcessPath;
        var exeDir = exe is null ? AppContext.BaseDirectory : Path.GetDirectoryName(exe)!;
        return new AppPaths(
            exeDir,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ThumbnailCacheDirectory);
    }
}
