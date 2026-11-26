using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Cometsnap.App.Services;
using Cometsnap.App.Theme;
using Cometsnap.Core.Localization;

namespace Cometsnap.App.Editing;

/// <summary>
/// The small card that appears in the corner after a capture: "Copied", with an Edit button.
/// It never takes focus, so you can keep typing or paste right away, and it fades out on its own.
/// </summary>
public sealed class EditCard : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private readonly Image _thumb;
    private readonly TextBlock _title;
    private readonly TextBlock _detail;
    private readonly DispatcherTimer _timer;
    private BitmapSource? _image;
    private string? _path;
    private Point? _pressAt;
    private bool _dragged;

    /// <summary>Raised with the saved path (if known yet) and the image when the card or Edit is clicked.</summary>
    public event Action<string?, BitmapSource>? EditRequested;

    public EditCard()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = SpaceTheme.UiFont;

        _thumb = new Image { Width = 88, Height = 56, Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(_thumb, BitmapScalingMode.HighQuality);
        var thumbFrame = new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = _thumb, BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1) };

        _title = new TextBlock { Text = L.T("COPIED!"), FontFamily = SpaceTheme.HeadingFont, FontSize = 16, Foreground = SpaceTheme.SunBrush };
        _detail = new TextBlock { Foreground = SpaceTheme.DustBrush, FontSize = 12, Margin = new Thickness(0, 2, 0, 0), MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
        text.Children.Add(_title);
        text.Children.Add(_detail);

        var edit = new Border
        {
            Background = SpaceTheme.TerracottaBrush, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 14, 7),
            VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, ToolTip = L.T("Open in the editor"),
        };
        var editRow = new StackPanel { Orientation = Orientation.Horizontal };
        editRow.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M10.5,2.5 L13.5,5.5 L5.5,13.5 H2.5 V10.5 Z M8.5,4.5 L11.5,7.5"),
            Stroke = SpaceTheme.StarWhiteBrush, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round,
            Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
        });
        editRow.Children.Add(new TextBlock { Text = L.T("Edit"), Foreground = SpaceTheme.StarWhiteBrush, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        edit.Child = editRow;
        edit.MouseLeftButtonUp += (_, e) => { e.Handled = true; _pressAt = null; if (!_dragged) OnEdit(); };

        var close = new TextBlock
        {
            Text = "✕", Foreground = SpaceTheme.DustBrush, FontSize = 12, Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top, Cursor = Cursors.Hand, ToolTip = L.T("Dismiss"),
        };
        close.MouseLeftButtonUp += (_, e) => { e.Handled = true; FadeOut(); };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(thumbFrame);
        row.Children.Add(text);
        row.Children.Add(edit);
        row.Children.Add(close);

        // The whole card opens the editor: one big target, faster than aiming for the button.
        var card = new Border
        {
            Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xF2), BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(10), Margin = new Thickness(12),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.5, Color = Colors.Black },
            Child = row, Cursor = Cursors.Hand, ToolTip = L.T("Click to edit · Drag into any app"),
        };
        card.MouseEnter += (_, _) =>
        {
            card.BorderBrush = SpaceTheme.CometBlueBrush;
            card.Background = SpaceTheme.WithAlpha(SpaceTheme.Navy, 0xF6);
        };
        card.MouseLeave += (_, _) =>
        {
            card.BorderBrush = SpaceTheme.NavyLightBrush;
            card.Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xF2);
        };
        // Click = edit; drag = drop the capture into any app (Teams, Word, mail, a browser…).
        card.PreviewMouseLeftButtonDown += (_, e) => { _pressAt = e.GetPosition(this); _dragged = false; };
        card.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _pressAt is not { } start || _image is null) return;
            var d = e.GetPosition(this) - start;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _pressAt = null;
            _dragged = true;
            _timer.Stop();
            var effect = DragOut.Start(card, _path, _image, _image);
            if (effect != DragDropEffects.None) FadeOut();
            else _timer.Start();
        };
        card.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _pressAt = null;
            if (!_dragged) OnEdit();
        };
        Content = card;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
        _timer.Tick += (_, _) => FadeOut();
        MouseEnter += (_, _) => { _timer.Stop(); BeginAnimation(OpacityProperty, null); Opacity = 1; };
        MouseLeave += (_, _) => _timer.Start();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
    }

    /// <summary>Shows (or refreshes) the card for a new capture, bottom-right of the work area.</summary>
    public void ShowFor(BitmapSource image, Rect workAreaDip, bool copied)
    {
        _image = image;
        _path = null;
        _thumb.Source = image;
        _title.Text = copied ? L.T("COPIED!") : L.T("SNAPPED!");
        _detail.Text = $"{image.PixelWidth} × {image.PixelHeight} · " + L.T("saving…");

        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        if (!IsVisible) Show();
        if (Content is FrameworkElement card) EditorUi.Entrance(card, lift: 14, ms: 180);
        UpdateLayout();
        Left = workAreaDip.Right - ActualWidth - 4;
        Top = workAreaDip.Bottom - ActualHeight - 4;
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>The capture reached the disk: Edit will now open the file.</summary>
    public void SetSaved(string path, BitmapSource image)
    {
        if (!ReferenceEquals(image, _image) || !IsVisible) return;
        _path = path;
        _detail.Text = $"{image.PixelWidth} × {image.PixelHeight} · {Path.GetFileName(path)}";
    }

    private void OnEdit()
    {
        _timer.Stop();
        Hide();
        if (_image is { } image) EditRequested?.Invoke(_path, image);
    }

    private void FadeOut()
    {
        _timer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => { if (Opacity <= 0.01) Hide(); };
        BeginAnimation(OpacityProperty, fade);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
