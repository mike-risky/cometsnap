using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Recording;
using Cometsnap.Linux.Theme;

namespace Cometsnap.Linux.Tray;

/// <summary>
/// The comet in the tray (StatusNotifierItem: KDE, Ubuntu, Cinnamon, Xfce, and GNOME with the
/// AppIndicator extension). Click: the flyout. Menu: everything else. Red dot while recording.
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly CometApp _app;
    private readonly TrayIcon _icon;
    private readonly WindowIcon _everyday;
    private WindowIcon _normal;
    private bool _isRecording;
    private readonly WindowIcon _recording;
    private NativeMenuItem _recent = null!;
    private NativeMenuItem _stop = null!;
    private NativeMenuItem _captureItem = null!;
    private string? _captureHint;

    public TrayController(CometApp app)
    {
        _app = app;
        _everyday = _normal = new WindowIcon(SpaceTheme.LoadAsset("cometsnap-256.png"));
        _recording = new WindowIcon(WithRedDot(SpaceTheme.LoadAsset("cometsnap-256.png")));

        _icon = new TrayIcon
        {
            Icon = _normal,
            ToolTipText = "Cometsnap",
            Menu = BuildMenu(),
            IsVisible = true,
        };
        _icon.Clicked += (_, _) => _app.Flyout.Toggle();
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _icon });

        _app.Recent.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshRecent);
        _app.Recorder.StateChanged += on => Avalonia.Threading.Dispatcher.UIThread.Post(() => SetRecording(on));
        RefreshRecent();
    }

    /// <summary>Builds the menu again in the current language.</summary>
    public void Rebuild()
    {
        _icon.Menu = BuildMenu();
        RefreshRecent();
        SetRecording(_isRecording);
        if (_captureHint is not null) SetCaptureHint(_captureHint);
    }

    private NativeMenu BuildMenu()
    {
        var menu = new NativeMenu();
        menu.Add(Item(T("Open Cometsnap"), () => _app.Flyout.ShowNearPointer()));
        menu.Add(new NativeMenuItemSeparator());
        _captureItem = Item(T("Capture window / region"), () => Later(_app.Capture.StartRegion));
        menu.Add(_captureItem);
        menu.Add(Item(T("Capture full screen"), () => Later(_app.Capture.CaptureFullscreen)));
        menu.Add(Item(T("Scrolling capture"), () => Later(_app.Scrolling.Start)));
        menu.Add(Item(T("Copy text from the screen"), () => Later(_app.CopyText.Start)));
        var delayed = new NativeMenuItem(T("Delayed capture")) { Menu = new NativeMenu() };
        foreach (var s in new[] { 3, 5, 10 }) delayed.Menu.Add(Item($"{s} seconds", () => _app.Capture.StartDelayed(s)));
        menu.Add(delayed);

        var record = new NativeMenuItem(T("Record")) { Menu = new NativeMenu() };
        record.Menu.Add(Item(T("Region / window (MP4)"), () => Later(() => _app.Recorder.ToggleRegion(RecordingFormat.Mp4))));
        record.Menu.Add(Item(T("Full screen (MP4)"), () => Later(() => _app.Recorder.ToggleFullscreen(RecordingFormat.Mp4))));
        record.Menu.Add(Item(T("Region / window (GIF)"), () => Later(() => _app.Recorder.ToggleRegion(RecordingFormat.Gif))));
        menu.Add(record);
        _stop = Item(T("Stop recording"), () => _app.Recorder.Stop());
        _stop.IsVisible = false;
        menu.Add(_stop);

        _recent = new NativeMenuItem(T("Recent")) { Menu = new NativeMenu() };
        menu.Add(_recent);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item(T("Open captures folder"), _app.OpenCapturesFolder));
        menu.Add(Item(T("Settings"), _app.OpenSettings));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item(T("Quit Cometsnap"), _app.Quit));

        return menu;
    }

    public void SetCaptureHint(string label)
    {
        _captureHint = label;
        _captureItem.Header = $"{T("Capture window / region")} ({label})";
        _icon.ToolTipText = F("Cometsnap · {0} to capture", label);
    }

    private static NativeMenuItem Item(string header, Action click)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => click();
        return item;
    }

    /// <summary>Let the menu close before the screen gets frozen.</summary>
    private static void Later(Action action) =>
        Avalonia.Threading.DispatcherTimer.RunOnce(action, TimeSpan.FromMilliseconds(250));

    private void RefreshRecent()
    {
        var menu = _recent.Menu!;
        menu.Items.Clear();
        var items = _app.Recent.Snapshot().Take(10).ToList();
        if (items.Count == 0)
        {
            menu.Add(new NativeMenuItem(T("No captures yet")) { IsEnabled = false });
            return;
        }
        foreach (var path in items)
        {
            var p = path;
            menu.Add(Item(Path.GetFileName(path), () =>
            {
                if (MediaKinds.IsRecording(p)) Platform.Tools.Open(p);
                else _app.OpenInEditor(p);
            }));
        }
    }

    /// <summary>Snowy comet in December, pumpkin comet at Halloween; the everyday comet otherwise.</summary>
    public void SetSeason(Season season)
    {
        _normal = SeasonalArt.CreateIcon(season) is { } art ? new WindowIcon(art) : _everyday;
        if (!_isRecording) _icon.Icon = _normal;
    }

    private void SetRecording(bool on)
    {
        _isRecording = on;
        _icon.Icon = on ? _recording : _normal;
        _stop.IsVisible = on;
        _icon.ToolTipText = on ? T("Cometsnap · recording (click the pill or press the shortcut to stop)") : "Cometsnap";
    }

    /// <summary>The comet plus a red dot at the bottom-right, as in the brand guide.</summary>
    private static Bitmap WithRedDot(Bitmap source)
    {
        var size = source.PixelSize;
        var rtb = new RenderTargetBitmap(size, new Vector(96, 96));
        using (var dc = rtb.CreateDrawingContext())
        {
            dc.DrawImage(source, new Rect(0, 0, size.Width, size.Height));
            var r = size.Width * 0.2;
            var c = new Point(size.Width - r - size.Width * 0.04, size.Height - r - size.Height * 0.04);
            dc.DrawEllipse(SpaceTheme.RecordRedBrush, new ImmutablePen(Brushes.White.ToImmutable(), size.Width * 0.035), c, r, r);
        }
        return rtb;
    }

    public void Dispose()
    {
        _icon.IsVisible = false;
        _icon.Dispose();
    }
}
