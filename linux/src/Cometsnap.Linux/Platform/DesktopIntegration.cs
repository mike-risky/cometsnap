using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cometsnap.Linux.Core;

namespace Cometsnap.Linux.Platform;

/// <summary>
/// Fits Cometsnap into the desktop: start at login, an app-menu entry for the AppImage, and handing
/// the Print Screen key over from the desktop's own screenshot tool (and back).
/// </summary>
public static class DesktopIntegration
{
    public const string AppId = "com.cometsnap.Cometsnap";

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string ConfigHome =>
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c : Path.Combine(Home, ".config");

    private static string DataHome =>
        Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d ? d : Path.Combine(Home, ".local", "share");

    /// <summary>What starts Cometsnap: the AppImage file, the installed binary, or this executable.</summary>
    public static string Launcher
    {
        get
        {
            if (Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage && File.Exists(appImage)) return appImage;
            if (File.Exists("/usr/bin/cometsnap") && (Environment.ProcessPath ?? "").StartsWith("/usr/", StringComparison.Ordinal)) return "/usr/bin/cometsnap";
            return Environment.ProcessPath ?? "cometsnap";
        }
    }

    public static bool IsAppImage => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPIMAGE"));

    private static string Quote(string path) => path.Contains(' ') ? $"\"{path}\"" : path;

    // ------------------------------------------------------------------ autostart

    private static string AutostartFile => Path.Combine(ConfigHome, "autostart", "cometsnap.desktop");

    public static void SetStartAtLogin(bool on)
    {
        try
        {
            if (!on)
            {
                if (File.Exists(AutostartFile)) File.Delete(AutostartFile);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(AutostartFile)!);
            File.WriteAllText(AutostartFile, $"""
                [Desktop Entry]
                Type=Application
                Name=Cometsnap
                Comment=Screenshots and screen recordings
                Exec={Quote(Launcher)} --background
                Icon=cometsnap
                Terminal=false
                X-GNOME-Autostart-enabled=true
                X-GNOME-Autostart-Delay=2
                X-KDE-autostart-after=panel

                """);
        }
        catch (Exception ex)
        {
            Log.Error("Updating autostart failed", ex);
        }
    }

    // ------------------------------------------------------------------ menu entry (AppImage)

    /// <summary>
    /// For the AppImage: adds Cometsnap to the app menu (and its icon), pointing at the AppImage you
    /// last started, so a new download replaces the old one. The .deb installs these itself.
    /// </summary>
    public static void InstallMenuEntry(string iconSourceDir)
    {
        if (!IsAppImage) return;
        try
        {
            var apps = Path.Combine(DataHome, "applications");
            Directory.CreateDirectory(apps);
            File.WriteAllText(Path.Combine(apps, "cometsnap.desktop"), $"""
                [Desktop Entry]
                Type=Application
                Name=Cometsnap
                GenericName=Screenshot tool
                Comment=Snappy screenshots, annotations and screen recordings
                Exec={Quote(Launcher)} %F
                Icon=cometsnap
                Terminal=false
                Categories=Utility;Graphics;
                Keywords=screenshot;capture;screen;record;gif;annotate;
                StartupNotify=false
                Actions=Capture;Record;

                [Desktop Action Capture]
                Name=Capture a region
                Exec={Quote(Launcher)} --capture

                [Desktop Action Record]
                Name=Record a video
                Exec={Quote(Launcher)} --record

                """);
            foreach (var size in new[] { 16, 24, 32, 48, 64, 128, 256, 512 })
            {
                var src = Path.Combine(iconSourceDir, $"cometsnap-{size}.png");
                if (!File.Exists(src)) continue;
                var dst = Path.Combine(DataHome, "icons", "hicolor", $"{size}x{size}", "apps", "cometsnap.png");
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                if (!File.Exists(dst) || new FileInfo(dst).Length != new FileInfo(src).Length) File.Copy(src, dst, true);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Installing the menu entry failed", ex);
        }
    }

    // ------------------------------------------------------------------ Print Screen hand-over

    public sealed record KeyBinding(string Id, string Name, string GtkAccelerator, string Command, string Label = "");

    /// <summary>True when this desktop's shortcuts can be set up automatically.</summary>
    public static bool CanTakeOverPrintScreen => (DesktopSession.IsGnome || DesktopSession.IsCinnamon) && DesktopSession.Which("gsettings") is not null
                                                 || DesktopSession.IsXfce && DesktopSession.Which("xfconf-query") is not null;

    /// <summary>The commands a keyboard shortcut should run (fast D-Bus call to the running app).</summary>
    public static string CommandFor(string command) =>
        DesktopSession.Which("dbus-send") is not null ? InstanceService.TriggerCommand(command) : $"{Quote(Launcher)} --{command}";

    public static IReadOnlyList<KeyBinding> Bindings(AppSettings s)
    {
        var list = new List<KeyBinding>();
        void Add(string id, string name, string hotkey, string command)
        {
            if (HotkeyGesture.TryParse(hotkey, out var g)) list.Add(new KeyBinding(id, name, g.ToGtkAccelerator(), CommandFor(command), g.ToString()));
        }
        Add("capture", "Cometsnap: capture", s.CaptureHotkey, "capture");
        Add("fullscreen", "Cometsnap: full screen", s.FullscreenHotkey, "fullscreen");
        Add("record", "Cometsnap: record video", s.RecordHotkey, "record");
        Add("record-gif", "Cometsnap: record GIF", s.RecordGifHotkey, "record-gif");
        return list;
    }

    /// <summary>
    /// Makes Print Screen (and its Ctrl / Shift variants) run Cometsnap instead of the desktop's own
    /// screenshot tool. The previous shortcuts are kept in <paramref name="backup"/> to give them back later.
    /// </summary>
    public static bool TakeOverPrintScreen(AppSettings settings, out string backup)
    {
        backup = "";
        try
        {
            var bindings = Bindings(settings);
            if (DesktopSession.IsCinnamon) return TakeOverCinnamon(bindings, out backup);
            if (DesktopSession.IsGnome) return TakeOverGnome(bindings, out backup);
            if (DesktopSession.IsXfce) return TakeOverXfce(bindings, out backup);
        }
        catch (Exception ex)
        {
            Log.Error("Taking over Print Screen failed", ex);
        }
        return false;
    }

    public static void GiveBackPrintScreen(string backup)
    {
        try
        {
            if (string.IsNullOrEmpty(backup)) return;
            var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(backup) ?? new();
            foreach (var (key, value) in saved)
            {
                var parts = key.Split('|');
                if (parts[0] == "gsettings" && parts.Length == 3) Run("gsettings", "set", parts[1], parts[2], value);
                if (parts[0] == "xfconf" && parts.Length == 2)
                {
                    if (value.Length == 0) Run("xfconf-query", "-c", "xfce4-keyboard-shortcuts", "-p", parts[1], "-r");
                    else Run("xfconf-query", "-c", "xfce4-keyboard-shortcuts", "-p", parts[1], "-n", "-t", "string", "-s", value);
                }
            }
            RemoveCustomGnome();
        }
        catch (Exception ex)
        {
            Log.Error("Giving Print Screen back failed", ex);
        }
    }

    private const string GnomeMediaKeys = "org.gnome.settings-daemon.plugins.media-keys";
    private const string GnomeCustomPath = "/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/";

    private static bool TakeOverGnome(IReadOnlyList<KeyBinding> bindings, out string backup)
    {
        var saved = new Dictionary<string, string>();
        // GNOME Shell's own screenshot keys (Print, Shift+Print, ...) go first, or ours never fire.
        foreach (var key in new[] { "show-screenshot-ui", "screenshot", "screenshot-window", "show-screen-recording-ui" })
        {
            var current = Get("gsettings", "get", "org.gnome.shell.keybindings", key);
            if (current is null) continue;
            saved[$"gsettings|org.gnome.shell.keybindings|{key}"] = current;
            var accels = ParseStringList(current);
            var keep = accels.Where(a => !bindings.Any(b => SameAccel(a, b.GtkAccelerator))).ToList();
            if (keep.Count != accels.Count) Run("gsettings", "set", "org.gnome.shell.keybindings", key, FormatStringList(keep));
        }
        // Older GNOME (< 42) used gnome-settings-daemon's media keys for screenshots.
        foreach (var key in new[] { "screenshot", "window-screenshot", "area-screenshot", "screenshot-clip", "window-screenshot-clip", "area-screenshot-clip", "screencast" })
        {
            var current = Get("gsettings", "get", GnomeMediaKeys, key);
            if (current is null) continue;
            saved[$"gsettings|{GnomeMediaKeys}|{key}"] = current;
            var accels = ParseStringList(current);
            var keep = accels.Where(a => !bindings.Any(b => SameAccel(a, b.GtkAccelerator))).ToList();
            if (keep.Count != accels.Count) Run("gsettings", "set", GnomeMediaKeys, key, FormatStringList(keep));
        }

        var list = ParseStringList(Get("gsettings", "get", GnomeMediaKeys, "custom-keybindings") ?? "@as []");
        foreach (var b in bindings)
        {
            var path = $"{GnomeCustomPath}cometsnap-{b.Id}/";
            if (!list.Contains(path)) list.Add(path);
            var schema = $"{GnomeMediaKeys}.custom-keybinding:{path}";
            Run("gsettings", "set", schema, "name", b.Name);
            Run("gsettings", "set", schema, "command", b.Command);
            Run("gsettings", "set", schema, "binding", b.GtkAccelerator);
        }
        Run("gsettings", "set", GnomeMediaKeys, "custom-keybindings", FormatStringList(list));
        backup = JsonSerializer.Serialize(saved);
        return true;
    }

    private static void RemoveCustomGnome()
    {
        if (!DesktopSession.IsGnome || DesktopSession.Which("gsettings") is null) return;
        var list = ParseStringList(Get("gsettings", "get", GnomeMediaKeys, "custom-keybindings") ?? "@as []");
        var keep = list.Where(p => !p.Contains("/cometsnap-", StringComparison.Ordinal)).ToList();
        if (keep.Count != list.Count) Run("gsettings", "set", GnomeMediaKeys, "custom-keybindings", FormatStringList(keep));
    }

    private static bool TakeOverCinnamon(IReadOnlyList<KeyBinding> bindings, out string backup)
    {
        var saved = new Dictionary<string, string>();
        const string media = "org.cinnamon.desktop.keybindings.media-keys";
        foreach (var key in new[] { "screenshot", "screenshot-clip", "area-screenshot", "area-screenshot-clip", "window-screenshot", "window-screenshot-clip" })
        {
            var current = Get("gsettings", "get", media, key);
            if (current is null) continue;
            saved[$"gsettings|{media}|{key}"] = current;
            var accels = ParseStringList(current);
            var keep = accels.Where(a => !bindings.Any(b => SameAccel(a, b.GtkAccelerator))).ToList();
            if (keep.Count != accels.Count) Run("gsettings", "set", media, key, FormatStringList(keep));
        }
        const string kb = "org.cinnamon.desktop.keybindings";
        var list = ParseStringList(Get("gsettings", "get", kb, "custom-list") ?? "@as []");
        saved[$"gsettings|{kb}|custom-list"] = FormatStringList(list);
        foreach (var b in bindings)
        {
            var name = $"cometsnap-{b.Id}";
            if (!list.Contains(name)) list.Add(name);
            var schema = $"{kb}.custom-keybinding:/org/cinnamon/desktop/keybindings/custom-keybindings/{name}/";
            Run("gsettings", "set", schema, "name", b.Name);
            Run("gsettings", "set", schema, "command", b.Command);
            Run("gsettings", "set", schema, "binding", FormatStringList(new List<string> { b.GtkAccelerator }));
        }
        Run("gsettings", "set", kb, "custom-list", FormatStringList(list));
        backup = JsonSerializer.Serialize(saved);
        return true;
    }

    private static bool TakeOverXfce(IReadOnlyList<KeyBinding> bindings, out string backup)
    {
        var saved = new Dictionary<string, string>();
        foreach (var b in bindings)
        {
            // Xfce names keys like <Primary><Shift>Print.
            var accel = b.GtkAccelerator.Replace("<Control>", "<Primary>");
            var prop = $"/commands/custom/{accel}";
            saved[$"xfconf|{prop}"] = Get("xfconf-query", "-c", "xfce4-keyboard-shortcuts", "-p", prop) ?? "";
            Run("xfconf-query", "-c", "xfce4-keyboard-shortcuts", "-p", prop, "-n", "-t", "string", "-s", b.Command);
        }
        backup = JsonSerializer.Serialize(saved);
        return true;
    }

    private static bool SameAccel(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string accel) => accel.Replace("<Primary>", "<Control>").Replace("<Ctrl>", "<Control>").Trim();

    /// <summary>Parses a GVariant string list like "['Print', '&lt;Shift&gt;Print']" or "@as []".</summary>
    internal static List<string> ParseStringList(string value)
    {
        var result = new List<string>();
        var v = value.Trim();
        if (v.StartsWith("@as", StringComparison.Ordinal)) v = v[3..].Trim();
        if (!v.StartsWith('[')) return string.IsNullOrWhiteSpace(v) ? result : new List<string> { v.Trim('\'', '"') };
        var sb = new StringBuilder();
        var inQuote = false;
        var quote = '\'';
        for (var i = 1; i < v.Length; i++)
        {
            var c = v[i];
            if (inQuote)
            {
                if (c == '\\' && i + 1 < v.Length) { sb.Append(v[++i]); continue; }
                if (c == quote) { inQuote = false; result.Add(sb.ToString()); sb.Clear(); continue; }
                sb.Append(c);
            }
            else if (c is '\'' or '"')
            {
                inQuote = true;
                quote = c;
            }
        }
        return result;
    }

    internal static string FormatStringList(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count == 0 ? "@as []" : "[" + string.Join(", ", list.Select(i => "'" + i.Replace("\\", "\\\\").Replace("'", "\\'") + "'")) + "]";
    }

    private static string? Get(string program, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(5000);
            return p.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static void Run(string program, params string[] args)
    {
        var psi = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        p?.WaitForExit(5000);
        if (p is { ExitCode: not 0 }) Log.Info($"{program} {string.Join(' ', args)} → exit {p.ExitCode}: {p.StandardError.ReadToEnd().Trim()}");
    }
}
