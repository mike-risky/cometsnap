using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Cometsnap.App.Capture;
using Cometsnap.App.Editing;
using Cometsnap.App.Fun;
using Cometsnap.App.Onboarding;
using Cometsnap.App.Preferences;
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
using Cometsnap.Core.Localization;

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
    private SettingsWindow? _settingsWindow;
    private readonly HashSet<string> _failedHotkeys = new(StringComparer.OrdinalIgnoreCase);

    public static new CometApp Current => (CometApp)Application.Current;

    public AppPaths Paths { get; private set; } = null!;
    public AppSettings Settings { get; private set; } = null!;
    public CaptureController Capture { get; private set; } = null!;
    public RecentCaptures Recent { get; private set; } = null!;
    public ThumbnailCache Thumbnails { get; private set; } = null!;
    public RecordingController Recorder { get; private set; } = null!;
    public ScrollingCapture Scrolling { get; private set; } = null!;

    /// <summary>Text of the hotkey that actually works for region capture (shown in menus).</summary>
    public string? RegionHotkeyText { get; private set; }

    public string CapturesFolder =>
        string.IsNullOrWhiteSpace(Settings.CapturesFolder) ? Paths.DefaultCapturesDirectory : Settings.CapturesFolder;

    /// <summary>Folder picked in the last "Ask where to save" dialog (this session only).</summary>
    public string? LastSaveFolder { get; set; }

    /// <summary>The {counter} value the next saved capture will get.</summary>
    public int PeekNameCounter() => Math.Max(0, Settings.NameCounter) + 1;

    /// <summary>Uses up a {counter} value (only when the name pattern has one) and returns it.</summary>
    public int TakeNameCounter()
    {
        var next = PeekNameCounter();
        if (FileNamePattern.UsesCounter(Settings.FileNamePattern))
        {
            Settings.NameCounter = next;
            TrySaveSettings();
        }
        return next;
    }

    /// <summary>A free path for a new capture, under the user's name pattern and format.</summary>
    public string NextCapturePath(DateTime when) =>
        SnapNaming.NextFreePath(CapturesFolder, when, Settings.FileNamePattern,
            SnapNaming.ExtensionFor(Settings.ImageFormat), TakeNameCounter());

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
        // Windows 11 slows idle background apps down; the first capture after a quiet spell
        // must open as fast as the rest, so opt out (no cost while Cometsnap is idle).
        Win32.DisableExecutionSpeedThrottling();

        Settings = AppSettings.Load(Paths.SettingsFile);
        L.Use(Settings.Language);
        Theme.CaptureLook.Use(Settings.CaptureTheme);
        Log.Info($"Language: {L.Language}");
        TrySaveSettings(); // writes defaults on first run and keeps upgrades of older files

        _host = HiddenHost.Create();
        Capture = new CaptureController(this);
        Scrolling = new ScrollingCapture(this);
        Recent = new RecentCaptures(Math.Clamp(Settings.RecentCount, 6, 200));
        Thumbnails = new ThumbnailCache(Paths.ThumbnailCacheDirectory);
        _ = Task.Run(() => Recent.Load(CapturesFolder));

        _sounds = new SoundService();
        _comet = new CometFlight();
        _card = NewEditCard();
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
            CtrlAlt = Scrolling.Start,
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
        _failedHotkeys.Clear();
        bool Register(string gesture, Action action)
        {
            var ok = hooked && UsesPrintScreen(gesture) ? HookCovers(gesture) : _hotkeys.TryRegister(gesture, action);
            if (!ok) _failedHotkeys.Add(gesture);
            return ok;
        }

        var main = Register(Settings.CaptureHotkey, Capture.StartRegion);
        var fallback = Register(Settings.CaptureHotkeyFallback, Capture.StartRegion);
        Register(Settings.FullscreenHotkey, Capture.CaptureFullscreen);
        Register(Settings.ActiveWindowHotkey, Capture.CaptureActiveWindow);
        Register(Settings.RecordHotkey, () => Recorder.ToggleRegion(RecordingFormat.Mp4));
        Register(Settings.RecordGifHotkey, () => Recorder.ToggleRegion(RecordingFormat.Gif));
        Register(Settings.ScrollingHotkey, Scrolling.Start);
        Register(Settings.TextHotkey, Capture.StartTextCapture);
        Register(Settings.ColorHotkey, Capture.StartColorPicker);
        Register(Settings.QrHotkey, Capture.StartQrScan);

        RegionHotkeyText = main ? Settings.CaptureHotkey : fallback ? Settings.CaptureHotkeyFallback : null;
        _menu?.Rebuild();

        if (!main && announce)
        {
            var msg = fallback
                ? L.F("{0} is taken by another app (often Windows' own Snipping Tool). Use {1}, or turn off \"Use the Print screen key to open screen capture\" in Windows Settings > Accessibility > Keyboard and restart Cometsnap.", Settings.CaptureHotkey, Settings.CaptureHotkeyFallback)
                : L.T("No capture hotkey could be registered. Double-click the tray icon to capture.");
            _tray?.ShowBalloon(L.T("Cometsnap is ready"), msg);
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
            || g.Equals("Ctrl+Shift+PrintScreen", StringComparison.OrdinalIgnoreCase)
            || g.Equals("Ctrl+Alt+PrintScreen", StringComparison.OrdinalIgnoreCase));

    /// <summary>Registers the hotkeys again after a change. With a gesture: returns whether that one works.</summary>
    public bool ReapplyHotkeys(string? gesture = null)
    {
        var main = RegisterHotkeys(announce: false);
        return gesture is null ? main : !_failedHotkeys.Contains(gesture);
    }

    /// <summary>Lets keys through while the Settings window records a new shortcut.</summary>
    public void PauseHotkeys()
    {
        _hotkeys?.UnregisterAll();
        _printScreen?.Uninstall();
    }

    // ------------------------------------------------------------------ settings

    /// <summary>The in-app Settings window (one at a time).</summary>
    public void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    /// <summary>Moves new captures to another folder ("" = the default) and reloads the recent list.</summary>
    public void SetCapturesFolder(string folder)
    {
        Settings.CapturesFolder = string.Equals(folder, Paths.DefaultCapturesDirectory, StringComparison.OrdinalIgnoreCase) ? "" : folder;
        TrySaveSettings();
        var root = CapturesFolder;
        _ = Task.Run(() => Recent.Load(root));
    }

    // ------------------------------------------------------------------ replace the old tool

    /// <summary>"Replace your old screen capture with Cometsnap": first run, or from the tray menu.</summary>
    public void OfferReplace() => OfferReplace(RegionHotkeyText == Settings.CaptureHotkey);

    private void OfferReplace(bool printScreenRegistered, bool justInstalled = false)
    {
        var report = CaptureTools.Detect(printScreenRegistered);
        var window = new ReplaceWindow(report, Settings, justInstalled ? L.F("Cometsnap {0} is installed", Version) : null);
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
            _tray?.ShowBalloon(ok ? L.T("Print Screen is now Cometsnap") : L.T("Cometsnap is ready"),
                ok ? L.T("Press Print Screen, click a window or drag a box. That's it.") : L.F("Use {0} to capture.", RegionHotkeyText ?? L.T("the tray icon")));
        }
        else
        {
            TrySaveSettings();
            RegisterHotkeys(announce: true);
            if (justInstalled) _tray?.ShowBalloon(L.F("Cometsnap {0} is installed", Version), L.T("It lives here in the tray. Press Print Screen to capture."));
        }
    }

    // ------------------------------------------------------------------ editor

    public void OpenInEditor(string path)
    {
        var editor = EditorWindow.OpenFile(this, path);
        editor?.Show();
        editor?.Activate();
    }

    /// <summary>Opens a recording (MP4 or GIF) in the trimmer.</summary>
    public void OpenTrimmer(string path)
    {
        var trimmer = Cometsnap.App.Trimming.TrimWindow.Open(this, path);
        trimmer?.Show();
        trimmer?.Activate();
    }

    /// <summary>
    /// Shows the "Copied" card for a finished recording. False when the card is turned off
    /// (the caller then shows a notification instead).
    /// </summary>
    public bool ShowRecordingCard(string path, System.Windows.Media.Imaging.BitmapSource? poster, string detail, bool copied)
    {
        if (!Settings.ShowEditCard || _card is null) return false;
        _card.ShowForRecording(path, poster, detail, copied, SystemParameters.WorkArea);
        return true;
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
        // A QR code in the capture? The card offers to read it (quick check, off the UI thread).
        if (Settings.ShowEditCard) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _ = OfferCodesAsync(image));
        // Counting (and any celebration) waits until the capture is fully done.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, CountSnap);
    }

    // ------------------------------------------------------------------ copy text (OCR)

    private bool _reading;

    /// <summary>
    /// Reads the text in an image with Windows' own OCR and puts it on the clipboard. The Copied
    /// card shows what was found; a click on it opens the text to pick just a part.
    /// </summary>
    public async Task<OcrOutcome?> CopyTextAsync(System.Windows.Media.Imaging.BitmapSource image, Cometsnap.Core.Geometry.PixelRect? from)
    {
        if (_reading) return null;
        _reading = true;
        try
        {
            var sw = Stopwatch.StartNew();
            if (Settings.ShowEditCard) _card?.ShowReading(image, SystemParameters.WorkArea);
            var outcome = await OcrService.RecognizeAsync(image);
            Log.Info($"OCR: {outcome.Lines} lines, {outcome.Text.Length} chars in {sw.ElapsedMilliseconds} ms ({outcome.Language})");
            if (outcome.EngineMissing)
            {
                _card?.Hide();
                Notify(L.T("Text recognition is not installed"), L.T("Add a language with \"Optical character recognition\" in Windows Settings > Time & language > Language & region, then try again."));
                return outcome;
            }
            if (outcome.Found && !ClipboardService.SetText(outcome.Text))
                Log.Error("Could not copy the text to the clipboard");
            if (Settings.ShowEditCard && _card is not null) _card.ShowText(image, outcome.Text, outcome.Lines, SystemParameters.WorkArea);
            else if (outcome.Found) Notify(L.T("Text copied"), Preview(outcome.Text));
            else Notify(L.T("No text found"), L.T("Try a bigger area, or zoom in on small text."));
            if (outcome.Found && from is { } rect) LaunchComet(new Point(rect.CenterX, rect.CenterY));
            return outcome;
        }
        catch (Exception ex)
        {
            Log.Error("Text recognition failed", ex);
            _card?.Hide();
            Notify(L.T("Could not read the text"), ex.Message);
            return null;
        }
        finally
        {
            _reading = false;
        }
    }

    private static string Preview(string text)
    {
        var first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return first.Length > 80 ? first[..80] + "…" : first;
    }

    /// <summary>Shows the recognized text in a small window, to copy only a part of it.</summary>
    public void ShowRecognizedText(string text)
    {
        var window = new TextResultWindow(text);
        window.Show();
        window.Activate();
    }

    // ------------------------------------------------------------------ QR codes

    private bool _scanning;

    /// <summary>"Scan a QR code": reads the codes in the picked area; the first one goes on the clipboard.</summary>
    public async Task ScanCodesAsync(System.Windows.Media.Imaging.BitmapSource image, Cometsnap.Core.Geometry.PixelRect? from)
    {
        if (_scanning) return;
        _scanning = true;
        try
        {
            var codes = await QrService.ScanAsync(image, quick: false);
            if (codes.Count == 0)
            {
                Notify(L.T("No QR code found"), L.T("Drag a box around the code, a little bigger than the code itself."));
                return;
            }
            ShowCodes(codes);
            if (from is { } rect) LaunchComet(new Point(rect.CenterX, rect.CenterY));
        }
        finally
        {
            _scanning = false;
        }
    }

    /// <summary>Copies the first code (a Wi-Fi code gives its password) and shows what the codes say.</summary>
    public void ShowCodes(IReadOnlyList<Cometsnap.Core.Text.ScannedCode> codes)
    {
        if (codes.Count == 0) return;
        if (!ClipboardService.SetText(codes[0].ClipboardText)) Log.Error("Could not copy the QR code to the clipboard");
        var window = new QrResultWindow(codes);
        window.Show();
        window.Activate();
    }

    private async Task OfferCodesAsync(System.Windows.Media.Imaging.BitmapSource image)
    {
        var codes = await QrService.ScanAsync(image, quick: true);
        if (codes.Count > 0) _card?.OfferCodes(image, codes);
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
            var name = new DateTime(month.Year, month.Month, 1).ToString("MMMM yyyy", L.Culture);
            Celebrate(delay, 14, month.Count == 1 ? L.T("You captured 1 snap") : L.F("You captured {0:N0} snaps", month.Count),
                L.F("in {0}. New month, new snaps!", name));
            delay += TimeSpan.FromSeconds(3.4);
        }
        if (happened.Milestone is { } n)
            Celebrate(delay, 22, "✦ " + L.F("{0:N0}th snap!", n), L.T("Thanks for snapping with Cometsnap."));
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
        MeteorShower.Play(SystemParameters.WorkArea, 70, TimeSpan.FromSeconds(3.6), "☄ " + L.T("METEOR SHOWER") + " ☄", L.T("You found the secret. Make a wish!"), captionAtBottom: false);
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
                    foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(MediaKinds.IsImage))
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

    /// <summary>Switches the app language now: menus, the tray panel and the Copied card are rebuilt.</summary>
    public void SetLanguage(string code)
    {
        Settings.Language = code;
        TrySaveSettings();
        L.Use(code);
        Log.Info($"Language switched to {L.Language}");
        if (_flyout is not null && _tray is not null)
        {
            _flyout.Close();
            _flyout = new TrayFlyout(this, Recent, Thumbnails);
            _flyout.Prewarm();
            _flyout.SetSeason(_season);
        }
        if (_card is not null)
        {
            _card.Close();
            _card = NewEditCard();
        }
        _menu?.Rebuild();
    }

    private EditCard NewEditCard()
    {
        var card = new EditCard();
        card.EditRequested += (path, image) =>
        {
            if (path is not null) OpenInEditor(path);
            else OpenInEditor(image, new AnnotationDocument());
        };
        card.TrimRequested += OpenTrimmer;
        card.TextRequested += image => _ = CopyTextAsync(image, null);
        card.ShowTextRequested += ShowRecognizedText;
        card.CodesRequested += ShowCodes;
        return card;
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

    /// <summary>A Windows notification from the tray icon.</summary>
    public void Notify(string title, string text) => _tray?.ShowBalloon(title, text);

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
