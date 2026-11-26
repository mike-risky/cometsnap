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
using Cometsnap.Core.Native;
using Cometsnap.Core.Storage;
using Path = System.IO.Path;

namespace Cometsnap.App.Tray;

/// <summary>
/// The left-click panel: big capture buttons and the latest captures as thumbnails.
/// Click a thumbnail to copy it, double-click to open it, drag it into any app.
/// Created once and kept hidden, so it pops instantly.
/// </summary>
public sealed class TrayFlyout : Window
{
    private FrameworkElement? _content;
    private SpaceHeader? _sky;
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
    private IntPtr _hwnd;
    private bool _dirty = true;
    private DateTime _hiddenAt;
    private Point _dragStart;
    private bool _suppressClick;

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
        var buttons = new UniformGrid { Columns = 3, Margin = new Thickness(12, 0, 12, 10) };
        buttons.Children.Add(BigButton("Region / Window", "M2,5 V2 H5 M11,2 H14 V5 M14,11 V14 H11 M5,14 H2 V11", primary: true,
            () => HideThen(_app.Capture.StartRegion)));
        buttons.Children.Add(BigButton("Fullscreen", "M2,3 H14 V11 H2 Z M6,14 H10", primary: false,
            () => HideThen(_app.Capture.CaptureFullscreen)));
        var delayed = BigButton("Delayed", "M8,2.5 A5.5,5.5 0 1 1 7.99,2.5 Z M8,5 V8 L10,10", primary: false, () => { });
        delayed.ContextMenu = DelayMenu();
        delayed.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            delayed.ContextMenu.PlacementTarget = delayed;
            delayed.ContextMenu.Placement = PlacementMode.Bottom;
            delayed.ContextMenu.IsOpen = true;
        };
        buttons.Children.Add(delayed);

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
            Text = "Search by name or date (e.g. 2026-11-26)", Foreground = SpaceTheme.DustBrush, FontSize = 13,
            Margin = new Thickness(27, 6, 0, 0), IsHitTestVisible = false,
        };
        _search.TextChanged += (_, _) => searchHint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var searchBox = new Grid();
        searchBox.Children.Add(_search);
        searchBox.Children.Add(searchHint);

        var recentTitle = new TextBlock
        {
            Text = "RECENT", FontFamily = SpaceTheme.HeadingFont, FontSize = 13, Foreground = SpaceTheme.DustBrush,
            Margin = new Thickness(16, 2, 16, 6),
        };

        // ---- thumbnails
        _grid = new WrapPanel { Margin = new Thickness(12, 0, 4, 8) };
        var scroll = new ScrollViewer
        {
            Content = _grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
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
        _status = new TextBlock { Foreground = SpaceTheme.DustBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var footer = new DockPanel { Margin = new Thickness(12, 8, 12, 10), LastChildFill = false };
        var openFolder = LinkButton("Open folder", () => { Hide(); _app.OpenCapturesFolder(); });
        var settings = LinkButton("Settings", () => { Hide(); _app.OpenSettingsFile(); });
        DockPanel.SetDock(openFolder, Dock.Left);
        DockPanel.SetDock(settings, Dock.Right);
        DockPanel.SetDock(_status, Dock.Left);
        footer.Children.Add(openFolder);
        footer.Children.Add(settings);
        footer.Children.Add(_status);
        _status.Margin = new Thickness(12, 0, 0, 0);

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
        KeyDown += (_, e) => { if (e.Key == Key.Escape) HideFlyout(); };
        _recent.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            _dirty = true;
            if (IsVisible) Populate();
        });
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

    private void HideFlyout()
    {
        if (!IsVisible) return;
        _hiddenAt = DateTime.UtcNow;
        _sky?.Stop();
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

    private ContextMenu DelayMenu()
    {
        var menu = new ContextMenu();
        foreach (var s in new[] { 3, 5, 10 })
        {
            var item = new MenuItem { Header = $"{s} seconds" };
            item.Click += (_, _) => { HideFlyout(); _app.Capture.StartDelayed(s); };
            menu.Items.Add(item);
        }
        return menu;
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
        foreach (var path in paths) _grid.Children.Add(Tile(path));
        _empty.Visibility = paths.Count == 0 && filter.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _status.Text = paths.Count == 0 ? "" : $"{paths.Count} recent";
    }

    private FrameworkElement Tile(string path)
    {
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
        actions.Children.Add(MiniButton("M10.5,2.5 L13.5,5.5 L5.5,13.5 H2.5 V10.5 Z M8.5,4.5 L11.5,7.5", "Edit (circle, arrow, text…)", () => Open(path)));
        actions.Children.Add(MiniButton("M5.5,5.5 H13.5 V13.5 H5.5 Z M2.5,10.5 V2.5 H10.5", "Copy", () => CopyFile(path)));
        actions.Children.Add(MiniButton("M2,4 H6 L7.5,5.5 H14 V13 H2 Z", "Show in folder", () => ShowInFolder(path)));
        actions.Children.Add(MiniButton("M3,4.5 H13 M6,4.5 V2.5 H10 V4.5 M4.5,4.5 L5.2,14 H10.8 L11.5,4.5", "Delete (to Recycle Bin)", () => Delete(path)));

        var frame = new Border
        {
            Width = TileWidth, Height = TileHeight, Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), BorderBrush = SpaceTheme.NavyLightBrush, Background = SpaceTheme.VoidBrush,
            ClipToBounds = true, Cursor = Cursors.Hand,
            ToolTip = $"{Path.GetFileName(path)}\nClick: copy · Double-click: edit · Drag: drop into any app",
        };
        var inner = new Grid { ClipToBounds = true };
        inner.Children.Add(image);
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
        frame.MouseEnter += (_, _) => { frame.BorderBrush = SpaceTheme.CometBlueBrush; actions.Visibility = Visibility.Visible; Zoom(1.04); };
        frame.MouseLeave += (_, _) => { frame.BorderBrush = SpaceTheme.NavyLightBrush; actions.Visibility = Visibility.Collapsed; Zoom(1); };
        frame.MouseLeftButtonDown += (_, e) =>
        {
            _dragStart = e.GetPosition(this);
            _suppressClick = false;
            if (e.ClickCount == 2)
            {
                _suppressClick = true;
                Open(path);
            }
        };
        frame.MouseLeftButtonUp += (_, e) =>
        {
            if (_suppressClick) return;
            CopyFile(path);
        };
        frame.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var d = e.GetPosition(this) - _dragStart;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _suppressClick = true;
            StartDrag(frame, path);
        };
        return frame;
    }

    private async Task LoadThumbAsync(Image target, string path)
    {
        var thumb = await _thumbs.GetAsync(path);
        if (thumb is not null) target.Source = thumb;
    }

    private void StartDrag(DependencyObject source, string path)
    {
        if (!File.Exists(path)) return;
        // A file drop works with Word, Outlook, Teams, browsers, Explorer...
        var data = new DataObject(DataFormats.FileDrop, new[] { path });
        try
        {
            using var stream = File.OpenRead(path);
            var bmp = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            data.SetImage(bmp);
        }
        catch (Exception ex)
        {
            Log.Error("Drag image data failed", ex);
        }
        DragDrop.DoDragDrop(source, data, DragDropEffects.Copy);
    }

    private void CopyFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var bmp = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (ClipboardService.SetImage(bmp)) Flash("Copied to clipboard ✓");
        }
        catch (Exception ex)
        {
            Log.Error("Copy from flyout failed", ex);
            Flash("Could not copy");
        }
    }

    /// <summary>Opens the capture in Cometsnap's editor.</summary>
    private void Open(string path)
    {
        HideFlyout();
        _app.OpenInEditor(path);
    }

    private void ShowInFolder(string path)
    {
        HideFlyout();
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Show in folder failed", ex); }
    }

    private void Delete(string path)
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
            Flash("Moved to the Recycle Bin");
        }
        catch (Exception ex)
        {
            Log.Error("Delete failed", ex);
            Flash("Could not delete");
        }
    }

    private async void Flash(string text)
    {
        _status.Text = text;
        _status.Foreground = SpaceTheme.SunBrush;
        await Task.Delay(1500);
        _status.Foreground = SpaceTheme.DustBrush;
        _status.Text = _grid.Children.Count == 0 ? "" : $"{_grid.Children.Count} recent";
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
