using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cometsnap.Core.Storage;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Platform;
using Cometsnap.Linux.Services;
using Cometsnap.Linux.Theme;
using Path = System.IO.Path;

namespace Cometsnap.Linux.Tray;

/// <summary>
/// The tray panel: big capture buttons and the latest captures as thumbnails, 3 per row.
/// Click a thumbnail to copy it, double-click to open it. Ctrl+click, Shift+click, a box drawn on
/// empty space or Ctrl+A pick several: dragging any of them drags them all, and Copy / Delete work
/// on all of them. Created once and kept hidden, so it pops instantly.
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
    private readonly KonamiCode _konami = new();
    private string? _dragArmed;
    private Point _dragFrom;
    private bool _picking;
    private readonly ScrollViewer _scroll;
    private readonly Canvas _bandLayer;
    private readonly Border _band;
    private readonly StackPanel _selectionBar;
    private readonly TextBlock _selectionText;
    private readonly TraySelection _selection = new();
    private readonly Dictionary<string, (Border Frame, Action Paint)> _tiles = new(StringComparer.OrdinalIgnoreCase);
    private Point? _bandStart;
    private bool _bandMoved;

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
        buttons.Children.Add(BigButton(T("Region / Window"), "M2,5 V2 H5 M11,2 H14 V5 M14,11 V14 H11 M5,14 H2 V11", true,
            T("Pick a window or drag an area (Print Screen)"), () => HideThen(_app.Capture.StartRegion)));
        buttons.Children.Add(BigButton(T("Fullscreen"), "M2,3 H14 V11 H2 Z M6,14 H10", false,
            T("The whole screen under the mouse (Ctrl+Print Screen)"), () => HideThen(_app.Capture.CaptureFullscreen)));
        var delayed = BigButton(T("Delayed"), "M8,2.5 A5.5,5.5 0 1 1 7.99,2.5 Z M8,5 V8 L10,10", false, T("Capture after 3, 5 or 10 seconds"), () => { });
        AttachMenu(delayed, new[]
        {
            (F("{0} seconds", 3), (Action)(() => { HideFlyout(); _app.Capture.StartDelayed(3); })),
            (F("{0} seconds", 5), () => { HideFlyout(); _app.Capture.StartDelayed(5); }),
            (F("{0} seconds", 10), () => { HideFlyout(); _app.Capture.StartDelayed(10); }),
        }, note: T("Wait a few seconds before capturing, so you can open a menu or a tooltip first."));
        buttons.Children.Add(delayed);
        var record = BigButton(T("Record"), "M8,3 A5,5 0 1 1 7.99,3 Z M8,6 A2,2 0 1 1 7.99,6 Z", false,
            T("Record a video (Shift+Print Screen). Right-click for full screen or GIF"),
            () => HideThen(() => _app.Recorder.ToggleRegion(Recording.RecordingFormat.Mp4)));
        AttachMenu(record, new[]
        {
            (T("Region / window (MP4)"), (Action)(() => HideThen(() => _app.Recorder.ToggleRegion(Recording.RecordingFormat.Mp4)))),
            (T("Fullscreen (MP4)"), () => HideThen(() => _app.Recorder.ToggleFullscreen(Recording.RecordingFormat.Mp4))),
            (T("Region / window (GIF)"), () => HideThen(() => _app.Recorder.ToggleRegion(Recording.RecordingFormat.Gif))),
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
            Watermark = T("Search by name or date (e.g. 2026-11-26)"),
        };
        _search.TextChanged += (_, _) => Populate();

        var recentTitle = new TextBlock
        {
            Text = T("RECENT"), FontFamily = SpaceTheme.HeadingFont, FontSize = 13, Foreground = SpaceTheme.DustBrush,
            Margin = new Thickness(16, 2, 16, 6),
        };

        // ---- thumbnails
        _grid = new WrapPanel { Margin = new Thickness(16, 0, 4, 8), ItemWidth = TileWidth + 10, ItemHeight = TileHeight + 10 };
        var scroll = new ScrollViewer
        {
            Content = _grid,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent, // empty space takes the press that starts a box
        };
        _scroll = scroll;
        // The box (rubber band) drawn while picking several captures.
        _band = new Border
        {
            BorderBrush = SpaceTheme.CometBlueBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
            Background = SpaceTheme.WithAlpha(SpaceTheme.CometBlue, 0x30), IsVisible = false,
        };
        _bandLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        _bandLayer.Children.Add(_band);
        scroll.AddHandler(PointerPressedEvent, BandDown, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        scroll.PointerMoved += BandMove;
        scroll.PointerReleased += (_, _) => EndBand(clickOnly: true);
        scroll.PointerCaptureLost += (_, _) => EndBand(clickOnly: false);
        _empty = new TextBlock
        {
            Text = T("No captures yet.") + "\n" + T("Press Print Screen and grab something!") + " ☄️",
            Foreground = SpaceTheme.DustBrush, TextAlignment = TextAlignment.Center, FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var gridHost = new Grid();
        gridHost.Children.Add(scroll);
        gridHost.Children.Add(_bandLayer);
        gridHost.Children.Add(_empty);

        // ---- footer
        _status = new TextBlock { Foreground = SpaceTheme.DustBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var footer = new DockPanel { Margin = new Thickness(12, 8, 12, 10), LastChildFill = false };
        var openFolder = Link(T("Open folder"), () => { HideFlyout(); _app.OpenCapturesFolder(); });
        var settings = Link(T("Settings"), () => { HideFlyout(); _app.OpenSettings(); });
        DockPanel.SetDock(openFolder, Dock.Left);
        DockPanel.SetDock(settings, Dock.Right);
        DockPanel.SetDock(_status, Dock.Left);
        footer.Children.Add(openFolder);
        footer.Children.Add(settings);
        footer.Children.Add(_status);

        // ---- shown instead of the count while several captures are picked
        _selectionText = new TextBlock { Foreground = SpaceTheme.SunBrush, FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _selectionBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), IsVisible = false, Spacing = 2 };
        _selectionBar.Children.Add(_selectionText);
        _selectionBar.Children.Add(Link(T("Copy"), CopySelection));
        _selectionBar.Children.Add(Link(T("Delete"), () => Delete(_selection.Selected)));
        _selectionBar.Children.Add(Link(T("Clear selection"), _selection.Clear));
        DockPanel.SetDock(_selectionBar, Dock.Left);
        footer.Children.Add(_selectionBar);
        _selection.Changed += OnSelectionChanged;

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
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            // Esc first lets go of a selection of several, then closes.
            if (_selection.Count > 1) _selection.Clear();
            else HideFlyout();
        };
        AddHandler(KeyDownEvent, SelectionKeys, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // ↑ ↑ ↓ ↓ ← → ← → B A: a meteor shower. Watched, never swallowed, so typing in search still works.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (!_konami.Push(e.Key.ToString())) return;
            if (_search.IsFocused && _search.Text is { } text && text.EndsWith("b", StringComparison.OrdinalIgnoreCase))
            {
                // The B already went into the search box and the A is about to: keep the search clean.
                _search.Text = text[..^1];
                e.Handled = true;
            }
            _app.MeteorShowerEasterEgg();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Action onRecent = () => Dispatcher.UIThread.Post(() =>
        {
            _dirty = true;
            if (IsVisible) Populate();
        });
        _app.Recent.Changed += onRecent;
        Closed += (_, _) => _app.Recent.Changed -= onRecent;
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
        _konami.Reset();
        _sky.Stop();
        _selection.Clear(); // the next open starts fresh
        Hide();
    }

    /// <summary>Applies the seasonal skin to the header (snowy or pumpkin comet).</summary>
    public void SetSeason(Season season) => _sky.SetSeason(season);

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

    private static void AttachMenu(Border button, IEnumerable<(string Text, Action Action)> items, bool rightClickOnly = false, string? note = null)
    {
        var menu = new ContextMenu();
        if (note is not null)
        {
            menu.Items.Add(new MenuItem
            {
                Header = new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap, MaxWidth = 240, FontSize = 12, Foreground = SpaceTheme.DustBrush },
                IsHitTestVisible = false, Focusable = false,
            });
            menu.Items.Add(new Separator());
        }
        foreach (var (text, action) in items)
        {
            var item = new MenuItem { Header = text };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        button.ContextMenu = menu;
        // The button marks its own release as handled, so listen to handled events too.
        if (!rightClickOnly)
            button.AddHandler(PointerReleasedEvent, (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left && button.IsPointerOver) menu.Open(button);
            }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
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
        _tiles.Clear();
        _selection.SetItems(paths);
        foreach (var path in paths) _grid.Children.Add(Tile(path));
        _empty.IsVisible = paths.Count == 0 && filter.Length == 0;
        _status.Text = paths.Count == 0 ? "" : F("{0} recent", paths.Count);
        OnSelectionChanged();
    }

    // ------------------------------------------------------------------ picking several

    private void OnSelectionChanged()
    {
        foreach (var (_, paint) in _tiles.Values) paint();
        var several = _selection.Count > 1;
        _selectionText.Text = F("{0} selected", _selection.Count);
        _selectionBar.IsVisible = several;
        _status.IsVisible = !several;
    }

    /// <summary>Ctrl+A, Ctrl+C and Delete on the thumbnails. In the search box they keep editing the text.</summary>
    private void SelectionKeys(object? sender, KeyEventArgs e)
    {
        var inSearch = _search.IsFocused;
        var typing = inSearch && !string.IsNullOrEmpty(_search.Text);
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (e.Key == Key.A && ctrl && !typing && _grid.Children.Count > 0)
        {
            _selection.SelectAll();
            _scroll.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.C && ctrl && !inSearch && _selection.Count > 0)
        {
            CopySelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && !inSearch && _selection.Count > 0)
        {
            Delete(_selection.Selected);
            e.Handled = true;
        }
    }

    private void CopySelection()
    {
        var picked = _selection.Selected;
        if (picked.Count == 1) _ = CopyAsync(picked[0]);
        else if (picked.Count > 1) _ = CopyFilesAsync(picked);
    }

    /// <summary>A press on empty space (not on a thumbnail or the scroll bar) starts a box.</summary>
    private void BandDown(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_scroll).Properties.IsLeftButtonPressed || e.ClickCount > 1) return;
        for (var v = e.Source as Visual; v is not null && v != _scroll; v = v.GetVisualParent())
            if (v is ScrollBar || v is Border { Tag: "tile" }) return;
        _bandStart = e.GetPosition(_grid);
        _bandMoved = false;
        _selection.BeginBox(add: e.KeyModifiers.HasFlag(KeyModifiers.Control));
        _bandAdd = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        _scroll.Focus();
        e.Pointer.Capture(_scroll);
        e.Handled = true;
    }

    private bool _bandAdd;

    private void BandMove(object? sender, PointerEventArgs e)
    {
        if (_bandStart is not { } start) return;
        // Near the top or bottom edge, scroll so the box can reach captures further down.
        var inView = e.GetPosition(_scroll);
        if (inView.Y < 0) _scroll.LineUp();
        else if (inView.Y > _scroll.Viewport.Height) _scroll.LineDown();

        var now = e.GetPosition(_grid);
        if (!_bandMoved && Math.Abs(now.X - start.X) < 4 && Math.Abs(now.Y - start.Y) < 4) return;
        _bandMoved = true;
        var box = new Rect(new Point(Math.Min(start.X, now.X), Math.Min(start.Y, now.Y)), new Point(Math.Max(start.X, now.X), Math.Max(start.Y, now.Y)));
        _selection.UpdateBox(_tiles.Where(t => t.Value.Frame.Bounds.Intersects(box)).Select(t => t.Key).ToList());

        if (_grid.TranslatePoint(box.TopLeft, _bandLayer) is { } a)
        {
            Canvas.SetLeft(_band, a.X);
            Canvas.SetTop(_band, a.Y);
        }
        _band.Width = box.Width;
        _band.Height = box.Height;
        _band.IsVisible = true;
    }

    private void EndBand(bool clickOnly)
    {
        if (_bandStart is null) return;
        // A plain click on empty space lets go of the selection.
        if (clickOnly && !_bandMoved && !_bandAdd) _selection.Clear();
        _bandStart = null;
        _band.IsVisible = false;
        _selection.EndBox();
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
        if (isRecording)
        {
            actions.Children.Add(Mini("M5,3 L13,8 L5,13 Z", T("Play"), () => { HideFlyout(); Tools.Open(path); }));
            actions.Children.Add(Mini(Editing.EditCard.ScissorsIcon, T("Trim or cut"), () => Open(path)));
        }
        else actions.Children.Add(Mini("M10.5,2.5 L13.5,5.5 L5.5,13.5 H2.5 V10.5 Z M8.5,4.5 L11.5,7.5", T("Edit (circle, arrow, text…)"), () => Open(path)));
        actions.Children.Add(Mini("M5.5,5.5 H13.5 V13.5 H5.5 Z M2.5,10.5 V2.5 H10.5", T("Copy"), () =>
        {
            var targets = _selection.For(path);
            _ = targets.Count > 1 ? CopyFilesAsync(targets) : CopyAsync(path);
        }));
        actions.Children.Add(Mini("M2,4 H6 L7.5,5.5 H14 V13 H2 Z", T("Show in folder"), () => { HideFlyout(); Tools.ShowInFolder(path); }));
        actions.Children.Add(Mini("M3,4.5 H13 M6,4.5 V2.5 H10 V4.5 M4.5,4.5 L5.2,14 H10.8 L11.5,4.5", T("Move to trash"), () => Delete(_selection.For(path))));

        var inner = new Grid { ClipToBounds = true };
        inner.Children.Add(image);
        if (isRecording) inner.Children.Add(RecordingBadge(MediaKinds.IsGif(path)));
        // Picked tiles get a blue wash and a check mark in the corner.
        var wash = new Border { Background = SpaceTheme.WithAlpha(SpaceTheme.CometBlue, 0x38), IsHitTestVisible = false, IsVisible = false };
        var checkIcon = Ui.Icon("M3.5,8.5 L6.5,11.5 L12.5,4.5", 12, thickness: 2);
        checkIcon.HorizontalAlignment = HorizontalAlignment.Center;
        checkIcon.VerticalAlignment = VerticalAlignment.Center;
        var check = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Margin = new Thickness(5),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Background = SpaceTheme.CometBlueBrush, BorderBrush = SpaceTheme.StarWhiteBrush, BorderThickness = new Thickness(1.5),
            IsHitTestVisible = false, IsVisible = false, Child = checkIcon,
        };
        inner.Children.Add(wash);
        inner.Children.Add(check);
        inner.Children.Add(actions);

        var scale = new ScaleTransform(1, 1);
        var frame = new Border
        {
            Width = TileWidth, Height = TileHeight, CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), BorderBrush = SpaceTheme.NavyLightBrush, Background = SpaceTheme.VoidBrush,
            ClipToBounds = true, Cursor = new Cursor(StandardCursorType.Hand),
            Child = new Border { CornerRadius = new CornerRadius(7), ClipToBounds = true, Child = inner },
            RenderTransform = scale, RenderTransformOrigin = RelativePoint.Center, Tag = "tile",
        };
        ToolTip.SetTip(frame, (isRecording
            ? Path.GetFileName(path) + "\n" + T("Click: copy · Double-click: trim · Drag: drop into any app")
            : Path.GetFileName(path) + "\n" + T("Click: copy · Double-click: edit · Drag: drop into any app"))
            + "\n" + F("{0}+click or drag a box to pick several", "Ctrl"));
        var hovering = false;
        void Paint()
        {
            var picked = _selection.Contains(path);
            frame.BorderBrush = picked || hovering ? SpaceTheme.CometBlueBrush : SpaceTheme.NavyLightBrush;
            frame.BorderThickness = new Thickness(picked ? 2 : 1);
            wash.IsVisible = check.IsVisible = picked && _selection.Count > 1;
        }
        _tiles[path] = (frame, Paint);
        Paint();
        frame.PointerEntered += (_, _) => { hovering = true; Paint(); actions.IsVisible = true; scale.ScaleX = scale.ScaleY = 1.04; };
        frame.PointerExited += (_, _) => { hovering = false; Paint(); actions.IsVisible = false; scale.ScaleX = scale.ScaleY = 1; };

        IDisposable? pendingClick = null;
        frame.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(frame).Properties.IsLeftButtonPressed) return;
            // Only a press that started on this tile can drag it (not one on a mini button or elsewhere).
            _dragArmed = path;
            _dragFrom = e.GetPosition(this);
            _picking = false;
            var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            if (ctrl || shift)
            {
                // Picking several: no copy. A tile Ctrl+clicked off can't start a drag.
                _picking = true;
                _selection.Click(path, toggle: ctrl, range: shift);
                if (!_selection.Contains(path)) _dragArmed = null;
                return;
            }
            if (e.ClickCount >= 2)
            {
                pendingClick?.Dispose();
                pendingClick = null;
                Open(path);
                return;
            }
            // A press on a picked tile keeps the group, so it can be dragged together.
            if (!_selection.Contains(path)) _selection.Click(path);
        };
        frame.PointerMoved += async (_, e) =>
        {
            if (_dragArmed != path || DragOut.Active) return;
            if (!e.GetCurrentPoint(frame).Properties.IsLeftButtonPressed) { _dragArmed = null; return; }
            var d = e.GetPosition(this) - _dragFrom;
            if (Math.Abs(d.X) < 5 && Math.Abs(d.Y) < 5) return;
            _dragArmed = null;
            pendingClick?.Dispose();
            pendingClick = null;
            scale.ScaleX = scale.ScaleY = 1;
            var group = _selection.For(path);
            var frames = group.Select(p => _tiles.TryGetValue(p, out var t) ? t.Frame : null).OfType<Border>().Append(frame).ToList();
            foreach (var f in frames) f.Opacity = 0.55;
            try
            {
                if (group.Count > 1) await DragOut.StartFilesAsync(group, image.Source as Bitmap);
                else await DragOut.StartAsync(path, null, image.Source as Bitmap);
            }
            finally { foreach (var f in frames) f.Opacity = 1; }
        };
        frame.PointerReleased += (_, e) =>
        {
            var armed = _dragArmed == path;
            _dragArmed = null;
            if (!armed || DragOut.Active) return; // the end of a drag, or a press that began elsewhere
            if (e.InitialPressMouseButton != MouseButton.Left || !frame.IsPointerOver || _picking) return;
            // Wait a moment: a double-click opens instead of copying.
            pendingClick?.Dispose();
            pendingClick = DispatcherTimer.RunOnce(() =>
            {
                pendingClick = null;
                _selection.Only(path);
                _ = CopyAsync(path);
            }, TimeSpan.FromMilliseconds(260));
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
            Flash(await _app.Clipboard.SetFilesAsync(new[] { path }) ? T("Copied the file ✓") : T("Could not copy"));
            return;
        }
        try
        {
            var png = await File.ReadAllBytesAsync(path);
            Flash(await _app.Clipboard.SetPngAsync(png) ? T("Copied to clipboard ✓") : T("Could not copy"));
        }
        catch (Exception ex)
        {
            Log.Error("Copy from flyout failed", ex);
            Flash(T("Could not copy"));
        }
    }

    /// <summary>Several captures go on the clipboard as files: paste them into a file manager, chat or mail.</summary>
    private async Task CopyFilesAsync(IReadOnlyList<string> paths)
    {
        var files = paths.Where(File.Exists).ToList();
        if (files.Count == 0) return;
        Flash(await _app.Clipboard.SetFilesAsync(files) ? F("Copied {0} files ✓", files.Count) : T("Could not copy"));
    }

    private void Open(string path)
    {
        HideFlyout();
        if (MediaKinds.IsRecording(path)) _app.OpenTrimmer(path);
        else _app.OpenInEditor(path);
    }

    private void Delete(IReadOnlyList<string> paths)
    {
        var moved = 0;
        var failed = false;
        foreach (var path in paths.ToList())
        {
            if (!Tools.Trash(path)) { failed = true; continue; }
            EditSidecar.DeleteFor(path);
            _app.Thumbs.Forget(path);
            _app.Recent.Remove(path);
            moved++;
        }
        if (failed) Flash(T("Could not delete"));
        else if (moved > 1) Flash(F("Moved {0} files to the trash", moved));
        else if (moved == 1) Flash(T("Moved to trash"));
    }

    private void Flash(string text)
    {
        _status.Text = text;
        DispatcherTimer.RunOnce(() => _status.Text = _app.Recent.Snapshot().Count is var n && n > 0 ? F("{0} recent", n) : "", TimeSpan.FromSeconds(2));
    }
}
