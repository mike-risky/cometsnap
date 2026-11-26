using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
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

    public static new CometApp Current => (CometApp)Application.Current;

    public AppPaths Paths { get; private set; } = null!;
    public AppSettings Settings { get; private set; } = null!;

    public string CapturesFolder =>
        string.IsNullOrWhiteSpace(Settings.CapturesFolder) ? Paths.DefaultCapturesDirectory : Settings.CapturesFolder;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        Paths = AppPaths.ForCurrentProcess();
        Paths.EnsureCreated();
        Log.Initialize(Paths.LogFile);
        Log.Info($"Cometsnap {Version} starting (portable: {Paths.IsPortable})");

        DispatcherUnhandledException += OnUnhandledException;

        Settings = AppSettings.Load(Paths.SettingsFile);
        if (!File.Exists(Paths.SettingsFile)) TrySaveSettings();

        _host = HiddenHost.Create();
        _menu = new TrayMenu(this);
        _tray = new TrayIcon(_host, "Cometsnap");
        _tray.ContextMenuRequested += (_, _) => ShowTrayMenu();
        _tray.Activated += ShowTrayMenu; // replaced by the flyout panel
    }

    public static string Version =>
        typeof(CometApp).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

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
