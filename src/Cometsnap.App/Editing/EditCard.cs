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
    private readonly System.Windows.Shapes.Path _editIcon;
    private readonly TextBlock _editLabel;
    private readonly Border _editButton;
    private readonly Border _cardBorder;
    private readonly Border _textButton;
    private readonly Border _styleButton;
    private BitmapSource? _raw;           // the capture before any style, for the ✨ button
    private bool _canStyle;
    private string? _recording;           // set while the card shows a recording
    private string? _text;                // set while the card shows recognized text
    private readonly DispatcherTimer _timer;
    private BitmapSource? _image;
    private string? _path;
    private Point? _pressAt;
    private bool _dragged;

    /// <summary>Raised with the saved path (if known yet) and the image when the card or Edit is clicked.</summary>
    public event Action<string?, BitmapSource>? EditRequested;

    /// <summary>Raised with the recording's path when the card of a recording is clicked (opens the trimmer).</summary>
    public event Action<string>? TrimRequested;

    /// <summary>Raised with the capture when its Text button is clicked (read the text in it).</summary>
    public event Action<BitmapSource>? TextRequested;

    /// <summary>Raised with the saved path (if known) and the raw capture when ✨ Style is clicked.</summary>
    public event Action<string?, BitmapSource>? StyleRequested;

    /// <summary>Raised with the recognized text when the card of a text capture is clicked.</summary>
    public event Action<string>? ShowTextRequested;

    private const string PenIcon = "M10.5,2.5 L13.5,5.5 L5.5,13.5 H2.5 V10.5 Z M8.5,4.5 L11.5,7.5";
    private const string TextIcon = "M3,3.5 H13 M8,3.5 V13.5 M6,13.5 H10";
    private const string EyeIcon = "M1.5,8 C4,3.5 12,3.5 14.5,8 C12,12.5 4,12.5 1.5,8 Z M8,8 m-2,0 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0";
    private const string ScissorsIcon = "M5,4.5 m-2,0 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M5,11.5 m-2,0 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M6.6,5.6 L14,12 M6.6,10.4 L14,4";

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
        _editIcon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(PenIcon),
            Stroke = SpaceTheme.StarWhiteBrush, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round,
            Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
        };
        editRow.Children.Add(_editIcon);
        _editLabel = new TextBlock { Text = L.T("Edit"), Foreground = SpaceTheme.StarWhiteBrush, FontWeight = FontWeights.SemiBold, FontSize = 14 };
        editRow.Children.Add(_editLabel);
        edit.Child = editRow;
        _editButton = edit;
        edit.MouseLeftButtonUp += (_, e) => { e.Handled = true; _pressAt = null; if (!_dragged) OnEdit(); };

        // Copy text (OCR): reads the text in the capture and puts it on the clipboard.
        _textButton = new Border
        {
            Background = SpaceTheme.NavyBrush, CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 7, 9, 7),
            Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
            ToolTip = L.T("Copy the text in this capture"),
            Child = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(TextIcon), Stroke = SpaceTheme.StarWhiteBrush, StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = 16, Height = 16,
            },
        };
        _textButton.MouseEnter += (_, _) => _textButton.Background = CaptureLook.AccentBrush;
        _textButton.MouseLeave += (_, _) => _textButton.Background = SpaceTheme.NavyBrush;
        _textButton.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _pressAt = null;
            if (_dragged || _image is not { } image) return;
            _timer.Stop();
            TextRequested?.Invoke(image);
        };

        // ✨ Style: this one capture, share-ready (rounded corners, shadow, backdrop).
        _styleButton = new Border
        {
            Background = SpaceTheme.NavyBrush, CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 6, 11, 6),
            Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
            ToolTip = L.T("Style this capture: rounded corners, shadow, backdrop"),
            Child = new TextBlock { Text = "✨ " + L.T("Style"), Foreground = SpaceTheme.StarWhiteBrush, FontWeight = FontWeights.SemiBold, FontSize = 13 },
        };
        _styleButton.MouseEnter += (_, _) => _styleButton.Background = CaptureLook.AccentBrush;
        _styleButton.MouseLeave += (_, _) => _styleButton.Background = SpaceTheme.NavyBrush;
        _styleButton.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _pressAt = null;
            if (_dragged || _raw is not { } raw) return;
            _timer.Stop();
            _styleButton.Visibility = Visibility.Collapsed;
            _title.Text = L.T("STYLING…");
            StyleRequested?.Invoke(_path, raw);
        };

        var close = new TextBlock
        {
            Text = "✕", Foreground = SpaceTheme.DustBrush, FontSize = 12, Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top, Cursor = Cursors.Hand, ToolTip = L.T("Dismiss"),
        };
        close.MouseLeftButtonUp += (_, e) => { e.Handled = true; FadeOut(); };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(thumbFrame);
        row.Children.Add(text);
        row.Children.Add(_styleButton);
        row.Children.Add(_textButton);
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
        _cardBorder = card;
        card.MouseEnter += (_, _) =>
        {
            card.BorderBrush = CaptureLook.AccentBrush;
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
            if (e.LeftButton != MouseButtonState.Pressed || _pressAt is not { } start || (_image is null && _recording is null) || _text is not null) return;
            var d = e.GetPosition(this) - start;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _pressAt = null;
            _dragged = true;
            _timer.Stop();
            var effect = DragOut.Start(card, _recording ?? _path, _recording is null ? _image : null, _image);
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
    public void ShowFor(BitmapSource image, Rect workAreaDip, bool copied, BitmapSource? raw = null, bool canStyle = false)
    {
        _raw = raw ?? image;
        _canStyle = canStyle;
        SetMode(recording: null, text: null);
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

    /// <summary>
    /// Shows the card for a finished recording: its poster frame, length and size, and a Trim button
    /// that opens the trimmer. Dragging the card drops the video file into any app.
    /// </summary>
    public void ShowForRecording(string path, BitmapSource? poster, string detail, bool copied, Rect workAreaDip)
    {
        _canStyle = false;
        SetMode(path, text: null);
        _image = poster;
        _path = path;
        _thumb.Source = poster;
        _title.Text = copied ? L.T("COPIED!") : L.T("SAVED!");
        _detail.Text = detail;
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

    private void SetMode(string? recording, string? text)
    {
        _title.Foreground = CaptureLook.TagBrush;
        _recording = recording;
        _text = text;
        var video = recording is not null;
        var reading = text is not null;
        _textButton.Visibility = video || reading ? Visibility.Collapsed : Visibility.Visible;
        _styleButton.Visibility = video || reading || !_canStyle ? Visibility.Collapsed : Visibility.Visible;
        _editButton.Visibility = reading && text!.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _editIcon.Data = Geometry.Parse(video ? ScissorsIcon : reading ? EyeIcon : PenIcon);
        _editLabel.Text = video ? L.T("Trim") : reading ? L.T("Show") : L.T("Edit");
        _editButton.ToolTip = video ? L.T("Trim the start and end, or cut parts out")
            : reading ? L.T("See the text and copy just a part") : L.T("Open in the editor");
        _cardBorder.ToolTip = video ? L.T("Click to trim · Drag into any app")
            : reading ? L.T("Click to see the text") : L.T("Click to edit · Drag into any app");
    }

    /// <summary>The text of a capture is being read: shows the card with a "Reading" title until it is done.</summary>
    public void ShowReading(BitmapSource image, Rect workAreaDip)
    {
        SetMode(recording: null, text: "");
        _image = image;
        _path = null;
        _thumb.Source = image;
        _title.Text = L.T("READING…");
        _detail.Text = L.T("Looking for text");
        Present(workAreaDip, fadeLater: false);
    }

    /// <summary>Shows what text recognition found (already on the clipboard).</summary>
    public void ShowText(BitmapSource image, string text, int lines, Rect workAreaDip)
    {
        SetMode(recording: null, text: text);
        _image = image;
        _path = null;
        _thumb.Source = image;
        _title.Text = text.Length > 0 ? L.T("TEXT COPIED!") : L.T("NO TEXT FOUND");
        _detail.Text = text.Length == 0 ? L.T("Try a bigger area")
            : lines == 1 ? FirstLine(text) : L.F("{0} lines · {1}", lines, FirstLine(text));
        Present(workAreaDip, fadeLater: true);
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";

    private void Present(Rect workAreaDip, bool fadeLater)
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        var wasVisible = IsVisible;
        if (!wasVisible) Show();
        if (!wasVisible && Content is FrameworkElement card) EditorUi.Entrance(card, lift: 14, ms: 180);
        UpdateLayout();
        Left = workAreaDip.Right - ActualWidth - 4;
        Top = workAreaDip.Bottom - ActualHeight - 4;
        _timer.Stop();
        if (fadeLater) _timer.Start();
    }

    /// <summary>
    /// The ✨ Style button finished: the card now holds the styled capture (already on the
    /// clipboard), and Edit or a drag use the styled file when there is one.
    /// </summary>
    public void ShowStyled(BitmapSource raw, BitmapSource styled, string? path, bool copied)
    {
        if (!ReferenceEquals(raw, _raw) || _recording is not null || _text is not null || !IsVisible) return;
        _image = styled;
        _path = path;
        _thumb.Source = styled;
        _title.Text = copied ? L.T("STYLED & COPIED!") : L.T("STYLED!");
        _detail.Text = $"{styled.PixelWidth} × {styled.PixelHeight}" + (path is null ? "" : " · " + Path.GetFileName(path));
        _timer.Stop();
        if (!IsMouseOver) _timer.Start();
    }

    /// <summary>The capture reached the disk: Edit will now open the file.</summary>
    public void SetSaved(string path, BitmapSource image)
    {
        if (_recording is not null || _text is not null || !ReferenceEquals(image, _image) || !IsVisible) return;
        _path = path;
        _detail.Text = $"{image.PixelWidth} × {image.PixelHeight} · {Path.GetFileName(path)}";
    }

    private void OnEdit()
    {
        _timer.Stop();
        Hide();
        if (_recording is { } recording) TrimRequested?.Invoke(recording);
        else if (_text is { Length: > 0 } text) ShowTextRequested?.Invoke(text);
        else if (_text is not null) return;
        else if (_image is { } image) EditRequested?.Invoke(_path, image);
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
