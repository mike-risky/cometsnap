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
using Cometsnap.Linux.Platform;
using Cometsnap.Linux.Recording;
using Cometsnap.Linux.Services;
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
        if (!File.Exists(Paths.SettingsFile)) SaveSettings();

        Recent = new RecentCaptures(Settings.RecentCount);
        Thumbs = new ThumbnailCache(Paths.ThumbnailCacheDirectory);
        Store = new CaptureStore(() => CapturesFolder);
        Sound = new SoundService(() => Settings.PlaySounds);
        Capture = new CaptureController(this);
        Recorder = new RecordingController(this);
        Flyout = new FlyoutWindow(this);
        Clipboard = new ClipboardService(() => Flyout.Clipboard);
        Task.Run(() => Recent.Load(CapturesFolder));

        try { _tray = new TrayController(this); }
        catch (Exception ex) { Log.Error("Tray icon failed", ex); }

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
                Toast($"Cometsnap is ready ☄️ Press {Hotkeys.CaptureLabel} or click the comet in the tray.", 4500);
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
        if (_card is null)
        {
            _card = new EditCard();
            _card.EditRequested += (path, img) =>
            {
                if (path is not null) OpenInEditor(path);
                else new EditorWindow(this, img, new AnnotationDocument(), null).Show();
            };
        }
        var (work, scale) = WorkAreaAt(rect.CenterX, rect.CenterY);
        _card.ShowFor(image, work, scale, copied);
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
            Toast("Could not open this capture", 3000);
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
            Toast("Print Screen now opens Cometsnap ☄️", 4000);
            return;
        }
        if (yes) Toast("Could not change the desktop's shortcuts. See Settings.", 5000);
        SaveSettings();
    }

    public void GiveBackPrintScreen()
    {
        DesktopIntegration.GiveBackPrintScreen(Settings.DesktopShortcutsBackup);
        Settings.ReplacedDesktopTool = false;
        Settings.DesktopShortcutsBackup = "";
        SaveSettings();
        _ = StartHotkeysAsync();
        Toast("Print Screen is back with your desktop's screenshot tool.", 4000);
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
