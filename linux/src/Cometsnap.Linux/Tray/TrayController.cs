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
    private readonly WindowIcon _normal;
    private readonly WindowIcon _recording;
    private readonly NativeMenuItem _recent;
    private readonly NativeMenuItem _stop;
    private readonly NativeMenuItem _captureItem;

    public TrayController(CometApp app)
    {
        _app = app;
        _normal = new WindowIcon(SpaceTheme.LoadAsset("cometsnap-256.png"));
        _recording = new WindowIcon(WithRedDot(SpaceTheme.LoadAsset("cometsnap-256.png")));

        var menu = new NativeMenu();
        menu.Add(Item("Open Cometsnap", () => _app.Flyout.ShowNearPointer()));
        menu.Add(new NativeMenuItemSeparator());
        _captureItem = Item("Capture window / region", () => Later(_app.Capture.StartRegion));
        menu.Add(_captureItem);
        menu.Add(Item("Capture full screen", () => Later(_app.Capture.CaptureFullscreen)));
        var delayed = new NativeMenuItem("Delayed capture") { Menu = new NativeMenu() };
        foreach (var s in new[] { 3, 5, 10 }) delayed.Menu.Add(Item($"{s} seconds", () => _app.Capture.StartDelayed(s)));
        menu.Add(delayed);

        var record = new NativeMenuItem("Record") { Menu = new NativeMenu() };
        record.Menu.Add(Item("Region / window (MP4)", () => Later(() => _app.Recorder.ToggleRegion(RecordingFormat.Mp4))));
        record.Menu.Add(Item("Full screen (MP4)", () => Later(() => _app.Recorder.ToggleFullscreen(RecordingFormat.Mp4))));
        record.Menu.Add(Item("Region / window (GIF)", () => Later(() => _app.Recorder.ToggleRegion(RecordingFormat.Gif))));
        menu.Add(record);
        _stop = Item("Stop recording", () => _app.Recorder.Stop());
        _stop.IsVisible = false;
        menu.Add(_stop);

        _recent = new NativeMenuItem("Recent") { Menu = new NativeMenu() };
        menu.Add(_recent);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Open captures folder", _app.OpenCapturesFolder));
        menu.Add(Item("Settings", _app.OpenSettings));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Quit Cometsnap", _app.Quit));

        _icon = new TrayIcon
        {
            Icon = _normal,
            ToolTipText = "Cometsnap",
            Menu = menu,
            IsVisible = true,
        };
        _icon.Clicked += (_, _) => _app.Flyout.Toggle();
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _icon });

        _app.Recent.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshRecent);
        _app.Recorder.StateChanged += on => Avalonia.Threading.Dispatcher.UIThread.Post(() => SetRecording(on));
        RefreshRecent();
    }

    public void SetCaptureHint(string label)
    {
        _captureItem.Header = $"Capture window / region ({label})";
        _icon.ToolTipText = $"Cometsnap · {label} to capture";
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
            menu.Add(new NativeMenuItem("No captures yet") { IsEnabled = false });
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

    private void SetRecording(bool on)
    {
        _icon.Icon = on ? _recording : _normal;
        _stop.IsVisible = on;
        _icon.ToolTipText = on ? "Cometsnap · recording (click the pill or press the shortcut to stop)" : "Cometsnap";
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
