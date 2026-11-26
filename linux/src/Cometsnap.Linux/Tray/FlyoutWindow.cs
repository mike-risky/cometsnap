using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Platform;
using Cometsnap.Linux.Services;
using Cometsnap.Linux.Theme;
using Path = System.IO.Path;

namespace Cometsnap.Linux.Tray;

/// <summary>
/// The tray panel: big capture buttons and the latest captures as thumbnails, 3 per row.
/// Click a thumbnail to copy it, double-click to open it. Created once and kept hidden, so it pops instantly.
/// </summary>
public sealed class FlyoutWindow : Window
{
    private const double PanelWidth = 560;
    private const double PanelHeight = 660;
    private const double TileWidth = 160;
    private const double TileHeight = 104;

    private readonly CometApp _app;
    private readonly SpaceHeader _sky;
    private readonly WrapPanel _grid;
    private readonly TextBox _search;
    private readonly TextBlock _empty;
    private readonly TextBlock _status;
    private readonly Control _content;
    private bool _dirty = true;
    private DateTime _hiddenAt;
    private DateTime _shownAt;

    public FlyoutWindow(CometApp app)
    {
        _app = app;
        Title = "Cometsnap";
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        Width = PanelWidth;
        Height = PanelHeight;
        Background = SpaceTheme.NebulaBrush;
        FontFamily = SpaceTheme.UiFont;
        Foreground = SpaceTheme.StarWhiteBrush;
        Icon = new WindowIcon(SpaceTheme.Logo);

        _sky = new SpaceHeader(PanelWidth - 2, 66) { Margin = new Thickness(0, 0, 0, 6) };

        // ---- big capture buttons
        var buttons = new UniformGrid { Columns = 4, Margin = new Thickness(12, 0, 12, 10) };
        buttons.Children.Add(BigButton("Region / Window", "M2,5 V2 H5 M11,2 H14 V5 M14,11 V14 H11 M5,14 H2 V11", true,
            "Pick a window or drag an area (Print Screen)", () => HideThen(_app.Capture.StartRegion)));
        buttons.Children.Add(BigButton("Fullscreen", "M2,3 H14 V11 H2 Z M6,14 H10", false,
            "The whole screen under the mouse (Ctrl+Print Screen)", () => HideThen(_app.Capture.CaptureFullscreen)));
        var delayed = BigButton("Delayed", "M8,2.5 A5.5,5.5 0 1 1 7.99,2.5 Z M8,5 V8 L10,10", false, "Capture after 3, 5 or 10 seconds", () => { });
        AttachMenu(delayed, new[]
        {
            ("3 seconds", (Action)(() => { HideFlyout(); _app.Capture.StartDelayed(3); })),
            ("5 seconds", () => { HideFlyout(); _app.Capture.StartDelayed(5); }),
            ("10 seconds", () => { HideFlyout(); _app.Capture.StartDelayed(10); }),
        });
        buttons.Children.Add(delayed);
        var record = BigButton("Record", "M8,3 A5,5 0 1 1 7.99,3 Z M8,6 A2,2 0 1 1 7.99,6 Z", false,
            "Record a video (Shift+Print Screen). Right-click for full screen or GIF",
            () => HideThen(() => _app.Recorder.ToggleRegion(Recording.RecordingFormat.Mp4)));
        AttachMenu(record, new[]
        {
            ("Region / window (MP4)", (Action)(() => HideThen(() => _app.Recorder.ToggleRegion(Recording.RecordingFormat.Mp4)))),
            ("Fullscreen (MP4)", () => HideThen(() => _app.Recorder.ToggleFullscreen(Recording.RecordingFormat.Mp4))),
            ("Region / window (GIF)", () => HideThen(() => _app.Recorder.ToggleRegion(Recording.RecordingFormat.Gif))),
        }, rightClickOnly: true);
        buttons.Children.Add(record);

        // ---- search
        _search = new TextBox
        {
            Margin = new Thickness(16, 0, 16, 8),
            Padding = new Thickness(10, 6),
            Background = SpaceTheme.VoidBrush,
            Foreground = SpaceTheme.StarWhiteBrush,
            BorderBrush = SpaceTheme.NavyLightBrush,
            FontSize = 13,
            Watermark = "Search by name or date (e.g. 2026-11-26)",
        };
        _search.TextChanged += (_, _) => Populate();

        var recentTitle = new TextBlock
        {
            Text = "RECENT", FontFamily = SpaceTheme.HeadingFont, FontSize = 13, Foreground = SpaceTheme.DustBrush,
            Margin = new Thickness(16, 2, 16, 6),
        };

        // ---- thumbnails
        _grid = new WrapPanel { Margin = new Thickness(16, 0, 4, 8), ItemWidth = TileWidth + 10, ItemHeight = TileHeight + 10 };
        var scroll = new ScrollViewer
        {
            Content = _grid,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        _empty = new TextBlock
        {
            Text = "No captures yet.\nPress Print Screen and grab something! ☄️",
            Foreground = SpaceTheme.DustBrush, TextAlignment = TextAlignment.Center, FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var gridHost = new Grid();
        gridHost.Children.Add(scroll);
        gridHost.Children.Add(_empty);

        // ---- footer
        _status = new TextBlock { Foreground = SpaceTheme.DustBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var footer = new DockPanel { Margin = new Thickness(12, 8, 12, 10), LastChildFill = false };
        var openFolder = Link("Open folder", () => { HideFlyout(); _app.OpenCapturesFolder(); });
        var settings = Link("Settings", () => { HideFlyout(); _app.OpenSettings(); });
        DockPanel.SetDock(openFolder, Dock.Left);
        DockPanel.SetDock(settings, Dock.Right);
        DockPanel.SetDock(_status, Dock.Left);
        footer.Children.Add(openFolder);
        footer.Children.Add(settings);
        footer.Children.Add(_status);

        var root = new DockPanel();
        var footerLine = new Border { Height = 1, Background = SpaceTheme.NavyLightBrush };
        DockPanel.SetDock(_sky, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Top);
        DockPanel.SetDock(_search, Dock.Top);
        DockPanel.SetDock(recentTitle, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        DockPanel.SetDock(footerLine, Dock.Bottom);
        root.Children.Add(_sky);
        root.Children.Add(buttons);
        root.Children.Add(_search);
        root.Children.Add(recentTitle);
        root.Children.Add(footer);
        root.Children.Add(footerLine);
        root.Children.Add(gridHost);
        _content = root;
        Content = new Border { BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1), Child = root };

        Deactivated += (_, _) =>
        {
            // Some window managers send a stray deactivate right after showing.
            if ((DateTime.UtcNow - _shownAt).TotalMilliseconds > 300) HideFlyout();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) HideFlyout(); };
        _app.Recent.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            _dirty = true;
            if (IsVisible) Populate();
        });
    }

    public void Toggle()
    {
        if (IsVisible)
        {
            HideFlyout();
            return;
        }
        // The click on the tray icon may deactivate us first; don't reopen on that same click.
        if ((DateTime.UtcNow - _hiddenAt).TotalMilliseconds < 250) return;
        ShowNearPointer();
    }

    public void ShowNearPointer()
    {
        if (_dirty) Populate();
        var pointer = X11.PointerPosition() ?? (0, 0);
        var screen = Screens.ScreenFromPoint(new PixelPoint(pointer.X, pointer.Y)) ?? Screens.Primary;
        var work = screen?.WorkingArea ?? new Avalonia.PixelRect(0, 0, 1280, 800);
        var scale = screen?.Scaling ?? 1;
        var w = (int)(PanelWidth * scale);
        var h = (int)Math.Min(PanelHeight * scale, work.Height - 24 * scale);
        Height = h / scale;
        var m = (int)(12 * scale);
        // The tray sits on a panel edge: open from the edge the pointer is closest to.
        var x = Math.Clamp(pointer.X - w / 2, work.X + m, work.Right - w - m);
        var nearTop = pointer.Y - work.Y < work.Bottom - pointer.Y;
        var y = nearTop ? work.Y + m : work.Bottom - h - m;
        Position = new PixelPoint(x, y);
        _shownAt = DateTime.UtcNow;
        Show();
        Activate();
        if (TryGetPlatformHandle()?.Handle is { } handle) X11.ForceFocus(handle);
        Ui.Entrance(_content, lift: 6, ms: 140);
        _sky.Start();
    }

    public void HideFlyout()
    {
        if (!IsVisible) return;
        _hiddenAt = DateTime.UtcNow;
        _sky.Stop();
        Hide();
    }

    private void HideThen(Action action)
    {
        HideFlyout();
        // Let the panel disappear from the screen before it gets captured.
        DispatcherTimer.RunOnce(action, TimeSpan.FromMilliseconds(180));
    }

    // ------------------------------------------------------------------ building blocks

    private static Border BigButton(string label, string icon, bool primary, string tip, Action click)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 6 };
        var i = Ui.Icon(icon, 24, thickness: 1.4);
        i.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(i);
        stack.Children.Add(new TextBlock
        {
            Text = label, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = SpaceTheme.StarWhiteBrush,
            HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.NoWrap,
        });
        var b = Ui.Button(stack, tip, click,
            primary ? SpaceTheme.TerracottaBrush : SpaceTheme.NavyBrush,
            primary ? new ImmutableSolidColorBrush(Color.FromRgb(0xF0, 0x6A, 0x4E)) : SpaceTheme.HoverBrush,
            new Thickness(4, 10), 10);
        b.Margin = new Thickness(4);
        b.Height = 74;
        if (!primary)
        {
            b.BorderBrush = SpaceTheme.NavyLightBrush;
            b.BorderThickness = new Thickness(1);
        }
        return b;
    }

    private static void AttachMenu(Border button, IEnumerable<(string Text, Action Action)> items, bool rightClickOnly = false)
    {
        var menu = new ContextMenu();
        foreach (var (text, action) in items)
        {
            var item = new MenuItem { Header = text };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        button.ContextMenu = menu;
        if (!rightClickOnly)
            button.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left) menu.Open(button);
            };
    }

    private static Control Link(string text, Action click)
    {
        var t = new TextBlock { Text = text, Foreground = SpaceTheme.DustBrush, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        var b = Ui.Button(t, text, click, padding: new Thickness(8, 4));
        b.PointerEntered += (_, _) => t.Foreground = SpaceTheme.StarWhiteBrush;
        b.PointerExited += (_, _) => t.Foreground = SpaceTheme.DustBrush;
        return b;
    }

    // ------------------------------------------------------------------ thumbnails

    private void Populate()
    {
        _dirty = false;
        var filter = _search.Text?.Trim() ?? "";
        var paths = _app.Recent.Snapshot()
            .Where(p => filter.Length == 0 || Path.GetFileName(p).Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _grid.Children.Clear();
        foreach (var path in paths) _grid.Children.Add(Tile(path));
        _empty.IsVisible = paths.Count == 0 && filter.Length == 0;
        _status.Text = paths.Count == 0 ? "" : $"{paths.Count} recent";
    }

    private Control Tile(string path)
    {
        var isRecording = MediaKinds.IsRecording(path);
        var image = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
        if (_app.Thumbs.TryGet(path) is { } cached) image.Source = cached;
        else _ = LoadThumbAsync(image, path);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(3), IsVisible = false, Spacing = 2,
        };
        if (isRecording) actions.Children.Add(Mini("M5,3 L13,8 L5,13 Z", "Play", () => Open(path)));
        else actions.Children.Add(Mini("M10.5,2.5 L13.5,5.5 L5.5,13.5 H2.5 V10.5 Z M8.5,4.5 L11.5,7.5", "Edit (circle, arrow, text…)", () => Open(path)));
        actions.Children.Add(Mini("M5.5,5.5 H13.5 V13.5 H5.5 Z M2.5,10.5 V2.5 H10.5", "Copy", () => _ = CopyAsync(path)));
        actions.Children.Add(Mini("M2,4 H6 L7.5,5.5 H14 V13 H2 Z", "Show in folder", () => { HideFlyout(); Tools.ShowInFolder(path); }));
        actions.Children.Add(Mini("M3,4.5 H13 M6,4.5 V2.5 H10 V4.5 M4.5,4.5 L5.2,14 H10.8 L11.5,4.5", "Move to trash", () => Delete(path)));

        var inner = new Grid { ClipToBounds = true };
        inner.Children.Add(image);
        if (isRecording) inner.Children.Add(RecordingBadge(MediaKinds.IsGif(path)));
        inner.Children.Add(actions);

        var scale = new ScaleTransform(1, 1);
        var frame = new Border
        {
            Width = TileWidth, Height = TileHeight, CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), BorderBrush = SpaceTheme.NavyLightBrush, Background = SpaceTheme.VoidBrush,
            ClipToBounds = true, Cursor = new Cursor(StandardCursorType.Hand),
            Child = new Border { CornerRadius = new CornerRadius(7), ClipToBounds = true, Child = inner },
            RenderTransform = scale, RenderTransformOrigin = RelativePoint.Center,
        };
        ToolTip.SetTip(frame, isRecording
            ? $"{Path.GetFileName(path)}\nClick: copy · Double-click: play"
            : $"{Path.GetFileName(path)}\nClick: copy · Double-click: edit");
        frame.PointerEntered += (_, _) => { frame.BorderBrush = SpaceTheme.CometBlueBrush; actions.IsVisible = true; scale.ScaleX = scale.ScaleY = 1.04; };
        frame.PointerExited += (_, _) => { frame.BorderBrush = SpaceTheme.NavyLightBrush; actions.IsVisible = false; scale.ScaleX = scale.ScaleY = 1; };

        IDisposable? pendingClick = null;
        frame.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(frame).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount >= 2)
            {
                pendingClick?.Dispose();
                pendingClick = null;
                Open(path);
            }
        };
        frame.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left || !frame.IsPointerOver) return;
            // Wait a moment: a double-click opens instead of copying.
            pendingClick?.Dispose();
            pendingClick = DispatcherTimer.RunOnce(() => { pendingClick = null; _ = CopyAsync(path); }, TimeSpan.FromMilliseconds(260));
        };
        return frame;
    }

    private static Control Mini(string icon, string tip, Action click)
    {
        var b = Ui.Button(Ui.Icon(icon, 14), tip, click, SpaceTheme.WithAlpha(SpaceTheme.Void, 0xD8), SpaceTheme.CometBlueBrush, new Thickness(5), 6);
        return b;
    }

    private static Control RecordingBadge(bool gif)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M0,0 L7,4 L0,8 Z"), Fill = SpaceTheme.StarWhiteBrush, VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = gif ? "GIF" : "MP4", Foreground = SpaceTheme.StarWhiteBrush, FontSize = 10, FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return new Border
        {
            Child = row,
            Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xD0),
            BorderBrush = SpaceTheme.WithAlpha(SpaceTheme.RecordRed, 0xE0),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 2, 6, 2),
            Margin = new Thickness(5),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
        };
    }

    private async Task LoadThumbAsync(Image target, string path)
    {
        var thumb = await _app.Thumbs.GetAsync(path);
        if (thumb is not null) target.Source = thumb;
    }

    private async Task CopyAsync(string path)
    {
        if (MediaKinds.IsRecording(path))
        {
            Flash(await _app.Clipboard.SetFilesAsync(new[] { path }) ? "Copied the file ✓" : "Could not copy");
            return;
        }
        try
        {
            var png = await File.ReadAllBytesAsync(path);
            Flash(await _app.Clipboard.SetPngAsync(png) ? "Copied to clipboard ✓" : "Could not copy");
        }
        catch (Exception ex)
        {
            Log.Error("Copy from flyout failed", ex);
            Flash("Could not copy");
        }
    }

    private void Open(string path)
    {
        HideFlyout();
        if (MediaKinds.IsRecording(path)) Tools.Open(path);
        else _app.OpenInEditor(path);
    }

    private void Delete(string path)
    {
        if (!Tools.Trash(path)) { Flash("Could not delete"); return; }
        EditSidecar.DeleteFor(path);
        _app.Thumbs.Forget(path);
        _app.Recent.Remove(path);
        Flash("Moved to trash");
    }

    private void Flash(string text)
    {
        _status.Text = text;
        DispatcherTimer.RunOnce(() => _status.Text = _app.Recent.Snapshot().Count is var n && n > 0 ? $"{n} recent" : "", TimeSpan.FromSeconds(2));
    }
}
