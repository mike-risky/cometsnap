using Cometsnap.Linux.Core;
using Cometsnap.Linux.Platform;

namespace Cometsnap.Linux.Services;

/// <summary>
/// Global shortcuts, whatever the desktop:
/// 1. When Print Screen was handed over through the desktop's keyboard settings, the desktop runs
///    Cometsnap's D-Bus command: nothing else to do.
/// 2. X11: XGrabKey (falls back to Ctrl+Shift+S when Print Screen is taken).
/// 3. Wayland: the GlobalShortcuts portal (KDE Plasma, GNOME 48+, Hyprland...).
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Action<string> _onCommand;
    private X11Hotkeys? _x11;

    /// <summary>The capture shortcut that works right now, for menus and hints (e.g. "Print Screen").</summary>
    public string CaptureLabel { get; private set; } = "Print Screen";

    /// <summary>True when Print Screen is owned by another program (offer the hand-over).</summary>
    public bool PrintScreenTaken { get; private set; }

    public HotkeyService(AppSettings settings, Action<string> onCommand)
    {
        _settings = settings;
        _onCommand = onCommand;
    }

    public async Task StartAsync()
    {
        Stop();
        if (_settings.ReplacedDesktopTool)
        {
            CaptureLabel = Label(_settings.CaptureHotkey);
            Log.Info("Hotkeys: handled by the desktop's keyboard settings");
            return;
        }

        if (!DesktopSession.IsWayland)
        {
            StartX11();
            return;
        }

        try
        {
            var portal = await Portal.GetAsync();
            var shortcuts = new List<Portal.Shortcut>();
            void Add(string id, string description, string hotkey)
            {
                if (HotkeyGesture.TryParse(hotkey, out var g)) shortcuts.Add(new Portal.Shortcut(id, description, g.ToPortalTrigger()));
            }
            Add("capture", T("Capture a window or an area"), _settings.CaptureHotkey);
            Add("fullscreen", T("Capture the whole screen"), _settings.FullscreenHotkey);
            Add("record", T("Record a video (press again to stop)"), _settings.RecordHotkey);
            Add("record-gif", T("Record a GIF (press again to stop)"), _settings.RecordGifHotkey);
            Add("text", T("Copy text from the screen"), _settings.TextHotkey);
            Add("color", T("Pick a color"), _settings.ColorHotkey);
            var ok = await portal.BindGlobalShortcutsAsync(shortcuts, id => Avalonia.Threading.Dispatcher.UIThread.Post(() => _onCommand(id)));
            Log.Info($"Hotkeys: GlobalShortcuts portal {(ok ? "bound" : "not available")}");
            PrintScreenTaken = !ok;
            CaptureLabel = ok ? Label(_settings.CaptureHotkey) : "your shortcut";
        }
        catch (Exception ex)
        {
            Log.Error("GlobalShortcuts portal failed", ex);
            PrintScreenTaken = true;
            CaptureLabel = "your shortcut";
        }
    }

    private void StartX11()
    {
        _x11 = new X11Hotkeys();
        _x11.Pressed += id => Avalonia.Threading.Dispatcher.UIThread.Post(() => _onCommand(id));

        bool Grab(string id, string hotkey) => HotkeyGesture.TryParse(hotkey, out var g) && _x11.Register(id, g);

        if (Grab("capture", _settings.CaptureHotkey))
        {
            CaptureLabel = Label(_settings.CaptureHotkey);
        }
        else
        {
            PrintScreenTaken = true;
            CaptureLabel = Grab("capture", _settings.CaptureHotkeyFallback) ? Label(_settings.CaptureHotkeyFallback) : "the tray icon";
        }
        Grab("fullscreen", _settings.FullscreenHotkey);
        Grab("record", _settings.RecordHotkey);
        Grab("record-gif", _settings.RecordGifHotkey);
        Grab("scroll", _settings.ScrollingHotkey);
        Grab("text", _settings.TextHotkey);
        Grab("color", _settings.ColorHotkey);
        _x11.Start();
    }

    private static string Label(string hotkey) => HotkeyGesture.TryParse(hotkey, out var g) ? g.ToString() : hotkey;

    public void Stop()
    {
        _x11?.Dispose();
        _x11 = null;
        PrintScreenTaken = false;
    }

    public void Dispose() => Stop();
}
