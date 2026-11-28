using Avalonia.Platform.Storage;
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
    public TextCapture CopyText { get; private set; } = null!;
    public CodeScan ScanCode { get; private set; } = null!;
    public ScrollingCapture Scrolling { get; private set; } = null!;
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
        Theme.CaptureLook.Use(Settings.CaptureTheme);
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
        CopyText = new TextCapture(this);
        ScanCode = new CodeScan(this);
        Scrolling = new ScrollingCapture(this);
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
            case "scroll": Scrolling.Start(); break;
            case "text": CopyText.Start(); break;
            case "qr": ScanCode.Start(); break;
            case "color": Capture.StartColorPicker(); break;
            case "delayed": Capture.StartDelayed(parts.Length > 1 && int.TryParse(parts[1], out var s) ? s : 3); break;
            case "record": Recorder.ToggleRegion(RecordingFormat.Mp4); break;
            case "record-gif": Recorder.ToggleRegion(RecordingFormat.Gif); break;
            case "record-fullscreen": Recorder.ToggleFullscreen(RecordingFormat.Mp4); break;
            case "stop": Recorder.Stop(); break;
            case "flyout" or "show": Flyout.Toggle(); break;
            case "settings": OpenSettings(); break;
            case "style-studio": OpenStyleStudio(); break;
            case "edit" when parts.Length > 1: OpenInEditor(parts[1]); break;
            case "trim" when parts.Length > 1: OpenTrimmer(parts[1]); break;
            case "quit": Quit(); break;
            case "background": break;
            default: Log.Info($"Unknown command '{command}'"); break;
        }
    }

    // ------------------------------------------------------------------ captures

    /// <summary>A capture is done: copy it, save it, show the Edit card.</summary>
    public void OnCaptured(Bitmap raw, PixelRect rect)
    {
        LastRawCapture = raw;
        var copy = Settings.CopyToClipboardOnCapture;
        // Share-ready style: the copy and the saved file can each be raw or styled.
        Bitmap? styled = null;
        Bitmap Styled()
        {
            if (styled is not null) return styled;
            try { styled = SnapStyler.Apply(raw, Settings); }
            catch (Exception ex) { Log.Error("Styling the capture failed", ex); styled = raw; }
            return styled;
        }
        var copyImage = Settings.StyleOnCopy ? Styled() : raw;
        var image = Settings.StyleOnExport ? Styled() : raw;
        var saving = Settings.AutoSave || Settings.AskWhereToSave;
        if (Settings.ShowEditCard)
        {
            var shown = saving ? image : copyImage;
            ShowCard(shown, rect, copy, raw);
            if (Settings.DetectQrCodes) SpotQr(shown, raw);
        }
        LaunchComet(rect, image);
        // Counting (and any celebration) waits until the capture is fully done.
        Dispatcher.UIThread.Post(CountSnap, DispatcherPriority.ApplicationIdle);
        var when = DateTime.Now;
        var ask = Settings.AskWhereToSave;
        var save = Settings.AutoSave && !ask;
        var edit = Settings.OpenEditorAfterCapture;
        var pattern = Settings.FileNamePattern;
        var ext = SnapNaming.ExtensionFor(Settings.ImageFormat);
        var counter = save ? TakeNameCounter() : 0;
        _ = Task.Run(async () =>
        {
            try
            {
                var png = CaptureStore.EncodePng(image);
                var copyPng = ReferenceEquals(copyImage, image) ? png : CaptureStore.EncodePng(copyImage);
                if (copy) await Dispatcher.UIThread.InvokeAsync(() => Clipboard.SetPngAsync(copyPng));
                if (ask)
                {
                    Dispatcher.UIThread.Post(() => _ = AskAndSaveAsync(image, png, edit));
                    return;
                }
                var path = save ? Store.Save(png, when, pattern, ext, counter) : null;
                if (path is not null)
                {
                    Recent.Add(path);
                    Dispatcher.UIThread.Post(() => _card?.SetSaved(path, image));
                }
                if (edit) Dispatcher.UIThread.Post(() => OpenEditorFor(path, image));
            }
            catch (Exception ex)
            {
                Log.Error("Delivering the capture failed", ex);
            }
        });
    }

    /// <summary>The saved capture in the editor, or the image itself when it was not saved.</summary>
    private void OpenEditorFor(string? path, Bitmap image)
    {
        if (path is not null) OpenInEditor(path);
        else new EditorWindow(this, image, new AnnotationDocument(), null).Show();
    }

    /// <summary>"Ask where to save each time": the Save dialog, after the clipboard copy.</summary>
    private async Task AskAndSaveAsync(Bitmap image, byte[] png, bool edit)
    {
        string? path = null;
        try
        {
            var jpg = SnapNaming.ExtensionFor(Settings.ImageFormat) == ".jpg";
            var pngType = new Avalonia.Platform.Storage.FilePickerFileType(T("PNG image")) { Patterns = new[] { "*.png" } };
            var jpgType = new Avalonia.Platform.Storage.FilePickerFileType(T("JPEG image")) { Patterns = new[] { "*.jpg", "*.jpeg" } };
            Avalonia.Platform.Storage.IStorageFolder? start = null;
            try { start = await Flyout.StorageProvider.TryGetFolderFromPathAsync(Directory.Exists(_lastSaveFolder) ? _lastSaveFolder! : CapturesFolder); }
            catch { /* the dialog picks a folder itself */ }
            var file = await Flyout.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = T("Save capture"),
                SuggestedFileName = SnapNaming.SuggestedName(Settings.FileNamePattern, DateTime.Now, PeekNameCounter()) + (jpg ? ".jpg" : ".png"),
                DefaultExtension = jpg ? "jpg" : "png",
                FileTypeChoices = jpg ? new[] { jpgType, pngType } : new[] { pngType, jpgType },
                SuggestedStartLocation = start,
            });
            if (file?.TryGetLocalPath() is { } target)
            {
                CaptureStore.WriteAtomic(target, CaptureStore.ForPath(png, target));
                _lastSaveFolder = Path.GetDirectoryName(target);
                TakeNameCounter();
                path = target;
                Recent.Add(target);
                _card?.SetSaved(target, image);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Save as failed", ex);
            Toast($"{T("Could not save the capture:")} {ex.Message}", 4000);
        }
        if (edit) OpenEditorFor(path, image);
    }

    private string? _lastSaveFolder;

    /// <summary>The {counter} value the next saved capture will get.</summary>
    public int PeekNameCounter() => Math.Max(0, Settings.NameCounter) + 1;

    /// <summary>Uses up a {counter} value (only when the name pattern has one) and returns it.</summary>
    public int TakeNameCounter()
    {
        var next = PeekNameCounter();
        if (Cometsnap.Core.Storage.FileNamePattern.UsesCounter(Settings.FileNamePattern))
        {
            Settings.NameCounter = next;
            SaveSettings();
        }
        return next;
    }

    /// <summary>A free path for a new capture, under the user's name pattern and format.</summary>
    public string NextCapturePath(DateTime when) =>
        SnapNaming.NextFreePath(CapturesFolder, when, Settings.FileNamePattern, SnapNaming.ExtensionFor(Settings.ImageFormat), TakeNameCounter());

    /// <summary>Sets the captures folder ("" = default) and reloads the recent list from it.</summary>
    public void SetCapturesFolder(string folder)
    {
        Settings.CapturesFolder = string.Equals(folder, Paths.DefaultCapturesDirectory, StringComparison.Ordinal) ? "" : folder;
        SaveSettings();
        var root = CapturesFolder;
        Task.Run(() => Recent.Load(root));
    }

    /// <summary>Grabs the shortcuts again (after the settings listened for new keys).</summary>
    public void RestartHotkeys() => _ = StartHotkeysAsync();

    /// <summary>
    /// Applies changed shortcuts right away. When Print Screen was handed over through the desktop's
    /// keyboard settings, those bindings are written again with the new keys.
    /// </summary>
    public void ApplyHotkeyChange()
    {
        if (Settings.ReplacedDesktopTool)
        {
            DesktopIntegration.GiveBackPrintScreen(Settings.DesktopShortcutsBackup);
            if (DesktopIntegration.TakeOverPrintScreen(Settings, out var backup))
            {
                Settings.DesktopShortcutsBackup = backup;
            }
            else
            {
                Settings.ReplacedDesktopTool = false;
                Settings.DesktopShortcutsBackup = "";
            }
            SaveSettings();
        }
        _ = StartHotkeysAsync();
    }

    /// <summary>
    /// After a capture (copied and saved as usual): looks for a QR code in the background. When
    /// there is one, the Copied card offers its content; the screenshot stays on the clipboard.
    /// </summary>
    private void SpotQr(Bitmap shown, Bitmap raw) => _ = Task.Run(async () =>
    {
        try
        {
            await Task.Delay(60); // the card, the comet and the save go first
            var code = CodeScan.FindQr(raw);
            if (code is null) return;
            var link = Cometsnap.Core.Scanning.CodeScanner.LinkOf(code.Text);
            Dispatcher.UIThread.Post(() => _card?.OfferCode(shown, code.Text, link));
        }
        catch (Exception ex) { Log.Error("Looking for a QR code failed", ex); }
    });

    private void ShowCard(Bitmap image, PixelRect rect, bool copied, Bitmap? raw = null)
    {
        var (work, scale) = WorkAreaAt(rect.CenterX, rect.CenterY);
        // ✨ has nothing to add when both the copy and the file are styled already.
        var canStyle = raw is not null && Settings.StylePerShot && !(Settings.StyleOnCopy && Settings.StyleOnExport);
        EnsureCard().ShowFor(image, work, scale, copied, raw: raw, canStyle: canStyle);
    }

    /// <summary>
    /// The ✨ Style button of the Copied card: styles this one capture, copies it and saves it
    /// beside the original ("name-styled.png"), which stays untouched.
    /// </summary>
    private async Task StyleOneShotAsync(string? path, Bitmap raw)
    {
        try
        {
            var settings = Settings;
            var styled = await Task.Run(() => SnapStyler.Apply(raw, settings));
            var png = await Task.Run(() => CaptureStore.EncodePng(styled));
            var copied = await Clipboard.SetPngAsync(png);
            string? styledPath = null;
            if (path is not null)
            {
                if (settings.StyleOnExport) styledPath = path; // the saved file is styled already
                else
                {
                    var target = Cometsnap.Core.Styling.StyledFiles.StyledPath(path);
                    await Task.Run(() => CaptureStore.WriteAtomic(target, CaptureStore.ForPath(png, target)));
                    Recent.Add(target);
                    styledPath = target;
                }
            }
            _card?.ShowStyled(raw, styled, styledPath, copied);
            if (!Settings.ShowEditCard) Toast(T("Styled capture copied"), 2500);
        }
        catch (Exception ex)
        {
            Log.Error("Styling one capture failed", ex);
            Toast($"{T("Could not style the capture")}: {ex.Message}", 4000);
        }
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
        _card.StyleRequested += (path, raw) => _ = StyleOneShotAsync(path, raw);
        _card.CopyCodeRequested += text => _ = Clipboard.SetTextAsync(text);
        _card.OpenLinkRequested += link => { if (Cometsnap.Core.Scanning.CodeScanner.LinkOf(link) is { } safe) Tools.Open(safe); };
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
                    foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(MediaKinds.IsImage))
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

    public void Toast(string text, int ms, string? actionLabel = null, Action? action = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _toast ??= new ToastWindow();
            var p = X11.PointerPosition() ?? (0, 0);
            var (work, scale) = WorkAreaAt(p.X, p.Y);
            _toast.ShowMessage(text, ms, work, scale, actionLabel, action);
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

    /// <summary>The newest capture, unstyled: the Style studio can preview on it.</summary>
    public Bitmap? LastRawCapture { get; private set; }

    private StyleStudioWindow? _styleStudio;

    /// <summary>The Style studio (one at a time): builds the share-ready look with a live preview.</summary>
    public void OpenStyleStudio()
    {
        if (_styleStudio is { IsVisible: true })
        {
            _styleStudio.Activate();
            return;
        }
        _styleStudio = new StyleStudioWindow(this);
        _styleStudio.StyleChanged += () => _settingsWindow?.RefreshStylePreview();
        _styleStudio.Show();
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
