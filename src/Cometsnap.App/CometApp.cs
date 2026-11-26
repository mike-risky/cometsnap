using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Cometsnap.App.Capture;
using Cometsnap.App.Editing;
using Cometsnap.App.Fun;
using Cometsnap.App.Onboarding;
using Cometsnap.App.Recording;
using Cometsnap.App.Services;
using Cometsnap.App.Theme;
using Cometsnap.App.Tray;
using Cometsnap.Core.Diagnostics;
using Cometsnap.Core.Fun;
using Cometsnap.Core.Native;
using Cometsnap.Core.Settings;
using Cometsnap.Core.Storage;
using Cometsnap.Core.Takeover;
using Cometsnap.Editor;
using Cometsnap.Video;

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
    private Season _season;
    private DispatcherTimer? _seasonTimer;

    public static new CometApp Current => (CometApp)Application.Current;

    public AppPaths Paths { get; private set; } = null!;
    public AppSettings Settings { get; private set; } = null!;
    public CaptureController Capture { get; private set; } = null!;
    public RecentCaptures Recent { get; private set; } = null!;
    public ThumbnailCache Thumbnails { get; private set; } = null!;
    public RecordingController Recorder { get; private set; } = null!;

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
        Recorder = new RecordingController(this, _tray);
        _menu = new TrayMenu(this);
        _flyout = new TrayFlyout(this, Recent, Thumbnails);
        _flyout.Prewarm();
        ApplySeason();
        _seasonTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        _seasonTimer.Tick += (_, _) => ApplySeason();
        _seasonTimer.Start();
        if (!Settings.SnapCountSeeded) SeedSnapCount();
        _tray.ContextMenuRequested += (_, _) => ShowTrayMenu();
        _tray.Activated += () =>
        {
            if (Recorder.IsRecording) Recorder.Stop(); // a click on the pulsing icon stops the recording
            else _flyout.Toggle(_tray.GetIconRect());
        };

        _hotkeys = new HotkeyManager(_host);
        _printScreen = new PrintScreenHook(Dispatcher, Capture.StartRegion, Capture.CaptureFullscreen, Capture.CaptureActiveWindow)
        {
            Shift = () => Recorder.ToggleRegion(RecordingFormat.Mp4),
            CtrlShift = () => Recorder.ToggleRegion(RecordingFormat.Gif),
        };
        var printScreenOk = RegisterHotkeys(announce: Settings.ReplaceOfferAnswered);

        ListenForOtherLaunches();
        var fromInstaller = TakeInstallerChoices();
        ApplyStartWithWindows(userAsked: fromInstaller);
        Log.Info($"Started in {sw.ElapsedMilliseconds} ms");

        // Right after Setup, always show the replace step, so installing (or reinstalling) is clear.
        var afterInstall = e.Args.Any(a => a.Equals("--after-install", StringComparison.OrdinalIgnoreCase));
        if (afterInstall || !Settings.ReplaceOfferAnswered)
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => OfferReplace(printScreenOk, afterInstall));
    }

    /// <summary>
    /// Setup leaves its "Start with Windows" answer in HKCU\Software\Cometsnap; take it into the
    /// settings once and remove it. Returns true when there was one.
    /// </summary>
    private bool TakeInstallerChoices()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Cometsnap", writable: true);
            if (key?.GetValue("InstallerStartWithWindows") is not int value) return false;
            Settings.StartWithWindows = value != 0;
            key.DeleteValue("InstallerStartWithWindows", throwOnMissingValue: false);
            TrySaveSettings();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not read the installer choices", ex);
            return false;
        }
    }

    /// <summary>
    /// Makes the Windows Run entry match the setting, pointing at this exe. At launch it is a quiet
    /// sync that keeps a Task Manager "Disabled" choice; from the menu or the offer it is the user's call.
    /// </summary>
    public void ApplyStartWithWindows(bool userAsked)
    {
        var exe = Environment.ProcessPath ?? "";
        if (exe.Length == 0 || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
        CaptureTools.SetCometsnapStartsWithWindows(exe, Settings.StartWithWindows, overrideTaskManager: userAsked);
    }

    /// <summary>
    /// Launching Cometsnap again opens the tray panel of the running one; launching a different
    /// or newer exe makes this one quit so the new one takes over (see Program).
    /// </summary>
    private void ListenForOtherLaunches()
    {
        void Listen(string name, Action action)
        {
            var handle = new EventWaitHandle(false, EventResetMode.AutoReset, name);
            var thread = new Thread(() =>
            {
                while (handle.WaitOne())
                    Dispatcher.BeginInvoke(() =>
                    {
                        try { action(); }
                        catch (Exception ex) { Log.Error($"{name} failed", ex); }
                    });
            }) { IsBackground = true, Name = name };
            thread.Start();
        }
        Listen(Program.ActivateEventName, () =>
        {
            if (_flyout is { IsVisible: false }) _flyout.Toggle(_tray?.GetIconRect());
        });
        Listen(Program.QuitEventName, () =>
        {
            Log.Info("A newer Cometsnap was started: handing over");
            Shutdown();
        });
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
        Register(Settings.RecordHotkey, () => Recorder.ToggleRegion(RecordingFormat.Mp4));
        Register(Settings.RecordGifHotkey, () => Recorder.ToggleRegion(RecordingFormat.Gif));

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
            || g.Equals("Alt+PrintScreen", StringComparison.OrdinalIgnoreCase)
            || g.Equals("Shift+PrintScreen", StringComparison.OrdinalIgnoreCase)
            || g.Equals("Ctrl+Shift+PrintScreen", StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------ replace the old tool

    /// <summary>"Replace your old screen capture with Cometsnap": first run, or from the tray menu.</summary>
    public void OfferReplace() => OfferReplace(RegionHotkeyText == Settings.CaptureHotkey);

    private void OfferReplace(bool printScreenRegistered, bool justInstalled = false)
    {
        var report = CaptureTools.Detect(printScreenRegistered);
        var window = new ReplaceWindow(report, Settings.StartWithWindows, justInstalled ? $"Cometsnap {Version} is installed" : null);
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
            Settings.StartWithWindows = choice.StartWithWindows;
            ApplyStartWithWindows(userAsked: true);
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
            if (justInstalled) _tray?.ShowBalloon($"Cometsnap {Version} is installed", "It lives here in the tray. Press Print Screen to capture.");
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
        LaunchComet(new Point(from.CenterX, from.CenterY), image);
        // Counting (and any celebration) waits until the capture is fully done.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, CountSnap);
    }

    // ------------------------------------------------------------------ playful touches

    /// <summary>Counts a snap; round numbers and the first snap of a month get a tiny celebration.</summary>
    private void CountSnap()
    {
        var happened = SnapMilestones.Record(Settings, DateTime.Now);
        TrySaveSettings();
        if (!Settings.Celebrations) return;

        var delay = TimeSpan.FromMilliseconds(900); // after the comet has landed
        if (happened.FinishedMonth is { } month)
        {
            var count = month.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            Celebrate(delay, 14, $"You captured {count} snap{(month.Count == 1 ? "" : "s")}", $"in {month.MonthName} {month.Year}. New month, new snaps!");
            delay += TimeSpan.FromSeconds(3.4);
        }
        if (happened.Milestone is { } n)
            Celebrate(delay, 22, "✦ " + SnapMilestones.Title(n), "Thanks for snapping with Cometsnap.");
    }

    /// <summary>A small meteor shower above the corner where the Copied card sits.</summary>
    private void Celebrate(TimeSpan after, int meteors, string title, string subtitle)
    {
        var timer = new DispatcherTimer { Interval = after };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var work = SystemParameters.WorkArea;
            var area = new Rect(work.Right - 392, Math.Max(work.Top, work.Bottom - 250 - 112), 380, 250);
            MeteorShower.Play(area, meteors, TimeSpan.FromSeconds(3), title, subtitle);
        };
        timer.Start();
    }

    /// <summary>↑ ↑ ↓ ↓ ← → ← → B A in the tray panel.</summary>
    public void MeteorShowerEasterEgg()
    {
        Log.Info("Konami code: meteor shower");
        MeteorShower.Play(SystemParameters.WorkArea, 70, TimeSpan.FromSeconds(3.6), "☄ METEOR SHOWER ☄", "You found the secret. Make a wish!", captionAtBottom: false);
    }

    /// <summary>First run with milestones: start from the snaps already in the captures folder.</summary>
    private void SeedSnapCount()
    {
        var folder = CapturesFolder;
        _ = Task.Run(() =>
        {
            var times = new List<DateTime>();
            try
            {
                if (Directory.Exists(folder))
                    foreach (var f in Directory.EnumerateFiles(folder, "*.png", SearchOption.AllDirectories))
                        times.Add(File.GetLastWriteTime(f));
            }
            catch (Exception ex)
            {
                Log.Error("Could not count existing snaps", ex);
            }
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                SnapMilestones.Seed(Settings, times, DateTime.Now);
                TrySaveSettings();
            });
        });
    }

    /// <summary>Snowy comet in December, pumpkin comet at Halloween (tray icon and panel header).</summary>
    public void ApplySeason()
    {
        var season = Settings.SeasonalSkins ? Seasons.For(DateTime.Now) : Season.None;
        if (season == _season) return;
        _season = season;
        var size = Math.Max(16, Win32.GetSystemMetrics(49 /* SM_CXSMICON */));
        _tray?.SetSeasonIcon(SeasonalArt.CreateTrayIcon(season, size));
        _flyout?.SetSeason(season);
    }

    /// <summary>Sends the comet from a screen point (physical pixels) into the tray, carrying the capture.</summary>
    public void LaunchComet(Point start, System.Windows.Media.Imaging.BitmapSource? image = null)
    {
        if (!Settings.ShowCometAnimation || _comet is null || _tray is null) return;
        Point end;
        if (_tray.GetIconRect() is { } icon)
        {
            end = new Point((icon.Left + icon.Right) / 2.0, (icon.Top + icon.Bottom) / 2.0);
        }
        else
        {
            var cx = (int)start.X;
            var cy = (int)start.Y;
            var work = Win32.GetWorkArea(cx, cy, out _, out _) ?? default;
            end = new Point(work.Right - 40, work.Bottom - 10);
        }
        _comet.Launch(start, end, image);
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
        Recorder?.Dispose(); // finishes a running recording so the file is not lost
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
