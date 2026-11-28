using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Cometsnap.Linux.Services;
using Cometsnap.Linux.Theme;

namespace Cometsnap.Linux.Editing;

/// <summary>
/// The small card that appears in the corner after a capture: "Copied", with an Edit button.
/// It never takes the focus, so you can keep typing or paste right away, and it fades out on its own.
/// </summary>
public sealed class EditCard : Window
{
    private readonly Image _thumb;
    private readonly TextBlock _title;
    private readonly TextBlock _detail;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(4.5) };
    private readonly Border _card;
    private readonly Border _editButton;
    private readonly Border _trimButton;
    private readonly Border _styleButton;
    private Bitmap? _raw;                 // the capture before any style, for the ✨ button
    private bool _canStyle;
    private Bitmap? _image;
    private string? _path;
    private string? _recording;           // set while the card shows a recording
    private Point? _pressAt;
    private bool _dragged;

    /// <summary>Raised with the saved path (if known yet) and the image when Edit is clicked.</summary>
    public event Action<string?, Bitmap>? EditRequested;

    /// <summary>Raised with the recording's path when the card of a recording is clicked (opens the trimmer).</summary>
    public event Action<string>? TrimRequested;

    /// <summary>Raised with the saved path (if known) and the raw capture when ✨ Style is clicked.</summary>
    public event Action<string?, Bitmap>? StyleRequested;

    private const string PenIcon = "M10.5,2.5 L13.5,5.5 L5.5,13.5 H2.5 V10.5 Z M8.5,4.5 L11.5,7.5";
    public const string ScissorsIcon = "M5,4.5 m-2,0 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M5,11.5 m-2,0 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M6.6,5.6 L14,12 M6.6,10.4 L14,4";

    public EditCard()
    {
        Title = "Cometsnap";
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None };
        Background = Brushes.Transparent;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = SpaceTheme.UiFont;

        _thumb = new Image { Width = 88, Height = 56, Stretch = Stretch.UniformToFill };
        var thumbFrame = new Border
        {
            CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = _thumb,
            BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
        };
        _title = new TextBlock { Text = T("COPIED!"), FontFamily = SpaceTheme.HeadingFont, FontSize = 16, Foreground = SpaceTheme.SunBrush };
        _detail = new TextBlock
        {
            Foreground = SpaceTheme.DustBrush, FontSize = 12, Margin = new Thickness(0, 2, 0, 0), MaxWidth = 170,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
        text.Children.Add(_title);
        text.Children.Add(_detail);

        var edit = Ui.ActionButton(T("Edit"), PenIcon, T("Open in the editor"), OnEdit, primary: true);
        edit.VerticalAlignment = VerticalAlignment.Center;
        _editButton = edit;
        _trimButton = Ui.ActionButton(T("Trim"), ScissorsIcon, T("Trim the start and end, or cut parts out"), OnEdit, primary: true);
        _trimButton.VerticalAlignment = VerticalAlignment.Center;
        _trimButton.IsVisible = false;
        // ✨ Style: this one capture, share-ready (rounded corners, shadow, backdrop).
        _styleButton = Ui.ActionButton("✨ " + T("Style"), null, T("Style this capture: rounded corners, shadow, backdrop"), () =>
        {
            if (_raw is not { } raw) return;
            _timer.Stop();
            _styleButton!.IsVisible = false;
            _title.Text = T("STYLING…");
            StyleRequested?.Invoke(_path, raw);
        });
        _styleButton.VerticalAlignment = VerticalAlignment.Center;
        _styleButton.Margin = new Thickness(0, 0, 6, 0);
        _styleButton.IsVisible = false;
        var close = new TextBlock
        {
            Text = "✕", Foreground = SpaceTheme.DustBrush, FontSize = 12, Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top, Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(close, T("Dismiss"));
        close.PointerReleased += (_, e) => { e.Handled = true; FadeOut(); };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(thumbFrame);
        row.Children.Add(text);
        row.Children.Add(_styleButton);
        row.Children.Add(edit);
        row.Children.Add(_trimButton);
        row.Children.Add(close);
        _card = new Border
        {
            Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xF2), BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(10), Margin = new Thickness(12),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 18, Color = Color.FromArgb(0x80, 0, 0, 0) }),
            Child = row,
        };
        Content = _card;

        // The whole card opens the editor (faster than aiming for the button); Edit and ✕ still work.
        _card.Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(_card, T("Click to edit · Drag into any app"));
        _card.PointerEntered += (_, _) => _card.BorderBrush = CaptureLook.AccentBrush;
        _card.PointerExited += (_, _) => _card.BorderBrush = SpaceTheme.NavyLightBrush;
        // Click = edit; drag = drop the capture into any app (file manager, chat, mail, a browser…).
        _card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(_card).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            _pressAt = e.GetPosition(this);
            _dragged = false;
        };
        _card.PointerMoved += async (_, e) =>
        {
            if (_pressAt is not { } start || (_image is null && _recording is null) || DragOut.Active) return;
            if (!e.GetCurrentPoint(_card).Properties.IsLeftButtonPressed) { _pressAt = null; return; }
            var d = e.GetPosition(this) - start;
            if (Math.Abs(d.X) < 5 && Math.Abs(d.Y) < 5) return;
            _pressAt = null;
            _dragged = true;
            _timer.Stop();
            var dropped = await DragOut.StartAsync(_recording ?? _path, _recording is null ? _image : null, _image);
            if (dropped) FadeOut();
            else if (!IsPointerOver) _timer.Start();
        };
        _card.PointerReleased += (_, e) =>
        {
            _pressAt = null;
            if (_dragged) { _dragged = false; e.Handled = true; return; }
            if (e.Handled || e.InitialPressMouseButton != MouseButton.Left || !_card.IsPointerOver) return;
            e.Handled = true;
            OnEdit();
        };
        // Without a compositor there is no transparency: blend the margin with the card instead of black.
        Opened += (_, _) =>
        {
            if (ActualTransparencyLevel == WindowTransparencyLevel.None)
            {
                Background = _card.Background;
                _card.Margin = new Thickness(0);
            }
        };

        _timer.Tick += (_, _) => FadeOut();
        PointerEntered += (_, _) => { _timer.Stop(); Opacity = 1; };
        PointerExited += (_, _) => _timer.Start();
    }

    /// <summary>Shows (or refreshes) the card for a new capture, bottom-right of the work area (desktop pixels).</summary>
    public void ShowFor(Bitmap image, PixelRect workArea, double scaling, bool copied, string? title = null, Bitmap? raw = null, bool canStyle = false)
    {
        _raw = raw;
        _canStyle = canStyle && raw is not null;
        SetMode(recording: null);
        _image = image;
        _path = null;
        _thumb.Source = image;
        _title.Foreground = CaptureLook.TagBrush;
        _title.Text = title ?? (copied ? T("COPIED!") : T("SNAPPED!"));
        _detail.Text = $"{image.PixelSize.Width} × {image.PixelSize.Height} · {T("saving…")}";
        Place(workArea, scaling);
    }

    /// <summary>
    /// Shows the card for a finished recording: its poster frame, length and size, and a Trim button
    /// that opens the trimmer. Dragging the card drops the video file into any app.
    /// </summary>
    public void ShowForRecording(string path, Bitmap? poster, string detail, bool copied, PixelRect workArea, double scaling)
    {
        _canStyle = false;
        SetMode(path);
        _image = poster;
        _path = path;
        _thumb.Source = poster;
        _title.Foreground = CaptureLook.TagBrush;
        _title.Text = copied ? T("COPIED!") : T("SAVED!");
        _detail.Text = detail;
        Place(workArea, scaling);
    }

    private void SetMode(string? recording)
    {
        _recording = recording;
        var video = recording is not null;
        _editButton.IsVisible = !video;
        _trimButton.IsVisible = video;
        _styleButton.IsVisible = !video && _canStyle;
        ToolTip.SetTip(_card, video ? T("Click to trim · Drag into any app") : T("Click to edit · Drag into any app"));
    }

    private void Place(PixelRect workArea, double scaling)
    {
        Opacity = 1;
        if (!IsVisible) Show();
        Ui.Entrance(_card, lift: 14, ms: 180);
        var size = _card.DesiredSize.Width > 0 ? _card.DesiredSize + new Size(24, 24) : new Size(420, 100);
        Position = new PixelPoint(
            workArea.Right - (int)(size.Width * scaling) - 4,
            workArea.Bottom - (int)(size.Height * scaling) - 4);
        Dispatcher.UIThread.Post(() =>
        {
            Position = new PixelPoint(
                workArea.Right - (int)(Bounds.Width * scaling) - 4,
                workArea.Bottom - (int)(Bounds.Height * scaling) - 4);
        }, DispatcherPriority.Loaded);
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>The capture reached the disk: Edit will now open the file.</summary>
    public void SetSaved(string path, Bitmap image)
    {
        if (_recording is not null || !ReferenceEquals(image, _image) || !IsVisible) return;
        _path = path;
        _detail.Text = $"{image.PixelSize.Width} × {image.PixelSize.Height} · {System.IO.Path.GetFileName(path)}";
    }

    public void SetDetail(string text) => _detail.Text = text;

    /// <summary>The ✨ Style button finished: the card now holds the styled capture (already copied).</summary>
    public void ShowStyled(Bitmap raw, Bitmap styled, string? path, bool copied)
    {
        if (!ReferenceEquals(raw, _raw) || _recording is not null || !IsVisible) return;
        _image = styled;
        _path = path;
        _thumb.Source = styled;
        _title.Text = copied ? T("STYLED & COPIED!") : T("STYLED!");
        _detail.Text = $"{styled.PixelSize.Width} × {styled.PixelSize.Height}" + (path is null ? "" : " · " + System.IO.Path.GetFileName(path));
        _timer.Stop();
        if (!IsPointerOver) _timer.Start();
    }

    private void OnEdit()
    {
        _timer.Stop();
        _card.BorderBrush = SpaceTheme.NavyLightBrush;
        Hide();
        if (_recording is { } recording) TrimRequested?.Invoke(recording);
        else if (_image is not null) EditRequested?.Invoke(_path, _image);
    }

    private async void FadeOut()
    {
        _timer.Stop();
        for (var i = 10; i >= 0; i--)
        {
            Opacity = i / 10.0;
            await Task.Delay(18);
            if (IsPointerOver) { Opacity = 1; return; }
        }
        Hide();
        Opacity = 1;
    }
}
