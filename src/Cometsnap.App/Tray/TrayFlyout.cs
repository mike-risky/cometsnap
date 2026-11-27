using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cometsnap.App.Services;
using Cometsnap.App.Theme;
using Cometsnap.Core.Diagnostics;
using Cometsnap.Core.Fun;
using Cometsnap.Core.Native;
using Cometsnap.Core.Storage;
using Path = System.IO.Path;
using Cometsnap.Core.Localization;

namespace Cometsnap.App.Tray;

/// <summary>
/// The left-click panel: big capture buttons and the latest captures as thumbnails.
/// Click a thumbnail to copy it, double-click to open it, drag it into any app.
/// Ctrl+click, Shift+click, a box drawn on empty space or Ctrl+A pick several: dragging
/// any of them drags them all, and Copy / Delete work on all of them.
/// Created once and kept hidden, so it pops instantly.
/// </summary>
public sealed class TrayFlyout : Window
{
    private FrameworkElement? _content;
    private SpaceHeader? _sky;
    private readonly KonamiCode _konami = new();
    // Wide enough for 3 thumbnails per row and 3+ rows, and for every button label in full.
    private const double PanelWidth = 560;
    private const double PanelHeight = 660;
    private const double TileWidth = 160;
    private const double TileHeight = 104;

    private readonly CometApp _app;
    private readonly RecentCaptures _recent;
    private readonly ThumbnailCache _thumbs;
    private readonly WrapPanel _grid;
    private readonly TextBox _search;
    private readonly TextBlock _empty;
    private readonly TextBlock _status;
    private readonly FrameworkElement _selectionBar;
    private readonly TextBlock _selectionText;
    private readonly ScrollViewer _scroll;
    private readonly Canvas _bandLayer;
    private readonly System.Windows.Shapes.Rectangle _band;
    private readonly TraySelection _selection = new();
    private readonly Dictionary<string, (Border Frame, Action Paint)> _tiles = new(StringComparer.OrdinalIgnoreCase);
    private Point? _bandStart;
    private bool _bandMoved;
    private IntPtr _hwnd;
    private bool _dirty = true;
    private DateTime _hiddenAt;
    private Point _dragStart;
    private bool _suppressClick;
    private string? _dragArmed;

    public TrayFlyout(CometApp app, RecentCaptures recent, ThumbnailCache thumbs)
    {
        _app = app;
        _recent = recent;
        _thumbs = thumbs;

        Title = "Cometsnap";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = PanelWidth;
        Height = PanelHeight;
        Left = Top = -32000;
        Background = SpaceTheme.NebulaBrush;
        BorderBrush = SpaceTheme.NavyLightBrush;
        BorderThickness = new Thickness(1);
        FontFamily = SpaceTheme.UiFont;
        Foreground = SpaceTheme.StarWhiteBrush;
        UseLayoutRounding = true;

        // ---- header: night sky, twinkling stars, floating comet
        var header = new SpaceHeader(PanelWidth - 2, 66) { Margin = new Thickness(0, 0, 0, 6) };
        _sky = header;

        // ---- big capture buttons
        var buttons = new UniformGrid { Columns = 4, Margin = new Thickness(12, 0, 12, 10) };
        buttons.Children.Add(BigButton(L.T("Region / Window"), "M2,5 V2 H5 M11,2 H14 V5 M14,11 V14 H11 M5,14 H2 V11", primary: true,
            () => HideThen(_app.Capture.StartRegion)));
        buttons.Children.Add(BigButton(L.T("Fullscreen"), "M2,3 H14 V11 H2 Z M6,14 H10", primary: false,
            () => HideThen(_app.Capture.CaptureFullscreen)));
        // Delayed: a left click opens a small picker (3 s, 5 s, 10 s) that says what it is for.
        Border? delayed = null;
        delayed = BigButton(L.T("Delayed"), "M8,2.5 A5.5,5.5 0 1 1 7.99,2.5 Z M8,5 V8 L10,10", primary: false, () => ShowDelayPicker(delayed!));
        delayed.ToolTip = L.T("Capture after 3, 5 or 10 seconds: time to open a menu or hover something first");
        buttons.Children.Add(delayed);
        // Record: click = region video, right-click = fullscreen / GIF.
        var record = BigButton(L.T("Record"), "M8,3 A5,5 0 1 1 7.99,3 Z M8,6 A2,2 0 1 1 7.99,6 Z", primary: false,
            () => HideThen(() => _app.Recorder.ToggleRegion(Cometsnap.Video.RecordingFormat.Mp4)));
        record.ContextMenu = RecordMenu();
        record.ToolTip = L.T("Click: record a video of an area · Right-click: fullscreen or GIF");
        buttons.Children.Add(record);

        // ---- search
        _search = new TextBox
        {
            Margin = new Thickness(16, 0, 16, 8), Padding = new Thickness(8, 5, 8, 5),
            Background = SpaceTheme.VoidBrush, Foreground = SpaceTheme.StarWhiteBrush, CaretBrush = SpaceTheme.StarWhiteBrush,
            BorderBrush = SpaceTheme.NavyLightBrush, FontSize = 13,
        };
        _search.TextChanged += (_, _) => Populate();
        var searchHint = new TextBlock
        {
            Text = L.T("Search by name or date (e.g. 2026-11-26)"), Foreground = SpaceTheme.DustBrush, FontSize = 13,
            Margin = new Thickness(27, 6, 0, 0), IsHitTestVisible = false,
        };
        _search.TextChanged += (_, _) => searchHint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var searchBox = new Grid();
        searchBox.Children.Add(_search);
        searchBox.Children.Add(searchHint);

        var recentTitle = new TextBlock
        {
            Text = L.T("RECENT"), FontFamily = SpaceTheme.HeadingFont, FontSize = 13, Foreground = SpaceTheme.DustBrush,
            Margin = new Thickness(16, 2, 16, 6),
        };

        // ---- thumbnails
        _grid = new WrapPanel { Margin = new Thickness(12, 0, 4, 8) };
        var scroll = new ScrollViewer
        {
            Content = _grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent, // empty space takes the press that starts a box
            FocusVisualStyle = null,
        };
        _scroll = scroll;
        // The box (rubber band) drawn while picking several captures.
        _band = new System.Windows.Shapes.Rectangle
        {
            Stroke = SpaceTheme.CometBlueBrush, StrokeThickness = 1, Fill = SpaceTheme.WithAlpha(SpaceTheme.CometBlue, 0x30),
            RadiusX = 3, RadiusY = 3, Visibility = Visibility.Collapsed,
        };
        _bandLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        _bandLayer.Children.Add(_band);
        scroll.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(BandDown), handledEventsToo: true);
        scroll.MouseMove += BandMove;
        scroll.MouseLeftButtonUp += BandUp;
        scroll.LostMouseCapture += (_, _) => EndBand();
        _empty = new TextBlock
        {
            Text = L.T("No captures yet.") + "\n" + L.T("Press Print Screen and grab something!") + " ☄️",
            Foreground = SpaceTheme.DustBrush, TextAlignment = TextAlignment.Center, FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var gridHost = new Grid();
        gridHost.Children.Add(scroll);
        gridHost.Children.Add(_bandLayer);
        gridHost.Children.Add(_empty);

        // ---- footer
        _status = new TextBlock { Foreground = SpaceTheme.DustBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var footer = new DockPanel { Margin = new Thickness(12, 8, 12, 10), LastChildFill = false };
        var openFolder = LinkButton(L.T("Open folder"), () => { Hide(); _app.OpenCapturesFolder(); });
        var settings = LinkButton(L.T("Settings"), () => { Hide(); _app.OpenSettings(); });
        DockPanel.SetDock(openFolder, Dock.Left);
        DockPanel.SetDock(settings, Dock.Right);
        DockPanel.SetDock(_status, Dock.Left);
        footer.Children.Add(openFolder);
        footer.Children.Add(settings);
        footer.Children.Add(_status);
        _status.Margin = new Thickness(12, 0, 0, 0);

        // ---- shown instead of the count while several captures are picked
        _selectionText = new TextBlock { Foreground = SpaceTheme.SunBrush, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var selectionBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), Visibility = Visibility.Collapsed };
        selectionBar.Children.Add(_selectionText);
        TextBlock Dot() => new() { Text = "  ·  ", Foreground = SpaceTheme.DustBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        selectionBar.Children.Add(Dot());
        selectionBar.Children.Add(LinkButton(L.T("Copy"), CopySelection));
        selectionBar.Children.Add(Dot());
        selectionBar.Children.Add(LinkButton(L.T("Delete"), () => Delete(_selection.Selected)));
        selectionBar.Children.Add(Dot());
        selectionBar.Children.Add(LinkButton(L.T("Clear selection"), _selection.Clear));
        DockPanel.SetDock(selectionBar, Dock.Left);
        footer.Children.Add(selectionBar);
        _selectionBar = selectionBar;
        _selection.Changed += OnSelectionChanged;

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Top);
        DockPanel.SetDock(searchBox, Dock.Top);
        DockPanel.SetDock(recentTitle, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        var footerLine = new Border { Height = 1, Background = SpaceTheme.NavyLightBrush };
        DockPanel.SetDock(footerLine, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(buttons);
        root.Children.Add(searchBox);
        root.Children.Add(recentTitle);
        root.Children.Add(footer);
        root.Children.Add(footerLine);
        root.Children.Add(gridHost);
        _content = root;
        Content = root;

        Deactivated += (_, _) => HideFlyout();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            // Esc first lets go of a selection of several, then closes.
            if (_selection.Count > 1) _selection.Clear();
            else HideFlyout();
        };
        PreviewKeyDown += SelectionKeys;
        // ↑ ↑ ↓ ↓ ← → ← → B A: a meteor shower. Watched, never swallowed, so typing in search still works.
        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (!_konami.Push(key.ToString())) return;
            if (_search.IsKeyboardFocusWithin && _search.Text.EndsWith("b", StringComparison.OrdinalIgnoreCase))
            {
                // The B already went into the search box and the A is about to: keep the search clean.
                _search.Text = _search.Text[..^1];
                e.Handled = true;
            }
            _app.MeteorShowerEasterEgg();
        };
        Action onRecent = () => Dispatcher.BeginInvoke(() =>
        {
            _dirty = true;
            if (IsVisible) Populate();
        });
        _recent.Changed += onRecent;
        Closed += (_, _) => _recent.Changed -= onRecent; // a language switch replaces the panel
    }

    /// <summary>Creates the window once, cloaked, so the first real open is instant.</summary>
    public void Prewarm()
    {
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        var round = Win32.DWMWCP_ROUND;
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        var dark = 1;
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        var cloak = 1;
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_CLOAK, ref cloak, sizeof(int));
        Show();
        Hide();
        cloak = 0;
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_CLOAK, ref cloak, sizeof(int));
    }

    public void Toggle(RECT? iconRect)
    {
        if (IsVisible)
        {
            HideFlyout();
            return;
        }
        // The click on the tray icon deactivates us first; don't reopen on that same click.
        if ((DateTime.UtcNow - _hiddenAt).TotalMilliseconds < 250) return;
        ShowAt(iconRect);
    }

    private void ShowAt(RECT? iconRect)
    {
        if (_hwnd == IntPtr.Zero) Prewarm();
        if (_dirty) Populate();

        Win32.GetCursorPos(out var cursor);
        var anchor = iconRect is { } ir ? new POINT((ir.Left + ir.Right) / 2, (ir.Top + ir.Bottom) / 2) : cursor;
        var work = Win32.GetWorkArea(anchor.X, anchor.Y, out var monitor, out _) ?? new RECT { Right = 1280, Bottom = 800 };

        // Step 1: move onto the target monitor so WPF adopts its DPI. Step 2: size + place in physical pixels.
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, work.Left, work.Top, 0, 0, Win32.SWP_NOACTIVATE | 0x0001 /* NOSIZE */);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var w = (int)Math.Round(PanelWidth * scale);
        var m = (int)Math.Round(12 * scale);
        var h = Math.Min((int)Math.Round(PanelHeight * scale), work.Bottom - work.Top - 2 * m);
        w = Math.Min(w, work.Right - work.Left - 2 * m);

        int x, y;
        if (anchor.Y >= work.Bottom) y = work.Bottom - h - m;            // taskbar at the bottom
        else if (anchor.Y < work.Top) y = work.Top + m;                  // taskbar at the top
        else y = Math.Clamp(anchor.Y - h / 2, work.Top + m, work.Bottom - h - m);

        if (anchor.X >= work.Right) x = work.Right - w - m;              // taskbar on the right
        else if (anchor.X < work.Left) x = work.Left + m;                // taskbar on the left
        else x = Math.Clamp(anchor.X - w / 2, work.Left + m, work.Right - w - m);
        if (anchor.Y >= work.Bottom || anchor.Y < work.Top) x = Math.Clamp(anchor.X - w + m * 3, work.Left + m, work.Right - w - m);

        Show();
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, y, w, h, Win32.SWP_SHOWWINDOW);
        Activate();
        Win32.SetForegroundWindow(_hwnd);
        // Discreet entrance: a short fade and lift, the window itself is already there.
        if (_content is not null) Editing.EditorUi.Entrance(_content, lift: 6, ms: 140);
        _sky?.Start();
        _ = monitor;
    }

    /// <summary>Applies the seasonal skin to the header (snowy or pumpkin comet).</summary>
    public void SetSeason(Season season) => _sky?.SetSeason(season);

    private void HideFlyout()
    {
        if (!IsVisible) return;
        _hiddenAt = DateTime.UtcNow;
        _sky?.Stop();
        _konami.Reset();
        if (_delayPicker is not null) _delayPicker.IsOpen = false;
        _selection.Clear(); // the next open starts fresh
        Hide();
    }

    private void HideThen(Action action)
    {
        HideFlyout();
        // Let the flyout disappear from the screen before it gets captured.
        _ = Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(150);
            action();
        });
    }

    private ContextMenu RecordMenu()
    {
        var menu = new ContextMenu();
        void Add(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => HideThen(action);
            menu.Items.Add(item);
        }
        Add(L.T("Region / window (MP4)"), () => _app.Recorder.ToggleRegion(Cometsnap.Video.RecordingFormat.Mp4));
        Add(L.T("Fullscreen (MP4)"), () => _app.Recorder.ToggleFullscreen(Cometsnap.Video.RecordingFormat.Mp4));
        Add(L.T("Region / window (GIF)"), () => _app.Recorder.ToggleRegion(Cometsnap.Video.RecordingFormat.Gif));
        return menu;
    }

    private System.Windows.Controls.Primitives.Popup? _delayPicker;

    /// <summary>The little "Delayed capture" picker under the button: what it does, and 3 / 5 / 10 seconds.</summary>
    private void ShowDelayPicker(FrameworkElement anchor)
    {
        if (_delayPicker is null)
        {
            var title = new TextBlock { Text = L.T("Delayed capture"), FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = SpaceTheme.StarWhiteBrush };
            var explain = new TextBlock
            {
                Text = L.T("Wait a few seconds before capturing, so you can open a menu or a tooltip first."),
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = SpaceTheme.DustBrush, Margin = new Thickness(0, 4, 0, 10),
            };
            var choices = new UniformGrid { Columns = 3 };
            foreach (var seconds in new[] { 3, 5, 10 })
            {
                var s = seconds;
                var pill = new Border
                {
                    Height = 34, Margin = new Thickness(3, 0, 3, 0), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand,
                    Background = s == 3 ? SpaceTheme.TerracottaBrush : SpaceTheme.NavyBrush,
                    BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(s == 3 ? 0 : 1),
                    Child = new TextBlock
                    {
                        Text = L.F("{0} s", s), FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = SpaceTheme.StarWhiteBrush,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                var rest = pill.Background;
                pill.MouseEnter += (_, _) => pill.Background = SpaceTheme.CometBlueBrush;
                pill.MouseLeave += (_, _) => pill.Background = rest;
                pill.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    _delayPicker!.IsOpen = false;
                    HideFlyout();
                    _app.Capture.StartDelayed(s);
                };
                choices.Children.Add(pill);
            }
            var body = new StackPanel();
            body.Children.Add(title);
            body.Children.Add(explain);
            body.Children.Add(choices);
            _delayPicker = new System.Windows.Controls.Primitives.Popup
            {
                AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.Bottom, PopupAnimation = PopupAnimation.Fade,
                Child = new Border
                {
                    Width = 270, Padding = new Thickness(12, 10, 12, 12), Margin = new Thickness(0, 4, 8, 12), CornerRadius = new CornerRadius(10),
                    Background = SpaceTheme.VoidBrush, BorderBrush = SpaceTheme.CometBlueBrush, BorderThickness = new Thickness(1), Child = body,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Opacity = 0.5, Color = Colors.Black },
                },
            };
        }
        _delayPicker.PlacementTarget = anchor;
        _delayPicker.IsOpen = !_delayPicker.IsOpen;
    }

    // ------------------------------------------------------------------ thumbnails

    private void Populate()
    {
        _dirty = false;
        var filter = _search.Text.Trim();
        var paths = _recent.Snapshot()
            .Where(p => filter.Length == 0 || Path.GetFileName(p).Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _grid.Children.Clear();
        _tiles.Clear();
        _selection.SetItems(paths);
        foreach (var path in paths) _grid.Children.Add(Tile(path));
        _empty.Visibility = paths.Count == 0 && filter.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _status.Text = paths.Count == 0 ? "" : L.F("{0} recent", paths.Count);
        OnSelectionChanged();
    }

    // ------------------------------------------------------------------ picking several

    private void OnSelectionChanged()
    {
        foreach (var (_, paint) in _tiles.Values) paint();
        var several = _selection.Count > 1;
        _selectionText.Text = L.F("{0} selected", _selection.Count);
        _selectionBar.Visibility = several ? Visibility.Visible : Visibility.Collapsed;
        _status.Visibility = several ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool Ctrl => (Keyboard.Modifiers & ModifierKeys.Control) != 0;
    private static bool Shift => (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

    /// <summary>Ctrl+A, Ctrl+C and Delete on the thumbnails. In the search box they keep editing the text.</summary>
    private void SelectionKeys(object sender, KeyEventArgs e)
    {
        var typing = _search.IsKeyboardFocusWithin && _search.Text.Length > 0;
        if (e.Key == Key.A && Ctrl && !typing && _grid.Children.Count > 0)
        {
            _selection.SelectAll();
            _scroll.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.C && Ctrl && !_search.IsKeyboardFocusWithin && _selection.Count > 0)
        {
            CopySelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && !_search.IsKeyboardFocusWithin && _selection.Count > 0)
        {
            Delete(_selection.Selected);
            e.Handled = true;
        }
    }

    private void CopySelection()
    {
        var picked = _selection.Selected;
        if (picked.Count == 1) CopyFile(picked[0]);
        else if (picked.Count > 1) CopyFiles(picked);
    }

    /// <summary>A press on empty space (not on a thumbnail or the scroll bar) starts a box.</summary>
    private void BandDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1 || OnTileOrScrollBar(e.OriginalSource as DependencyObject)) return;
        _bandStart = e.GetPosition(_grid);
        _bandMoved = false;
        _selection.BeginBox(add: Ctrl);
        _scroll.Focus();
        _scroll.CaptureMouse();
        e.Handled = true;
    }

    private bool OnTileOrScrollBar(DependencyObject? hit)
    {
        for (var d = hit; d is not null && d != _scroll; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is ScrollBar) return true;
            if (d is Border { Tag: "tile" }) return true;
        }
        return false;
    }

    private void BandMove(object sender, MouseEventArgs e)
    {
        if (_bandStart is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        // Near the top or bottom edge, scroll so the box can reach captures further down.
        var inView = e.GetPosition(_scroll);
        if (inView.Y < 0) _scroll.LineUp();
        else if (inView.Y > _scroll.ViewportHeight) _scroll.LineDown();

        var now = e.GetPosition(_grid);
        if (!_bandMoved && Math.Abs(now.X - start.X) < 4 && Math.Abs(now.Y - start.Y) < 4) return;
        _bandMoved = true;
        var box = new Rect(start, now);

        var touched = new List<string>();
        foreach (var (path, (frame, _)) in _tiles)
        {
            if (!frame.IsVisible) continue;
            var r = frame.TransformToAncestor(_grid).TransformBounds(new Rect(frame.RenderSize));
            if (r.IntersectsWith(box)) touched.Add(path);
        }
        _selection.UpdateBox(touched);

        var a = _grid.TranslatePoint(box.TopLeft, _bandLayer);
        Canvas.SetLeft(_band, a.X);
        Canvas.SetTop(_band, a.Y);
        _band.Width = box.Width;
        _band.Height = box.Height;
        _band.Visibility = Visibility.Visible;
    }

    private void BandUp(object sender, MouseButtonEventArgs e)
    {
        if (_bandStart is null) return;
        // A plain click on empty space lets go of the selection.
        if (!_bandMoved && !Ctrl) _selection.Clear();
        EndBand();
        if (_scroll.IsMouseCaptured) _scroll.ReleaseMouseCapture();
    }

    private void EndBand()
    {
        if (_bandStart is null) return;
        _bandStart = null;
        _band.Visibility = Visibility.Collapsed;
        _selection.EndBox();
    }

    private FrameworkElement Tile(string path)
    {
        var isRecording = Cometsnap.Core.Storage.MediaKinds.IsRecording(path);
        var image = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var cached = _thumbs.TryGet(path);
        if (cached is not null) image.Source = cached;
        else _ = LoadThumbAsync(image, path);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(3), Visibility = Visibility.Collapsed,
        };
        if (isRecording)
        {
            actions.Children.Add(MiniButton("M5,3 L13,8 L5,13 Z", L.T("Play"), () => Play(path)));
            actions.Children.Add(MiniButton("M5,4.5 m-2,0 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M5,11.5 m-2,0 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M6.6,5.6 L14,12 M6.6,10.4 L14,4", L.T("Trim or cut"), () => Open(path)));
        }
        else actions.Children.Add(MiniButton("M10.5,2.5 L13.5,5.5 L5.5,13.5 H2.5 V10.5 Z M8.5,4.5 L11.5,7.5", L.T("Edit (circle, arrow, text…)"), () => Open(path)));
        actions.Children.Add(MiniButton("M5.5,5.5 H13.5 V13.5 H5.5 Z M2.5,10.5 V2.5 H10.5", L.T("Copy"), () =>
        {
            var targets = _selection.For(path);
            if (targets.Count > 1) CopyFiles(targets); else CopyFile(path);
        }));
        actions.Children.Add(MiniButton("M2,4 H6 L7.5,5.5 H14 V13 H2 Z", L.T("Show in folder"), () => ShowInFolder(path)));
        actions.Children.Add(MiniButton("M3,4.5 H13 M6,4.5 V2.5 H10 V4.5 M4.5,4.5 L5.2,14 H10.8 L11.5,4.5", L.T("Delete (to Recycle Bin)"), () => Delete(_selection.For(path))));

        var frame = new Border
        {
            Width = TileWidth, Height = TileHeight, Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), BorderBrush = SpaceTheme.NavyLightBrush, Background = SpaceTheme.VoidBrush,
            ClipToBounds = true, Cursor = Cursors.Hand, Tag = "tile",
            ToolTip = (isRecording
                ? Path.GetFileName(path) + "\n" + L.T("Click: copy · Double-click: trim · Drag: drop into any app")
                : Path.GetFileName(path) + "\n" + L.T("Click: copy · Double-click: edit · Drag: drop into any app"))
                + "\n" + L.F("{0}+click or drag a box to pick several", "Ctrl"),
        };
        var inner = new Grid { ClipToBounds = true };
        inner.Children.Add(image);
        if (isRecording) inner.Children.Add(RecordingBadge(Cometsnap.Core.Storage.MediaKinds.IsGif(path)));
        // Picked tiles get a blue wash and a check mark in the corner.
        var wash = new Border { Background = SpaceTheme.WithAlpha(SpaceTheme.CometBlue, 0x38), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        var check = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Margin = new Thickness(5),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Background = SpaceTheme.CometBlueBrush, BorderBrush = SpaceTheme.StarWhiteBrush, BorderThickness = new Thickness(1.5),
            IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            Child = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M3.5,8.5 L6.5,11.5 L12.5,4.5"), Stroke = SpaceTheme.StarWhiteBrush, StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                Width = 16, Height = 16, Stretch = Stretch.None, RenderTransform = new ScaleTransform(0.75, 0.75, 8, 8),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        inner.Children.Add(wash);
        inner.Children.Add(check);
        inner.Children.Add(actions);
        frame.Child = new Border { CornerRadius = new CornerRadius(7), ClipToBounds = true, Child = inner };

        var lift = new ScaleTransform(1, 1);
        frame.RenderTransformOrigin = new Point(0.5, 0.5);
        frame.RenderTransform = lift;
        void Zoom(double to)
        {
            var a = new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(110));
            lift.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            lift.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        var hovering = false;
        void Paint()
        {
            var picked = _selection.Contains(path);
            frame.BorderBrush = picked || hovering ? SpaceTheme.CometBlueBrush : SpaceTheme.NavyLightBrush;
            frame.BorderThickness = new Thickness(picked ? 2 : 1);
            wash.Visibility = picked && _selection.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            check.Visibility = wash.Visibility;
        }
        _tiles[path] = (frame, Paint);
        Paint();
        frame.MouseEnter += (_, _) => { hovering = true; Paint(); actions.Visibility = Visibility.Visible; Zoom(1.04); };
        frame.MouseLeave += (_, _) => { hovering = false; Paint(); actions.Visibility = Visibility.Collapsed; Zoom(1); };
        frame.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            _scroll.Focus();
            _dragStart = e.GetPosition(this);
            _dragArmed = path;
            _suppressClick = false;
            if (Ctrl || Shift)
            {
                // Picking several: no copy. A tile Ctrl+clicked off can't start a drag.
                _suppressClick = true;
                _selection.Click(path, toggle: Ctrl, range: Shift);
                if (!_selection.Contains(path)) _dragArmed = null;
                return;
            }
            if (e.ClickCount == 2)
            {
                _suppressClick = true;
                Open(path);
                return;
            }
            // A press on a picked tile keeps the group, so it can be dragged together.
            if (!_selection.Contains(path)) _selection.Click(path);
        };
        frame.MouseLeftButtonUp += (_, e) =>
        {
            _dragArmed = null;
            if (_suppressClick) return;
            _selection.Only(path);
            CopyFile(path);
        };
        frame.MouseMove += (_, e) =>
        {
            // Only a press that started on this tile can drag it (not one on a mini button or elsewhere).
            if (e.LeftButton != MouseButtonState.Pressed || _dragArmed != path) return;
            var d = e.GetPosition(this) - _dragStart;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _suppressClick = true;
            _dragArmed = null;
            Zoom(1);
            var group = _selection.For(path);
            var frames = group.Select(p => _tiles.TryGetValue(p, out var t) ? t.Frame : null).OfType<Border>().ToList();
            foreach (var f in frames) f.Opacity = 0.55;
            frame.Opacity = 0.55;
            try
            {
                if (group.Count > 1) DragOut.StartFiles(frame, group, image.Source);
                else DragOut.Start(frame, path, null, image.Source);
            }
            finally
            {
                foreach (var f in frames) f.Opacity = 1;
                frame.Opacity = 1;
            }
        };
        return frame;
    }

    /// <summary>Bottom-left tag on recordings: a play triangle and "MP4" or "GIF".</summary>
    private static FrameworkElement RecordingBadge(bool gif)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M0,0 L7,4 L0,8 Z"),
            Fill = SpaceTheme.StarWhiteBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        });
        row.Children.Add(new TextBlock
        {
            Text = gif ? "GIF" : "MP4",
            Foreground = SpaceTheme.StarWhiteBrush,
            FontFamily = SpaceTheme.UiFont,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
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
        var thumb = await _thumbs.GetAsync(path);
        if (thumb is not null) target.Source = thumb;
    }

    private void CopyFile(string path)
    {
        if (Cometsnap.Core.Storage.MediaKinds.IsRecording(path))
        {
            // Videos and GIFs go on the clipboard as the file: paste into Teams, Slack, mail, Explorer.
            try
            {
                Clipboard.SetFileDropList(new System.Collections.Specialized.StringCollection { path });
                Flash(L.T("Copied the file ✓"));
            }
            catch (Exception ex)
            {
                Log.Error("Copy recording from flyout failed", ex);
                Flash(L.T("Could not copy"));
            }
            return;
        }
        try
        {
            using var stream = File.OpenRead(path);
            var bmp = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (ClipboardService.SetImage(bmp)) Flash(L.T("Copied to clipboard ✓"));
        }
        catch (Exception ex)
        {
            Log.Error("Copy from flyout failed", ex);
            Flash(L.T("Could not copy"));
        }
    }

    /// <summary>Several captures go on the clipboard as files: paste them into Explorer, Outlook, Teams, Slack…</summary>
    private void CopyFiles(IReadOnlyList<string> paths)
    {
        try
        {
            var list = new System.Collections.Specialized.StringCollection();
            foreach (var p in paths.Where(File.Exists)) list.Add(p);
            if (list.Count == 0) return;
            Clipboard.SetFileDropList(list);
            Flash(L.F("Copied {0} files ✓", list.Count));
        }
        catch (Exception ex)
        {
            Log.Error("Copy of several captures from flyout failed", ex);
            Flash(L.T("Could not copy"));
        }
    }

    /// <summary>Opens the capture in Cometsnap's editor (recordings: in the trimmer).</summary>
    private void Open(string path)
    {
        HideFlyout();
        if (Cometsnap.Core.Storage.MediaKinds.IsRecording(path)) _app.OpenTrimmer(path);
        else _app.OpenInEditor(path);
    }

    /// <summary>Plays a recording in the default player.</summary>
    private void Play(string path)
    {
        HideFlyout();
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Could not play the recording", ex); }
    }

    private void ShowInFolder(string path)
    {
        HideFlyout();
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Show in folder failed", ex); }
    }

    private void Delete(IReadOnlyList<string> paths)
    {
        var moved = 0;
        var failed = false;
        foreach (var path in paths.ToList())
        {
            try
            {
                if (File.Exists(path))
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                Cometsnap.Core.Storage.EditSidecar.DeleteFor(path);
                _thumbs.Forget(path);
                _recent.Remove(path);
                moved++;
            }
            catch (Exception ex)
            {
                Log.Error("Delete failed", ex);
                failed = true;
            }
        }
        if (failed) Flash(L.T("Could not delete"));
        else if (moved > 1) Flash(L.F("Moved {0} files to the Recycle Bin", moved));
        else if (moved == 1) Flash(L.T("Moved to the Recycle Bin"));
    }

    private async void Flash(string text)
    {
        _status.Text = text;
        _status.Foreground = SpaceTheme.SunBrush;
        await Task.Delay(1500);
        _status.Foreground = SpaceTheme.DustBrush;
        _status.Text = _grid.Children.Count == 0 ? "" : L.F("{0} recent", _grid.Children.Count);
    }

    // ------------------------------------------------------------------ building blocks

    private static Border BigButton(string label, string icon, bool primary, Action click)
    {
        var normal = primary ? SpaceTheme.TerracottaBrush : SpaceTheme.NavyBrush;
        var hover = primary ? SpaceTheme.WithAlpha(SpaceTheme.Terracotta, 0xD0) : SpaceTheme.NavyLightBrush;
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(icon), Stroke = SpaceTheme.StarWhiteBrush, StrokeThickness = 1.6, Width = 16, Height = 16,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            Margin = new Thickness(0, 0, 0, 6),
        });
        // Labels wrap onto a second line instead of being cut, whatever the language.
        stack.Children.Add(new TextBlock
        {
            Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None, Margin = new Thickness(4, 0, 4, 0),
        });
        var b = new Border
        {
            MinHeight = 68, Padding = new Thickness(0, 8, 0, 8), Margin = new Thickness(4), CornerRadius = new CornerRadius(10), Background = normal,
            BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(primary ? 0 : 1),
            Cursor = Cursors.Hand, Child = stack,
        };
        b.MouseEnter += (_, _) => b.Background = hover;
        b.MouseLeave += (_, _) => b.Background = normal;
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    private static Border MiniButton(string icon, string tip, Action click)
    {
        var b = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(5), Margin = new Thickness(2, 0, 0, 0),
            Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xD8), ToolTip = tip, Cursor = Cursors.Hand,
            Child = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(icon), Stroke = SpaceTheme.StarWhiteBrush, StrokeThickness = 1.3, Width = 16, Height = 16,
                StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                RenderTransform = new ScaleTransform(0.8, 0.8, 8, 8),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        b.MouseEnter += (_, _) => b.Background = SpaceTheme.CometBlueBrush;
        b.MouseLeave += (_, _) => b.Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xD8);
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    private static TextBlock LinkButton(string text, Action click)
    {
        var t = new TextBlock { Text = text, Foreground = SpaceTheme.DustBrush, FontSize = 12, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        t.MouseEnter += (_, _) => t.Foreground = SpaceTheme.StarWhiteBrush;
        t.MouseLeave += (_, _) => t.Foreground = SpaceTheme.DustBrush;
        t.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return t;
    }
}
