using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Cometsnap.Core.Updates;
using Cometsnap.Linux.Theme;

namespace Cometsnap.Linux.Tray;

/// <summary>
/// "Cometsnap 1.4.0 is here": a small card in the corner with what the new version brings,
/// Update now (or Download) / Later and Skip this version. Never takes the focus; stays until answered.
/// </summary>
public sealed class UpdateWindow : Window
{
    private readonly TextBlock _title;
    private readonly StackPanel _notes;
    private readonly TextBlock _status;
    private readonly TextBlock _primaryText;
    private readonly Border _primary;
    private readonly Border _later;
    private readonly TextBlock _skip;
    private readonly Border _card;
    private bool _busy;
    private PixelRect _work;
    private double _scale = 1;

    public event Action? UpdateNow;
    public event Action? Later;
    public event Action? Skip;
    public event Action? ShowEverything;

    public UpdateWindow()
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
        Foreground = SpaceTheme.StarWhiteBrush;

        _title = new TextBlock { FontFamily = SpaceTheme.HeadingFont, FontSize = 15, Foreground = SpaceTheme.SunBrush, TextWrapping = TextWrapping.Wrap, MaxWidth = 270 };
        var close = new TextBlock { Text = "✕", Foreground = SpaceTheme.DustBrush, FontSize = 12, Margin = new Thickness(10, 2, 0, 0), Cursor = new Cursor(StandardCursorType.Hand) };
        close.PointerPressed += (_, e) => { e.Handled = true; if (!_busy) Later?.Invoke(); };
        var head = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(_title);

        _notes = new StackPanel { Margin = new Thickness(0, 8, 0, 0), Spacing = 4 };
        var everything = Link(T("See everything new"), () => ShowEverything?.Invoke());
        everything.Margin = new Thickness(0, 6, 0, 0);

        _primaryText = new TextBlock { FontWeight = FontWeight.SemiBold, FontSize = 13, Foreground = SpaceTheme.StarWhiteBrush };
        _primary = Ui.Button(_primaryText, T("Update now"), () => { if (!_busy) UpdateNow?.Invoke(); },
            SpaceTheme.TerracottaBrush, new ImmutableSolidColorBrush(Color.FromRgb(0xF0, 0x6A, 0x4E)), new Thickness(14, 7));
        _later = Ui.ActionButton(T("Later"), null, T("Later"), () => { if (!_busy) Later?.Invoke(); });
        _skip = Link(T("Skip this version"), () => { if (!_busy) Skip?.Invoke(); });
        _skip.VerticalAlignment = VerticalAlignment.Center;
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        _primary.Margin = new Thickness(0, 0, 8, 0);
        _later.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(_primary);
        buttons.Children.Add(_later);
        buttons.Children.Add(_skip);

        _status = new TextBlock { Foreground = SpaceTheme.DustBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 300, Margin = new Thickness(0, 8, 0, 0), IsVisible = false };

        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(_notes);
        body.Children.Add(everything);
        body.Children.Add(buttons);
        body.Children.Add(_status);

        var row = new DockPanel();
        var logo = new Image { Source = SpaceTheme.Logo, Width = 32, Height = 32, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(logo, Dock.Left);
        row.Children.Add(logo);
        row.Children.Add(body);

        _card = new Border
        {
            Child = row, Width = 380,
            Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xF4),
            BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 14), Margin = new Thickness(12),
        };
        Content = _card;
    }

    public void ShowOffer(UpdateOffer offer, string primaryText, string? note, PixelRect workArea, double scaling)
    {
        _busy = false;
        _work = workArea;
        _scale = scaling;
        _title.Text = F("Cometsnap {0} is here", offer.Version).ToUpper(Culture);
        _notes.Children.Clear();
        var lines = offer.Notes.Count > 0 ? offer.Notes : new[] { T("Fixes and improvements.") };
        foreach (var line in lines)
        {
            var bullet = new DockPanel();
            var star = new TextBlock { Text = "✦", Foreground = CaptureLook.AccentBrush, FontSize = 11, Margin = new Thickness(0, 2, 8, 0) };
            DockPanel.SetDock(star, Dock.Left);
            bullet.Children.Add(star);
            bullet.Children.Add(new TextBlock { Text = line, FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxWidth = 290 });
            _notes.Children.Add(bullet);
        }
        _primaryText.Text = primaryText;
        SetBusy(false, note);
        if (!IsVisible) Show();
        Ui.Entrance(_card, lift: 12, ms: 180);
        Place();
        Dispatcher.UIThread.Post(Place, DispatcherPriority.Loaded);
    }

    public void SetBusy(bool busy, string? status)
    {
        _busy = busy;
        _primary.Opacity = _later.Opacity = busy ? 0.5 : 1;
        _skip.IsVisible = !busy;
        SetStatus(status);
    }

    public void SetStatus(string? text)
    {
        _status.Text = text ?? "";
        _status.IsVisible = !string.IsNullOrEmpty(text);
        if (IsVisible) Dispatcher.UIThread.Post(Place, DispatcherPriority.Loaded);
    }

    private void Place() => Position = new PixelPoint(
        _work.Right - (int)(Math.Max(Bounds.Width, 400) * _scale) - 4,
        _work.Bottom - (int)(Math.Max(Bounds.Height, 160) * _scale) - 4);

    private static TextBlock Link(string text, Action click)
    {
        var t = new TextBlock
        {
            Text = text, Foreground = SpaceTheme.DustBrush, FontSize = 12, Cursor = new Cursor(StandardCursorType.Hand),
            TextDecorations = TextDecorations.Underline, HorizontalAlignment = HorizontalAlignment.Left,
        };
        t.PointerEntered += (_, _) => t.Foreground = SpaceTheme.StarWhiteBrush;
        t.PointerExited += (_, _) => t.Foreground = SpaceTheme.DustBrush;
        t.PointerPressed += (_, e) => { e.Handled = true; click(); };
        return t;
    }
}
