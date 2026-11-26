using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Cometsnap.App.Capture;
using Cometsnap.App.Services;
using Cometsnap.App.Tray;
using Cometsnap.Core.Diagnostics;
using Cometsnap.Core.Native;
using Cometsnap.Core.Settings;
using Cometsnap.Core.Storage;

namespace Cometsnap.App;

/// <summary>
/// The resident tray application. Everything heavy (overlay, flyout) is created once
/// and kept hidden, so showing it later is instant.
/// </summary>
public sealed class CometApp : Application
{
    private HwndSource? _host;
    private TrayIcon? _tray;
    private TrayMenu? _menu;
    private HotkeyManager? _hotkeys;
    private TrayFlyout? _flyout;
    private SoundService? _sounds;
    private CometFlight? _comet;

    public static new CometApp Current => (CometApp)Application.Current;

    public AppPaths Paths { get; private set; } = null!;
    public AppSettings Settings { get; private set; } = null!;
    public CaptureController Capture { get; private set; } = null!;
    public RecentCaptures Recent { get; private set; } = null!;
    public ThumbnailCache Thumbnails { get; private set; } = null!;

    /// <summary>Text of the hotkey that actually works for region capture (shown in menus).</summary>
    public string? RegionHotkeyText { get; private set; }

    public string CapturesFolder =>
        string.IsNullOrWhiteSpace(Settings.CapturesFolder) ? Paths.DefaultCapturesDirectory : Settings.CapturesFolder;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var sw = Stopwatch.StartNew();
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        Paths = AppPaths.ForCurrentProcess();
        Paths.EnsureCreated();
        Log.Initialize(Paths.LogFile);
        Log.Info($"Cometsnap {Version} starting (portable: {Paths.IsPortable})");

        DispatcherUnhandledException += OnUnhandledException;

        Settings = AppSettings.Load(Paths.SettingsFile);
        if (!File.Exists(Paths.SettingsFile)) TrySaveSettings();

        _host = HiddenHost.Create();
        Capture = new CaptureController(this);
        Recent = new RecentCaptures(Math.Clamp(Settings.RecentCount, 6, 200));
        Thumbnails = new ThumbnailCache(Paths.ThumbnailCacheDirectory);
        _ = Task.Run(() => Recent.Load(CapturesFolder));

        _sounds = new SoundService();
        _comet = new CometFlight();
        Capture.CaptureTaken += OnCaptureTaken;
        Capture.CaptureStored += (path, image) =>
        {
            Thumbnails.Prime(path, image);
            Recent.Add(path);
        };

        _tray = new TrayIcon(_host, "Cometsnap");
        _menu = new TrayMenu(this);
        _flyout = new TrayFlyout(this, Recent, Thumbnails);
        _flyout.Prewarm();
        _tray.ContextMenuRequested += (_, _) => ShowTrayMenu();
        _tray.Activated += () => _flyout.Toggle(_tray.GetIconRect());

        _hotkeys = new HotkeyManager(_host);
        RegisterHotkeys();

        Log.Info($"Started in {sw.ElapsedMilliseconds} ms");
    }

    private void RegisterHotkeys()
    {
        var main = _hotkeys!.TryRegister(Settings.CaptureHotkey, Capture.StartRegion);
        var fallback = _hotkeys.TryRegister(Settings.CaptureHotkeyFallback, Capture.StartRegion);
        _hotkeys.TryRegister(Settings.FullscreenHotkey, Capture.CaptureFullscreen);
        _hotkeys.TryRegister(Settings.ActiveWindowHotkey, Capture.CaptureActiveWindow);

        RegionHotkeyText = main ? Settings.CaptureHotkey : fallback ? Settings.CaptureHotkeyFallback : null;
        _menu?.Rebuild();

        if (!main)
        {
            var msg = fallback
                ? $"{Settings.CaptureHotkey} is taken by another app (often Windows' own Snipping Tool). Use {Settings.CaptureHotkeyFallback}, or turn off \"Use the Print screen key to open screen capture\" in Windows Settings > Accessibility > Keyboard and restart Cometsnap."
                : "No capture hotkey could be registered. Double-click the tray icon to capture.";
            _tray?.ShowBalloon("Cometsnap is ready", msg);
        }
    }

    private void OnCaptureTaken(System.Windows.Media.Imaging.BitmapSource image, Cometsnap.Core.Geometry.PixelRect from)
    {
        if (Settings.PlaySounds) _sounds?.Whoosh();
        if (!Settings.ShowCometAnimation || _comet is null || _tray is null) return;

        var start = new Point(from.CenterX, from.CenterY);
        Point end;
        if (_tray.GetIconRect() is { } icon)
        {
            end = new Point((icon.Left + icon.Right) / 2.0, (icon.Top + icon.Bottom) / 2.0);
        }
        else
        {
            var work = Win32.GetWorkArea(from.CenterX, from.CenterY, out _, out _) ?? default;
            end = new Point(work.Right - 40, work.Bottom - 10);
        }
        _comet.Launch(start, end);
    }

    public static string Version =>
        typeof(CometApp).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Shows a status in the tray tooltip (null restores the default).</summary>
    public void SetTrayStatus(string? text)
    {
        if (_tray is not null) _tray.Tooltip = text ?? "Cometsnap";
    }

    private void ShowTrayMenu()
    {
        if (_host is null || _menu is null) return;
        // The menu only closes on outside clicks when our process owns the foreground.
        Win32.SetForegroundWindow(_host.Handle);
        _menu.Rebuild();
        _menu.Open();
    }

    public void OpenCapturesFolder()
    {
        Directory.CreateDirectory(CapturesFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{CapturesFolder}\"") { UseShellExecute = true });
    }

    public void OpenSettingsFile()
    {
        TrySaveSettings();
        Process.Start(new ProcessStartInfo(Paths.SettingsFile) { UseShellExecute = true });
    }

    public void TrySaveSettings()
    {
        try { Settings.Save(Paths.SettingsFile); }
        catch (Exception ex) { Log.Error("Could not save settings", ex); }
    }

    public void Quit() => Shutdown();

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _sounds?.Dispose();
        _tray?.Dispose();
        _host?.Dispose();
        Log.Info("Cometsnap exiting");
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled exception", e.Exception);
        e.Handled = true; // a screenshot tool should never vanish from the tray
    }
}
