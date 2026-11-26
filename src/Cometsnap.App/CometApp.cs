using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Cometsnap.App.Capture;
using Cometsnap.App.Editing;
using Cometsnap.App.Onboarding;
using Cometsnap.App.Services;
using Cometsnap.App.Tray;
using Cometsnap.Core.Diagnostics;
using Cometsnap.Core.Native;
using Cometsnap.Core.Settings;
using Cometsnap.Core.Storage;
using Cometsnap.Core.Takeover;
using Cometsnap.Editor;

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
    private PrintScreenHook? _printScreen;
    private EditCard? _card;

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
        TrySaveSettings(); // writes defaults on first run and keeps upgrades of older files

        _host = HiddenHost.Create();
        Capture = new CaptureController(this);
        Recent = new RecentCaptures(Math.Clamp(Settings.RecentCount, 6, 200));
        Thumbnails = new ThumbnailCache(Paths.ThumbnailCacheDirectory);
        _ = Task.Run(() => Recent.Load(CapturesFolder));

        _sounds = new SoundService();
        _comet = new CometFlight();
        _card = new EditCard();
        _card.EditRequested += (path, image) =>
        {
            if (path is not null) OpenInEditor(path);
            else OpenInEditor(image, new AnnotationDocument());
        };
        Capture.CaptureTaken += OnCaptureTaken;
        Capture.CaptureStored += (path, image) =>
        {
            Thumbnails.Prime(path, image);
            Recent.Add(path);
            _card?.SetSaved(path, image);
        };

        _tray = new TrayIcon(_host, "Cometsnap");
        _menu = new TrayMenu(this);
        _flyout = new TrayFlyout(this, Recent, Thumbnails);
        _flyout.Prewarm();
        _tray.ContextMenuRequested += (_, _) => ShowTrayMenu();
        _tray.Activated += () => _flyout.Toggle(_tray.GetIconRect());

        _hotkeys = new HotkeyManager(_host);
        _printScreen = new PrintScreenHook(Dispatcher, Capture.StartRegion, Capture.CaptureFullscreen, Capture.CaptureActiveWindow);
        var printScreenOk = RegisterHotkeys(announce: Settings.ReplaceOfferAnswered);

        Log.Info($"Started in {sw.ElapsedMilliseconds} ms");

        if (!Settings.ReplaceOfferAnswered)
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => OfferReplace(printScreenOk));
    }

    private static bool UsesPrintScreen(string gesture) =>
        gesture.Contains("PrintScreen", StringComparison.OrdinalIgnoreCase) || gesture.Contains("PrtSc", StringComparison.OrdinalIgnoreCase);

    /// <summary>Registers every hotkey. Returns whether the main capture hotkey works.</summary>
    private bool RegisterHotkeys(bool announce)
    {
        _hotkeys!.UnregisterAll();
        _printScreen?.Uninstall();

        // Owning Print Screen with the keyboard hook beats Snipping Tool, OneDrive and friends.
        var hooked = Settings.TakeOverPrintScreen && _printScreen!.Install();
        bool Register(string gesture, Action action) =>
            hooked && UsesPrintScreen(gesture) ? HookCovers(gesture) : _hotkeys.TryRegister(gesture, action);

        var main = Register(Settings.CaptureHotkey, Capture.StartRegion);
        var fallback = _hotkeys.TryRegister(Settings.CaptureHotkeyFallback, Capture.StartRegion);
        Register(Settings.FullscreenHotkey, Capture.CaptureFullscreen);
        Register(Settings.ActiveWindowHotkey, Capture.CaptureActiveWindow);

        RegionHotkeyText = main ? Settings.CaptureHotkey : fallback ? Settings.CaptureHotkeyFallback : null;
        _menu?.Rebuild();

        if (!main && announce)
        {
            var msg = fallback
                ? $"{Settings.CaptureHotkey} is taken by another app (often Windows' own Snipping Tool). Use {Settings.CaptureHotkeyFallback}, or turn off \"Use the Print screen key to open screen capture\" in Windows Settings > Accessibility > Keyboard and restart Cometsnap."
                : "No capture hotkey could be registered. Double-click the tray icon to capture.";
            _tray?.ShowBalloon("Cometsnap is ready", msg);
        }
        return main;
    }

    /// <summary>The hook handles Print Screen, Ctrl+Print Screen and Alt+Print Screen.</summary>
    private static bool HookCovers(string gesture) =>
        gesture.Replace(" ", "", StringComparison.Ordinal) is var g
        && (g.Equals("PrintScreen", StringComparison.OrdinalIgnoreCase)
            || g.Equals("Ctrl+PrintScreen", StringComparison.OrdinalIgnoreCase)
            || g.Equals("Alt+PrintScreen", StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------ replace the old tool

    /// <summary>"Replace your old screen capture with Cometsnap": first run, or from the tray menu.</summary>
    public void OfferReplace() => OfferReplace(RegionHotkeyText == Settings.CaptureHotkey);

    private void OfferReplace(bool printScreenRegistered)
    {
        var report = CaptureTools.Detect(printScreenRegistered);
        var exe = Environment.ProcessPath ?? "";
        var window = new ReplaceWindow(report, CaptureTools.IsCometsnapStartingWithWindows(exe));
        window.ShowDialog();

        Settings.ReplaceOfferAnswered = true;
        if (window.Choice is { } choice)
        {
            if (choice.DisableSnippingTool) CaptureTools.DisableSnippingPrintScreen();
            foreach (var tool in choice.ToolsToStop)
            {
                if (tool.IsRunning) CaptureTools.Close(tool.Tool);
                CaptureTools.DisableStartup(tool);
            }
            if (choice.StartWithWindows && exe.Length > 0) CaptureTools.SetCometsnapStartsWithWindows(exe, true);
            Settings.TakeOverPrintScreen = true;
            TrySaveSettings();
            var ok = RegisterHotkeys(announce: false);
            _tray?.ShowBalloon(ok ? "Print Screen is now Cometsnap" : "Cometsnap is ready",
                ok ? "Press Print Screen, click a window or drag a box. That's it." : $"Use {RegionHotkeyText ?? "the tray icon"} to capture.");
        }
        else
        {
            TrySaveSettings();
            RegisterHotkeys(announce: true);
        }
    }

    // ------------------------------------------------------------------ editor

    public void OpenInEditor(string path)
    {
        var editor = EditorWindow.OpenFile(this, path);
        editor?.Show();
        editor?.Activate();
    }

    public void OpenInEditor(System.Windows.Media.Imaging.BitmapSource image, AnnotationDocument doc)
    {
        var editor = new EditorWindow(this, image, doc, null);
        editor.Show();
        editor.Activate();
    }

    private void OnCaptureTaken(System.Windows.Media.Imaging.BitmapSource image, Cometsnap.Core.Geometry.PixelRect from)
    {
        if (Settings.PlaySounds) _sounds?.Snap();
        if (Settings.ShowEditCard) _card?.ShowFor(image, SystemParameters.WorkArea, Settings.CopyToClipboardOnCapture);
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
        _printScreen?.Dispose();
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
