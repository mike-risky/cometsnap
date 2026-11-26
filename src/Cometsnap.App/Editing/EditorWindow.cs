using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Cometsnap.App.Capture;
using Cometsnap.App.Services;
using Cometsnap.App.Theme;
using Cometsnap.Core.Diagnostics;
using Cometsnap.Core.Native;
using Cometsnap.Core.Storage;
using Cometsnap.Editor;

namespace Cometsnap.App.Editing;

/// <summary>
/// The editor: a capture with its shapes, still editable. Opened from the quick bar
/// ("Open in editor"), or from the tray flyout (double-click or the pencil on a thumbnail).
/// The surface works in image pixels; zoom is a LayoutTransform on top.
/// </summary>
public sealed class EditorWindow : Window
{
    private readonly CometApp _app;
    private readonly BitmapSource _base;
    private readonly AnnotationDocument _doc;
    private readonly Canvas _surface;
    private readonly AnnotationLayer _layer;
    private readonly ScaleTransform _zoomTransform = new(1, 1);
    private readonly ScrollViewer _scroller;
    private readonly QuickBar _bar;
    private readonly TextBlock _status;
    private readonly TextBlock _hint;
    private readonly DispatcherTimer _hintTimer;

    private string? _path;
    private bool _dirty;
    private double _zoom = 1;       // 1 = one image pixel per screen pixel
    private double _dpi = 1;
    private AnnotationTool _tool = AnnotationTool.Arrow;
    private string _color = QuickBar.Palette[0];
    private BoxAnnotation? _drawing;
    private TextBox? _textBox;
    private Point _textAt;

    /// <param name="path">The capture file, or null for a new capture that isn't saved yet.</param>
    public EditorWindow(CometApp app, BitmapSource image, AnnotationDocument doc, string? path)
    {
        _app = app;
        _base = image;
        _doc = doc;
        _path = path;
        _dirty = path is null;

        Title = TitleText();
        Background = SpaceTheme.VoidBrush;
        Foreground = SpaceTheme.StarWhiteBrush;
        FontFamily = SpaceTheme.UiFont;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/cometsnap.ico")); }
        catch { /* no icon is fine */ }

        var pw = image.PixelWidth;
        var ph = image.PixelHeight;
        var picture = new Image { Source = image, Width = pw, Height = ph, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        _layer = new AnnotationLayer { Document = _doc, Width = pw, Height = ph };
        _surface = new Canvas
        {
            Width = pw, Height = ph, Background = Brushes.Transparent, ClipToBounds = true,
            Cursor = Cursors.Cross, LayoutTransform = _zoomTransform,
        };
        _surface.Children.Add(picture);
        _surface.Children.Add(_layer);
        _surface.MouseLeftButtonDown += OnSurfaceDown;
        _surface.MouseMove += OnSurfaceMove;
        _surface.MouseLeftButtonUp += OnSurfaceUp;

        var sheet = new Border
        {
            Child = _surface, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24), BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = 0.6, Color = Colors.Black },
        };
        _scroller = new ScrollViewer
        {
            Content = sheet, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = SpaceTheme.NebulaBrush, Focusable = false,
        };
        _scroller.PreviewMouseWheel += OnWheel;

        _bar = new QuickBar(inEditor: true) { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 10) };
        _bar.ToolSelected += t => SetTool(t);
        _bar.ColorSelected += SetColor;
        _bar.UndoClicked += Undo;
        _bar.CopyClicked += Copy;
        _bar.SaveClicked += () => Save();
        _bar.CloseClicked += Close;

        _status = new TextBlock { Foreground = SpaceTheme.DustBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        _hint = new TextBlock { Foreground = SpaceTheme.SunBrush, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var footer = new Grid { Margin = new Thickness(14, 6, 14, 8) };
        footer.Children.Add(_status);
        footer.Children.Add(_hint);
        _hintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _hintTimer.Tick += (_, _) => { _hintTimer.Stop(); _hint.Text = ""; };

        var top = new Border { Background = SpaceTheme.VoidBrush, BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(0, 0, 0, 1), Child = _bar };
        var bottom = new Border { Background = SpaceTheme.VoidBrush, BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(top);
        dock.Children.Add(bottom);
        dock.Children.Add(_scroller);
        Content = dock;

        _doc.Changed += () => { _dirty = true; Title = TitleText(); UpdateStatus(); };
        KeyDown += OnKeyDown;
        SourceInitialized += (_, _) => ApplyDarkFrame();
        Loaded += (_, _) => OnFirstLayout();
        Closing += OnClosing;

        SizeToImage(pw, ph);
        SetTool(_tool);
    }

    /// <summary>Opens a saved capture, with its shapes if it was edited before.</summary>
    public static EditorWindow? OpenFile(CometApp app, string path)
    {
        try
        {
            var original = EditSidecar.OriginalFor(path);
            var annotations = EditSidecar.AnnotationsFor(path);
            BitmapSource image;
            AnnotationDocument doc;
            if (File.Exists(original) && File.Exists(annotations)
                && File.GetLastWriteTimeUtc(annotations) >= File.GetLastWriteTimeUtc(path).AddSeconds(-5))
            {
                image = LoadImage(original);
                doc = AnnotationDocument.FromJson(File.ReadAllText(annotations));
            }
            else
            {
                // Never edited, or changed by another program since: start from the file itself.
                image = LoadImage(path);
                doc = new AnnotationDocument();
            }
            return new EditorWindow(app, image, doc, path);
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open {path} in the editor", ex);
            MessageBox.Show($"Could not open this capture:\n{ex.Message}", "Cometsnap", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    private static BitmapSource LoadImage(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    // ------------------------------------------------------------------ layout & zoom

    private void SizeToImage(int pw, int ph)
    {
        var work = SystemParameters.WorkArea;
        // Rough DIP size at 100% zoom; the exact DPI is known once the window exists.
        var chromeW = 80.0;
        var chromeH = 170.0;
        Width = Math.Clamp(pw + chromeW, 640, work.Width * 0.9);
        Height = Math.Clamp(ph + chromeH, 420, work.Height * 0.9);
    }

    private void OnFirstLayout()
    {
        _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var availW = Math.Max(100, _scroller.ViewportWidth - 50) * _dpi;
        var availH = Math.Max(100, _scroller.ViewportHeight - 50) * _dpi;
        var fit = Math.Min(availW / _base.PixelWidth, availH / _base.PixelHeight);
        SetZoom(Math.Min(1, fit));
        Activate();
        Keyboard.Focus(this);
    }

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 0.05, 16);
        var scale = _zoom / _dpi;
        _zoomTransform.ScaleX = scale;
        _zoomTransform.ScaleY = scale;
        UpdateStatus();
    }

    private void FitZoom()
    {
        var availW = Math.Max(100, _scroller.ViewportWidth - 50) * _dpi;
        var availH = Math.Max(100, _scroller.ViewportHeight - 50) * _dpi;
        SetZoom(Math.Min(availW / _base.PixelWidth, availH / _base.PixelHeight));
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        e.Handled = true;
        SetZoom(_zoom * (e.Delta > 0 ? 1.25 : 0.8));
    }

    private void UpdateStatus()
    {
        var name = _path is null ? "New capture (not saved yet)" : Path.GetFileName(_path);
        _status.Text = $"{name}   ·   {_base.PixelWidth} × {_base.PixelHeight}   ·   {Math.Round(_zoom * 100)}%   ·   "
                     + "E circle  A arrow  T text  R box  ·  Ctrl+Z undo  ·  Ctrl+wheel zoom";
    }

    private string TitleText() =>
        $"{(_dirty ? "• " : "")}{(_path is null ? "New capture" : Path.GetFileName(_path))} - Cometsnap";

    private void Flash(string text)
    {
        _hint.Text = text;
        _hintTimer.Stop();
        _hintTimer.Start();
    }

    // ------------------------------------------------------------------ tools

    private double Dpi => _dpi <= 0 ? 1 : _dpi;
    private double ShapeThickness => Math.Max(2, Math.Round(3 * Dpi));
    private double TextSize => Math.Round(18 * Dpi);

    private void SetTool(AnnotationTool tool)
    {
        CommitText();
        // In the editor there's always a tool: clicking the active one keeps it.
        if (tool == AnnotationTool.None) tool = _tool;
        _tool = tool;
        _bar.SetActive(_tool, _color);
        _surface.Cursor = _tool == AnnotationTool.Text ? Cursors.IBeam : Cursors.Cross;
    }

    private void SetColor(string color)
    {
        _color = color;
        _bar.SetActive(_tool, _color);
        if (_textBox is not null) _textBox.Foreground = Annotation.CreateBrush(color);
    }

    private void Undo()
    {
        CommitText();
        _doc.Undo();
    }

    private void OnSurfaceDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var p = e.GetPosition(_surface);
        CommitText();
        if (_tool == AnnotationTool.Text)
        {
            StartText(p);
            return;
        }

        _drawing = _tool switch
        {
            AnnotationTool.Ellipse => new EllipseAnnotation(),
            AnnotationTool.Rectangle => new RectangleAnnotation(),
            _ => new ArrowAnnotation(),
        };
        _drawing.Color = _color;
        _drawing.Thickness = ShapeThickness;
        _drawing.X1 = _drawing.X2 = p.X;
        _drawing.Y1 = _drawing.Y2 = p.Y;
        _layer.InProgress = _drawing;
        _surface.CaptureMouse();
    }

    private void OnSurfaceMove(object sender, MouseEventArgs e)
    {
        if (_drawing is null) return;
        var p = e.GetPosition(_surface);
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (_drawing is ArrowAnnotation arrow) arrow.DragArrowTo(p.X, p.Y, shift);
        else _drawing.DragTo(p.X, p.Y, shift);
        _layer.InProgress = _drawing;
    }

    private void OnSurfaceUp(object sender, MouseButtonEventArgs e)
    {
        if (_drawing is null) return;
        e.Handled = true;
        _surface.ReleaseMouseCapture();
        var done = _drawing;
        _drawing = null;
        _layer.InProgress = null;
        if (!done.IsDegenerate) _doc.Add(done);
    }

    private void StartText(Point at)
    {
        RemoveTextBox();
        _textAt = at;
        _textBox = new TextBox
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            FontSize = TextSize,
            Foreground = Annotation.CreateBrush(_color),
            Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0x66),
            CaretBrush = SpaceTheme.StarWhiteBrush,
            BorderBrush = SpaceTheme.CometBlueBrush,
            BorderThickness = new Thickness(Math.Max(1, Math.Round(Dpi))),
            Padding = new Thickness(0),
            MinWidth = 40 * Dpi,
            AcceptsReturn = true,
        };
        _textBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape || (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0))
            {
                e.Handled = true;
                CommitText();
            }
        };
        var inset = _textBox.BorderThickness.Left + 2;
        Canvas.SetLeft(_textBox, at.X - inset);
        Canvas.SetTop(_textBox, at.Y - inset);
        _surface.Children.Add(_textBox);
        _textBox.Focus();
        Keyboard.Focus(_textBox);
    }

    private void CommitText()
    {
        if (_textBox is null) return;
        var text = _textBox.Text;
        RemoveTextBox();
        if (!string.IsNullOrWhiteSpace(text))
        {
            _doc.Add(new TextAnnotation { X = _textAt.X, Y = _textAt.Y, Text = text, FontSize = TextSize, Color = _color });
        }
        Keyboard.Focus(this);
    }

    private void RemoveTextBox()
    {
        if (_textBox is null) return;
        _surface.Children.Remove(_textBox);
        _textBox = null;
    }

    // ------------------------------------------------------------------ keyboard

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_textBox is not null && _textBox.IsKeyboardFocusWithin) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        e.Handled = true;
        switch (key)
        {
            case Key.Z when ctrl: Undo(); return;
            case Key.Y when ctrl: _doc.Redo(); return;
            case Key.C when ctrl: Copy(); return;
            case Key.S when ctrl && shift: SaveAs(); return;
            case Key.S when ctrl: Save(); return;
            case Key.Enter when ctrl:
                Copy();
                if (_dirty) Save();
                Close();
                return;
            case Key.D0 when ctrl: FitZoom(); return;
            case Key.D1 when ctrl: SetZoom(1); return;
            case Key.OemPlus or Key.Add when ctrl: SetZoom(_zoom * 1.25); return;
            case Key.OemMinus or Key.Subtract when ctrl: SetZoom(_zoom * 0.8); return;
            case Key.E: SetTool(AnnotationTool.Ellipse); return;
            case Key.A: SetTool(AnnotationTool.Arrow); return;
            case Key.T: SetTool(AnnotationTool.Text); return;
            case Key.R: SetTool(AnnotationTool.Rectangle); return;
            case >= Key.D1 and <= Key.D6 when !ctrl: SetColor(QuickBar.Palette[key - Key.D1]); return;
            case >= Key.NumPad1 and <= Key.NumPad6: SetColor(QuickBar.Palette[key - Key.NumPad1]); return;
            case Key.Escape: Close(); return;
        }
        e.Handled = false;
    }

    // ------------------------------------------------------------------ output

    private BitmapSource Flattened()
    {
        CommitText();
        return _doc.Flatten(_base, new Point(0, 0));
    }

    private void Copy()
    {
        Flash(ClipboardService.SetImage(Flattened()) ? "Copied to the clipboard" : "Could not copy, try again");
    }

    /// <summary>Saves over the capture (keeping the shapes editable), or as a new capture.</summary>
    private bool Save()
    {
        var image = Flattened();
        var isNew = _path is null;
        try
        {
            var path = _path ?? SnapNaming.NextFreePath(_app.CapturesFolder, DateTime.Now);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (_doc.IsEmpty)
            {
                EditSidecar.DeleteFor(path);
            }
            else
            {
                EditSidecar.EnsureFolder(path);
                CaptureStore.SavePng(_base, EditSidecar.OriginalFor(path));
            }
            CaptureStore.SavePng(image, path);
            if (!_doc.IsEmpty) File.WriteAllText(EditSidecar.AnnotationsFor(path), _doc.ToJson());

            _path = path;
            _dirty = false;
            Title = TitleText();
            UpdateStatus();
            _app.Thumbnails.Forget(path);
            _app.Thumbnails.Prime(path, image);
            _app.Recent.Add(path);
            Flash(isNew ? "Saved to your captures" : "Saved");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Editor save failed", ex);
            MessageBox.Show(this, $"Could not save the capture:\n{ex.Message}", "Cometsnap", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void SaveAs()
    {
        var image = Flattened();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save a copy",
            FileName = _path is null ? SnapNaming.BaseName(DateTime.Now) : Path.GetFileNameWithoutExtension(_path) + "-edited",
            DefaultExt = ".png",
            Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg",
            InitialDirectory = _path is null ? _app.CapturesFolder : Path.GetDirectoryName(_path),
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (dialog.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || dialog.FileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var fs = File.Create(dialog.FileName);
                encoder.Save(fs);
            }
            else
            {
                CaptureStore.SavePng(image, dialog.FileName);
            }
            Flash("Copy saved");
        }
        catch (Exception ex)
        {
            Log.Error("Editor save as failed", ex);
            MessageBox.Show(this, $"Could not save:\n{ex.Message}", "Cometsnap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        CommitText();
        if (!_dirty) return;
        var answer = MessageBox.Show(this,
            _path is null ? "Save this capture?" : "Save your changes to this capture?",
            "Cometsnap", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !Save()))
            e.Cancel = true;
    }

    private void ApplyDarkFrame()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var dark = 1;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }
}
