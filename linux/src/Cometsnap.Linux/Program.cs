using Avalonia;
using Cometsnap.Linux.Platform;

namespace Cometsnap.Linux;

public static class Program
{
    private const string Help = """
        Cometsnap: snappy screenshots and screen recordings.

        Usage: cometsnap [command]
          (none)              start in the tray (or open the panel if already running)
          --capture           pick a window or an area
          --fullscreen        capture the screen under the mouse
          --delayed N         capture after N seconds
          --record            record a video (again: stop)
          --record-gif        record a GIF (again: stop)
          --record-fullscreen record the whole screen
          --stop              stop recording
          --edit FILE         open an image in the editor
          --settings          open the settings
          --style-studio      open the Style studio
          --background        start silently in the tray (used at login)
          --quit              quit the running Cometsnap
        """;

    [STAThread]
    public static int Main(string[] args)
    {
        string? command = null;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "-h" or "--help": Console.WriteLine(Help); return 0;
                case "--version": Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString(3)); return 0;
                case "--delayed": command = "delayed:" + (i + 1 < args.Length ? args[++i] : "3"); break;
                case "--edit" when i + 1 < args.Length: command = "edit:" + Path.GetFullPath(args[++i]); break;
                case var _ when a.StartsWith("--", StringComparison.Ordinal): command = a[2..]; break;
                case var _ when File.Exists(a) && a.EndsWith(".png", StringComparison.OrdinalIgnoreCase): command = "edit:" + Path.GetFullPath(a); break;
            }
        }

        // One Cometsnap per session: a second start passes its command to the running one.
        try
        {
            var instance = InstanceService.TryStartAsync(c => CometApp.Current?.RunCommand(c), command ?? "show").GetAwaiter().GetResult();
            if (instance is null) return 0;
            CometApp.Instance = instance;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Cometsnap: no D-Bus session bus ({ex.Message}); running without single-instance.");
        }

        CometApp.StartupCommand = command;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<CometApp>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions
            {
                // Tray icons through StatusNotifierItem, no IME surprises in the overlay.
                EnableIme = false,
                WmClass = "cometsnap",
            })
            .WithInterFont()
            .LogToTrace();
}
