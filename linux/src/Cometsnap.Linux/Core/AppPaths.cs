namespace Cometsnap.Linux.Core;

/// <summary>
/// Where Cometsnap keeps its settings, captures and caches, following the XDG base directories.
/// If a file named <c>portable.txt</c> sits next to the AppImage (or the binary), everything is
/// stored beside it instead (USB-friendly).
/// </summary>
public sealed class AppPaths
{
    public const string PortableMarker = "portable.txt";

    public bool IsPortable { get; }
    public string AppDirectory { get; }
    public string ConfigDirectory { get; }
    public string CacheDirectory { get; }
    public string DefaultCapturesDirectory { get; }
    public string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");
    public string ThumbnailCacheDirectory => Path.Combine(CacheDirectory, "thumbs");
    public string LogFile => Path.Combine(CacheDirectory, "cometsnap.log");

    public AppPaths(string appDirectory, Func<string, string?> env, string home)
    {
        AppDirectory = appDirectory;
        IsPortable = File.Exists(Path.Combine(appDirectory, PortableMarker));
        if (IsPortable)
        {
            ConfigDirectory = Path.Combine(appDirectory, "CometsnapData");
            CacheDirectory = Path.Combine(ConfigDirectory, "cache");
            DefaultCapturesDirectory = Path.Combine(appDirectory, "Captures");
            return;
        }

        string Xdg(string name, string fallback)
        {
            var v = env(name);
            return string.IsNullOrWhiteSpace(v) || !Path.IsPathRooted(v) ? Path.Combine(home, fallback) : v;
        }

        ConfigDirectory = Path.Combine(Xdg("XDG_CONFIG_HOME", ".config"), "cometsnap");
        CacheDirectory = Path.Combine(Xdg("XDG_CACHE_HOME", ".cache"), "cometsnap");
        DefaultCapturesDirectory = Path.Combine(PicturesDirectory(env, home), "Cometsnap");
    }

    /// <summary>The user's Pictures folder (XDG_PICTURES_DIR from user-dirs.dirs, localized names included).</summary>
    public static string PicturesDirectory(Func<string, string?> env, string home)
    {
        var direct = env("XDG_PICTURES_DIR");
        if (!string.IsNullOrWhiteSpace(direct)) return direct.Replace("$HOME", home);
        try
        {
            var configHome = env("XDG_CONFIG_HOME");
            if (string.IsNullOrWhiteSpace(configHome)) configHome = Path.Combine(home, ".config");
            var file = Path.Combine(configHome, "user-dirs.dirs");
            if (File.Exists(file))
            {
                foreach (var raw in File.ReadLines(file))
                {
                    var line = raw.Trim();
                    if (!line.StartsWith("XDG_PICTURES_DIR=", StringComparison.Ordinal)) continue;
                    var value = line["XDG_PICTURES_DIR=".Length..].Trim().Trim('"').Replace("$HOME", home);
                    if (Path.IsPathRooted(value) && value.TrimEnd('/') != home.TrimEnd('/')) return value;
                }
            }
        }
        catch
        {
            // fall through to ~/Pictures
        }
        return Path.Combine(home, "Pictures");
    }

    /// <summary>The folder of the AppImage file itself (not its mount point), or of the binary.</summary>
    public static string CurrentAppDirectory()
    {
        var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrEmpty(appImage) && Path.GetDirectoryName(appImage) is { } dir) return dir;
        var exe = Environment.ProcessPath;
        return exe is null ? AppContext.BaseDirectory : Path.GetDirectoryName(exe)!;
    }

    public static AppPaths ForCurrentProcess() =>
        new(CurrentAppDirectory(), Environment.GetEnvironmentVariable,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public void EnsureCreated()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(ThumbnailCacheDirectory);
    }
}
