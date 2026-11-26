namespace Cometsnap.Linux.Platform;

/// <summary>Helper programs Cometsnap uses: its bundled ffmpeg first, then the system's.</summary>
public static class Tools
{
    private static string? _ffmpeg;
    private static bool _searched;

    /// <summary>ffmpeg for recordings and GIFs. The AppImage and .deb ship one beside the app (libexec/ffmpeg).</summary>
    public static string? Ffmpeg
    {
        get
        {
            if (_searched) return _ffmpeg;
            _searched = true;
            var env = Environment.GetEnvironmentVariable("COMETSNAP_FFMPEG");
            var candidates = new[]
            {
                env,
                Path.Combine(AppContext.BaseDirectory, "libexec", "ffmpeg"),
                Path.Combine(AppContext.BaseDirectory, "ffmpeg"),
            };
            _ffmpeg = candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && File.Exists(c)) ?? DesktopSession.Which("ffmpeg");
            return _ffmpeg;
        }
    }

    /// <summary>Opens a file or folder with the desktop's default app.</summary>
    public static void Open(string pathOrUri)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("xdg-open") { UseShellExecute = false };
            psi.ArgumentList.Add(pathOrUri);
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            Core.Log.Error($"Could not open {pathOrUri}", ex);
        }
    }

    /// <summary>Starts a program without waiting.</summary>
    public static void Launch(string program, params string[] args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(program) { UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            Core.Log.Error($"Could not start {program}", ex);
        }
    }

    /// <summary>Shows a file selected in the file manager (FileManager1 D-Bus), or opens its folder.</summary>
    public static void ShowInFolder(string path)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("dbus-send") { UseShellExecute = false };
            foreach (var a in new[] { "--session", "--print-reply", "--dest=org.freedesktop.FileManager1", "/org/freedesktop/FileManager1",
                         "org.freedesktop.FileManager1.ShowItems", $"array:string:{new Uri(path).AbsoluteUri}", "string:" })
                psi.ArgumentList.Add(a);
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is not null && p.WaitForExit(1500) && p.ExitCode == 0) return;
        }
        catch
        {
            // fall back below
        }
        Open(Path.GetDirectoryName(path) ?? path);
    }

    /// <summary>Moves a file to the trash (gio), or deletes it when there is no trash.</summary>
    public static bool Trash(string path)
    {
        try
        {
            if (DesktopSession.Which("gio") is { } gio)
            {
                var psi = new System.Diagnostics.ProcessStartInfo(gio) { UseShellExecute = false, RedirectStandardError = true };
                psi.ArgumentList.Add("trash");
                psi.ArgumentList.Add(path);
                using var p = System.Diagnostics.Process.Start(psi);
                if (p is not null && p.WaitForExit(3000) && p.ExitCode == 0) return true;
            }
            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.Error($"Could not delete {path}", ex);
            return false;
        }
    }
}
