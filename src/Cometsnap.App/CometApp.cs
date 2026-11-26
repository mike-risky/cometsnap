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

    public static new CometApp Current => (CometApp)Application.Current;

    public AppPaths Paths { get; private set; } = null!;
    public AppSettings Settings { get; private set; } = null!;
    public CaptureController Capture { get; private set; } = null!;

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

        _tray = new TrayIcon(_host, "Cometsnap");
        _menu = new TrayMenu(this);
        _tray.ContextMenuRequested += (_, _) => ShowTrayMenu();
        _tray.Activated += ShowTrayMenu; // replaced by the flyout panel
        _tray.DoubleClicked += Capture.StartRegion;

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
