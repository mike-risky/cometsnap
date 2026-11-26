using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Cometsnap.Linux.Capture;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Editing;
using Cometsnap.Linux.Fun;
using Cometsnap.Linux.Platform;
using Cometsnap.Linux.Recording;
using Cometsnap.Linux.Services;
using Cometsnap.Linux.Theme;
using Cometsnap.Linux.Tray;
using PixelRect = Cometsnap.Linux.Core.PixelRect;

namespace Cometsnap.Linux;

/// <summary>The tray app: owns the settings, the capture pipeline, the flyout, the editor and the recorder.</summary>
public sealed class CometApp : Application
{
    public AppPaths Paths { get; private set; } = null!;
    public AppSettings Settings { get; private set; } = null!;
    public RecentCaptures Recent { get; private set; } = null!;
    public ThumbnailCache Thumbs { get; private set; } = null!;
    public CaptureStore Store { get; private set; } = null!;
    public ClipboardService Clipboard { get; private set; } = null!;
    public SoundService Sound { get; private set; } = null!;
    public CaptureController Capture { get; private set; } = null!;
    public RecordingController Recorder { get; private set; } = null!;
    public FlyoutWindow Flyout { get; private set; } = null!;
    public HotkeyService Hotkeys { get; private set; } = null!;
    private TrayController? _tray;
    private EditCard? _card;
    private ToastWindow? _toast;
    private SettingsWindow? _settingsWindow;
    private CometFlight? _comet;
    private Season _season;
    private DispatcherTimer? _seasonTimer;
    private IClassicDesktopStyleApplicationLifetime? _lifetime;

    /// <summary>The command this process was started with (e.g. "capture"), run once the app is up.</summary>
    public static string? StartupCommand { get; set; }

    /// <summary>Set by Program when the single-instance service is up.</summary>
    public static InstanceService? Instance { get; set; }

    public static new CometApp? Current => Application.Current as CometApp;

    public string CapturesFolder => string.IsNullOrWhiteSpace(Settings.CapturesFolder) ? Paths.DefaultCapturesDirectory : Settings.CapturesFolder;

    /// <summary>Screens, through the (always created) flyout window.</summary>
    public Screens? Screens => Flyout?.Screens;

    public override void Initialize()
    {
        Name = "Cometsnap";
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _lifetime = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Start();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void Start()
    {
        Paths = AppPaths.ForCurrentProcess();
        Paths.EnsureCreated();
        Log.Initialize(Paths.LogFile);
        Log.Info($"Cometsnap {typeof(CometApp).Assembly.GetName().Version} starting · {DesktopSession.Describe()}");
        Settings = AppSettings.Load(Paths.SettingsFile);
        Loc.Use(Settings.Language);
        Log.Info($"Language: {Loc.Language}");
        if (!File.Exists(Paths.SettingsFile)) SaveSettings();

        Recent = new RecentCaptures(Settings.RecentCount);
        Thumbs = new ThumbnailCache(Paths.ThumbnailCacheDirectory);
        Store = new CaptureStore(() => CapturesFolder);
        Sound = new SoundService(() => Settings.PlaySounds);
        Capture = new CaptureController(this);
        Recorder = new RecordingController(this);
        Flyout = new FlyoutWindow(this);
        Clipboard = new ClipboardService(() => Flyout);
        Task.Run(() => Recent.Load(CapturesFolder));

        try { _tray = new TrayController(this); }
        catch (Exception ex) { Log.Error("Tray icon failed", ex); }
        ApplySeason();
        _seasonTimer = new DispatcherTimer(TimeSpan.FromHours(1), DispatcherPriority.Background, (_, _) => ApplySeason());
        _seasonTimer.Start();
        if (!Settings.SnapCountSeeded) SeedSnapCount();

        Hotkeys = new HotkeyService(Settings, RunCommand);
        _ = StartHotkeysAsync();

        DesktopIntegration.SetStartAtLogin(Settings.StartAtLogin);
        DesktopIntegration.InstallMenuEntry(Path.Combine(AppContext.BaseDirectory, "icons"));

        // Prepare the overlay windows now, so the first capture is instant.
        Dispatcher.UIThread.Post(Capture.Prewarm, DispatcherPriority.Background);

        var command = StartupCommand;
        Dispatcher.UIThread.Post(() =>
        {
            if (command is null)
            {
                Toast(F("Cometsnap is ready ☄️ Press {0} or click the comet in the tray.", Hotkeys.CaptureLabel), 4500);
                if (!Settings.ReplaceOfferAnswered) DispatcherTimer.RunOnce(() => ShowReplaceOffer(false), TimeSpan.FromSeconds(1.5));
            }
            else if (command != "background")
            {
                RunCommand(command);
            }
        }, DispatcherPriority.Background);
    }

    private async Task StartHotkeysAsync()
    {
        await Hotkeys.StartAsync();
        _tray?.SetCaptureHint(Hotkeys.CaptureLabel);
    }

    /// <summary>Commands from hotkeys, the command line and the D-Bus remote.</summary>
    public void RunCommand(string command)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => RunCommand(command));
            return;
        }
        var parts = command.Split(':', 2);
        switch (parts[0])
        {
            case "capture" or "region":
                if (Recorder.IsRecording) Recorder.Stop();
                else Capture.StartRegion();
                break;
            case "fullscreen": Capture.CaptureFullscreen(); break;
            case "delayed": Capture.StartDelayed(parts.Length > 1 && int.TryParse(parts[1], out var s) ? s : 3); break;
            case "record": Recorder.ToggleRegion(RecordingFormat.Mp4); break;
            case "record-gif": Recorder.ToggleRegion(RecordingFormat.Gif); break;
            case "record-fullscreen": Recorder.ToggleFullscreen(RecordingFormat.Mp4); break;
            case "stop": Recorder.Stop(); break;
            case "flyout" or "show": Flyout.Toggle(); break;
            case "settings": OpenSettings(); break;
            case "edit" when parts.Length > 1: OpenInEditor(parts[1]); break;
            case "trim" when parts.Length > 1: OpenTrimmer(parts[1]); break;
            case "quit": Quit(); break;
            case "background": break;
            default: Log.Info($"Unknown command '{command}'"); break;
        }
    }

    // ------------------------------------------------------------------ captures

    /// <summary>A capture is done: copy it, save it, show the Edit card.</summary>
    public void OnCaptured(Bitmap image, PixelRect rect)
    {
        var copy = Settings.CopyToClipboardOnCapture;
        if (Settings.ShowEditCard) ShowCard(image, rect, copy);
        LaunchComet(rect, image);
        // Counting (and any celebration) waits until the capture is fully done.
        Dispatcher.UIThread.Post(CountSnap, DispatcherPriority.ApplicationIdle);
        var when = DateTime.Now;
        _ = Task.Run(async () =>
        {
            try
            {
                var png = CaptureStore.EncodePng(image);
                if (copy) await Dispatcher.UIThread.InvokeAsync(() => Clipboard.SetPngAsync(png));
                if (!Settings.AutoSave) return;
                var path = Store.SavePng(png, when);
                if (path is null) return;
                Recent.Add(path);
                Dispatcher.UIThread.Post(() => _card?.SetSaved(path, image));
            }
            catch (Exception ex)
            {
                Log.Error("Delivering the capture failed", ex);
            }
        });
    }

    private void ShowCard(Bitmap image, PixelRect rect, bool copied)
    {
        var (work, scale) = WorkAreaAt(rect.CenterX, rect.CenterY);
        EnsureCard().ShowFor(image, work, scale, copied);
    }

    private EditCard EnsureCard()
    {
        if (_card is not null) return _card;
        _card = new EditCard();
        _card.EditRequested += (path, img) =>
        {
            if (path is not null) OpenInEditor(path);
            else new EditorWindow(this, img, new AnnotationDocument(), null).Show();
        };
        _card.TrimRequested += OpenTrimmer;
        return _card;
    }

    /// <summary>
    /// Shows the "Copied" card for a finished recording, with a Trim button. False when the card
    /// is turned off (the caller then shows a toast instead).
    /// </summary>
    public bool ShowRecordingCard(string path, Bitmap? poster, string detail, bool copied)
    {
        if (!Settings.ShowEditCard) return false;
        var p = X11.PointerPosition() ?? (0, 0);
        var (work, scale) = WorkAreaAt(p.X, p.Y);
        EnsureCard().ShowForRecording(path, poster, detail, copied, work, scale);
        return true;
    }

    /// <summary>Opens a recording (MP4 or GIF) in the trimmer.</summary>
    public void OpenTrimmer(string path)
    {
        var trimmer = Cometsnap.Linux.Trimming.TrimWindow.Open(this, path);
        if (trimmer is null) return;
        trimmer.Show();
        trimmer.Activate();
    }

    // ------------------------------------------------------------------ playful touches

    /// <summary>Sends the comet, carrying the capture, from the captured area towards the tray.</summary>
    public void LaunchComet(PixelRect rect, Bitmap image)
    {
        if (!Settings.ShowCometAnimation) return;
        try
        {
            var (work, scale) = WorkAreaAt(rect.CenterX, rect.CenterY);
            // The tray's exact spot is unknown on Linux: aim for the panel's corner (top bar or bottom panel).
            var screen = Screens?.ScreenFromPoint(new PixelPoint(rect.CenterX, rect.CenterY))?.Bounds ?? work;
            var panelOnTop = work.Y > screen.Y && work.Bottom >= screen.Bottom;
            var end = panelOnTop
                ? new PixelPoint(work.Right - (int)(60 * scale), work.Y - (int)(12 * scale))
                : new PixelPoint(work.Right - (int)(40 * scale), work.Bottom + (int)(10 * scale) - (work.Bottom < screen.Bottom ? 0 : (int)(20 * scale)));
            _comet ??= new CometFlight();
            _comet.Launch(new PixelPoint(rect.CenterX, rect.CenterY), end, scale, image);
        }
        catch (Exception ex)
        {
            Log.Error("Comet flight failed", ex);
        }
    }

    /// <summary>Counts a snap; round numbers and the first snap of a month get a tiny celebration.</summary>
    private void CountSnap()
    {
        var happened = SnapMilestones.Record(Settings, DateTime.Now);
        SaveSettings();
        if (!Settings.Celebrations) return;

        var delay = TimeSpan.FromMilliseconds(900); // after the comet has landed
        if (happened.FinishedMonth is { } month)
        {
            var name = new DateTime(month.Year, month.Month, 1).ToString("MMMM yyyy", Loc.Culture);
            Celebrate(delay, 14, month.Count == 1 ? T("You captured 1 snap") : F("You captured {0:N0} snaps", month.Count),
                F("in {0}. New month, new snaps!", name));
            delay += TimeSpan.FromSeconds(3.4);
        }
        if (happened.Milestone is { } n)
            Celebrate(delay, 22, "✦ " + F("{0:N0}th snap!", n), T("Thanks for snapping with Cometsnap."));
    }

    /// <summary>A small meteor shower above the corner where the Copied card sits. Never during a capture.</summary>
    private void Celebrate(TimeSpan after, int meteors, string title, string subtitle)
    {
        DispatcherTimer.RunOnce(() =>
        {
            if (Capture.IsBusy || Recorder.IsRecording)
            {
                Celebrate(TimeSpan.FromSeconds(1), meteors, title, subtitle);
                return;
            }
            var p = X11.PointerPosition() ?? (0, 0);
            var (work, scale) = WorkAreaAt(p.X, p.Y);
            var w = (int)(380 * scale);
            var h = (int)(250 * scale);
            var area = new Avalonia.PixelRect(work.Right - w - (int)(12 * scale), Math.Max(work.Y, work.Bottom - h - (int)(112 * scale)), w, h);
            MeteorShower.Play(area, scale, meteors, TimeSpan.FromSeconds(3), title, subtitle,
                fallback: () => Toast($"{title} {subtitle}", 4000));
        }, after);
    }

    /// <summary>↑ ↑ ↓ ↓ ← → ← → B A in the tray panel.</summary>
    public void MeteorShowerEasterEgg()
    {
        Log.Info("Konami code: meteor shower");
        var p = X11.PointerPosition() ?? (0, 0);
        var (work, scale) = WorkAreaAt(p.X, p.Y);
        var title = "☄ " + T("METEOR SHOWER") + " ☄";
        var subtitle = T("You found the secret. Make a wish!");
        MeteorShower.Play(work, scale, 70, TimeSpan.FromSeconds(3.6), title, subtitle,
            captionAtBottom: false, fallback: () => Toast($"{title} {subtitle}", 4000));
    }

    /// <summary>First run with milestones: start from the snaps already in the captures folder, silently.</summary>
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
                    {
                        if (f.Contains(".cometsnap", StringComparison.Ordinal)) continue; // edit sidecars
                        times.Add(File.GetLastWriteTime(f));
                    }
            }
            catch (Exception ex)
            {
                Log.Error("Could not count existing snaps", ex);
            }
            Dispatcher.UIThread.Post(() =>
            {
                SnapMilestones.Seed(Settings, times, DateTime.Now);
                SaveSettings();
                Log.Info($"Snap count starts at {Settings.SnapCount}");
            }, DispatcherPriority.Background);
        });
    }

    /// <summary>Snowy comet in December, pumpkin comet at Halloween (tray icon and panel header).</summary>
    public void ApplySeason()
    {
        var season = Settings.SeasonalSkins ? Seasons.For(DateTime.Now) : Season.None;
        if (season == _season) return;
        _season = season;
        _tray?.SetSeason(season);
        Flyout.SetSeason(season);
    }

    public void OpenInEditor(string path)
    {
        try
        {
            var original = EditSidecar.OriginalFor(path);
            var annotations = EditSidecar.AnnotationsFor(path);
            Bitmap image;
            AnnotationDocument doc;
            if (File.Exists(original) && File.Exists(annotations))
            {
                image = new Bitmap(original);
                doc = AnnotationDocument.FromJson(File.ReadAllText(annotations));
            }
            else
            {
                image = new Bitmap(path);
                doc = new AnnotationDocument();
            }
            var editor = new EditorWindow(this, image, doc, path);
            editor.Show();
            editor.Activate();
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open {path}", ex);
            Toast(T("Could not open this capture"), 3000);
        }
    }

    // ------------------------------------------------------------------ helpers

    public (Avalonia.PixelRect Work, double Scale) WorkAreaAt(int x, int y)
    {
        var screen = Screens?.ScreenFromPoint(new PixelPoint(x, y)) ?? Screens?.Primary;
        if (screen is null) return (new Avalonia.PixelRect(0, 0, 1920, 1080), 1);
        return (screen.WorkingArea, screen.Scaling);
    }

    public double ScalingAt(PixelRect r) =>
        Screens?.ScreenFromPoint(new PixelPoint(r.CenterX, r.CenterY))?.Scaling ?? 1;

    public void Toast(string text, int ms)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _toast ??= new ToastWindow();
            var p = X11.PointerPosition() ?? (0, 0);
            var (work, scale) = WorkAreaAt(p.X, p.Y);
            _toast.ShowMessage(text, ms, work, scale);
        });
    }

    public void SaveSettings()
    {
        try { Settings.Save(Paths.SettingsFile); }
        catch (Exception ex) { Log.Error("Saving settings failed", ex); }
    }

    public void OpenCapturesFolder()
    {
        Directory.CreateDirectory(CapturesFolder);
        Tools.Open(CapturesFolder);
    }

    /// <summary>Switches the app language now: the tray menu, the tray panel and the Copied card are rebuilt.</summary>
    public void SetLanguage(string code)
    {
        Settings.Language = code;
        SaveSettings();
        Loc.Use(code);
        Log.Info($"Language switched to {Loc.Language}");
        var old = Flyout;
        Flyout = new FlyoutWindow(this);
        Flyout.SetSeason(_season);
        old.Close();
        if (_card is not null)
        {
            _card.Close();
            _card = null;
        }
        if (_toast is not null)
        {
            _toast.Close();
            _toast = null;
        }
        _tray?.Rebuild();
    }

    public void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Show();
    }

    public void ShowReplaceOffer(bool force)
    {
        if (!force && (Settings.ReplaceOfferAnswered || Settings.ReplacedDesktopTool)) return;
        // Nothing to replace when Cometsnap already owns Print Screen (X11 without a desktop tool).
        if (!force && !Hotkeys.PrintScreenTaken && !DesktopSession.IsWayland)
        {
            Settings.ReplaceOfferAnswered = true;
            SaveSettings();
            return;
        }
        new ReplaceWindow(this).Show();
    }

    public void AnswerReplaceOffer(bool yes)
    {
        Settings.ReplaceOfferAnswered = true;
        if (yes && DesktopIntegration.TakeOverPrintScreen(Settings, out var backup))
        {
            Settings.ReplacedDesktopTool = true;
            Settings.DesktopShortcutsBackup = backup;
            SaveSettings();
            _ = StartHotkeysAsync();
            Toast(T("Print Screen now opens Cometsnap ☄️"), 4000);
            return;
        }
        if (yes) Toast(T("Could not change the desktop's shortcuts. See Settings."), 5000);
        SaveSettings();
    }

    public void GiveBackPrintScreen()
    {
        DesktopIntegration.GiveBackPrintScreen(Settings.DesktopShortcutsBackup);
        Settings.ReplacedDesktopTool = false;
        Settings.DesktopShortcutsBackup = "";
        SaveSettings();
        _ = StartHotkeysAsync();
        Toast(T("Print Screen is back with your desktop's screenshot tool."), 4000);
    }

    public void Quit()
    {
        Recorder.Stop();
        Hotkeys.Dispose();
        _tray?.Dispose();
        Instance?.Dispose();
        _lifetime?.Shutdown();
    }
}
