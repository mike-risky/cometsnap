using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cometsnap.App.Editing;
using Cometsnap.App.Theme;
using Cometsnap.Core.Localization;
using Cometsnap.Core.Updates;

namespace Cometsnap.App.Updates;

/// <summary>
/// "Cometsnap 1.4.0 is here": a small card in the corner with what the new version brings,
/// Update now / Later and Skip this version. It never takes the focus and stays until answered.
/// </summary>
public sealed class UpdateCard : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private readonly TextBlock _title;
    private readonly StackPanel _notes;
    private readonly TextBlock _status;
    private readonly TextBlock _primaryLabel;
    private readonly Border _primary;
    private readonly Border _later;
    private readonly TextBlock _skip;
    private bool _busy;

    public event Action? UpdateNow;
    public event Action? Later;
    public event Action? Skip;
    public event Action? ShowEverything;

    public UpdateCard()
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
        Foreground = SpaceTheme.StarWhiteBrush;
        Title = "Cometsnap";

        var logo = new Image { Width = 34, Height = 34, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        try { logo.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/cometsnap-256.png")); }
        catch { logo.Visibility = Visibility.Collapsed; }

        _title = new TextBlock { FontFamily = SpaceTheme.HeadingFont, FontSize = 16, Foreground = SpaceTheme.SunBrush, TextWrapping = TextWrapping.Wrap, MaxWidth = 280 };
        var close = new TextBlock
        {
            Text = "✕", Foreground = SpaceTheme.DustBrush, FontSize = 12, Margin = new Thickness(10, 2, 0, 0),
            VerticalAlignment = VerticalAlignment.Top, Cursor = Cursors.Hand, ToolTip = L.T("Later"),
        };
        close.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (!_busy) Later?.Invoke(); };
        var head = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(_title);

        _notes = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var everything = Link(L.T("See everything new"), () => ShowEverything?.Invoke());
        everything.Margin = new Thickness(0, 6, 0, 0);
        everything.HorizontalAlignment = HorizontalAlignment.Left;

        _primaryLabel = new TextBlock { Text = L.T("Update now"), FontWeight = FontWeights.SemiBold, FontSize = 13 };
        _primary = Button(_primaryLabel, primary: true, () => { if (!_busy) UpdateNow?.Invoke(); });
        _later = Button(new TextBlock { Text = L.T("Later"), FontWeight = FontWeights.SemiBold, FontSize = 13 }, primary: false, () => { if (!_busy) Later?.Invoke(); });
        _skip = Link(L.T("Skip this version"), () => { if (!_busy) Skip?.Invoke(); });
        _skip.Margin = new Thickness(6, 0, 0, 0);
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(_primary);
        buttons.Children.Add(_later);
        buttons.Children.Add(_skip);

        _status = new TextBlock { Foreground = SpaceTheme.DustBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 300, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };

        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(_notes);
        body.Children.Add(everything);
        body.Children.Add(buttons);
        body.Children.Add(_status);

        var row = new DockPanel();
        DockPanel.SetDock(logo, Dock.Left);
        row.Children.Add(logo);
        row.Children.Add(body);

        Content = new Border
        {
            Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xF4), BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 14), Margin = new Thickness(12), Width = 384,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.5, Color = Colors.Black },
            Child = row,
        };

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
    }

    /// <summary>Shows the offer bottom-right of the main screen. <paramref name="primaryText"/>: "Update now" or "Download".</summary>
    public void ShowOffer(UpdateOffer offer, string primaryText, string? note)
    {
        _busy = false;
        _title.Text = L.F("Cometsnap {0} is here", offer.Version).ToUpper(L.Culture);
        _notes.Children.Clear();
        var lines = offer.Notes.Count > 0 ? offer.Notes : new[] { L.T("Fixes and improvements.") };
        foreach (var line in lines)
        {
            var bullet = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var star = new TextBlock { Text = "✦", Foreground = CaptureLook.AccentBrush, FontSize = 11, Margin = new Thickness(0, 2, 8, 0) };
            DockPanel.SetDock(star, Dock.Left);
            bullet.Children.Add(star);
            bullet.Children.Add(new TextBlock { Text = line, FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxWidth = 290 });
            _notes.Children.Add(bullet);
        }
        _primaryLabel.Text = primaryText;
        _primary.Visibility = _later.Visibility = _skip.Visibility = Visibility.Visible;
        SetStatus(note);

        if (!IsVisible) Show();
        if (Content is FrameworkElement card) EditorUi.Entrance(card, lift: 14, ms: 200);
        UpdateLayout();
        var work = SystemParameters.WorkArea;
        Left = work.Right - ActualWidth - 4;
        Top = work.Bottom - ActualHeight - 4;
    }

    /// <summary>Download progress, or an error. While busy, the buttons wait.</summary>
    public void SetBusy(bool busy, string? status)
    {
        _busy = busy;
        _primary.Opacity = _later.Opacity = busy ? 0.5 : 1;
        _skip.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        SetStatus(status);
    }

    public void SetStatus(string? text)
    {
        _status.Text = text ?? "";
        _status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        if (!IsVisible) return;
        UpdateLayout();
        var work = SystemParameters.WorkArea;
        Top = work.Bottom - ActualHeight - 4;
    }

    private static Border Button(TextBlock label, bool primary, Action click)
    {
        var normal = primary ? SpaceTheme.TerracottaBrush : SpaceTheme.NavyBrush;
        var hover = primary ? SpaceTheme.WithAlpha(SpaceTheme.Terracotta, 0xD0) : SpaceTheme.NavyLightBrush;
        var b = new Border
        {
            Background = normal, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0),
            BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(primary ? 0 : 1), Cursor = Cursors.Hand, Child = label,
            VerticalAlignment = VerticalAlignment.Center,
        };
        b.MouseEnter += (_, _) => b.Background = hover;
        b.MouseLeave += (_, _) => b.Background = normal;
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    private static TextBlock Link(string text, Action click)
    {
        var t = new TextBlock { Text = text, Foreground = SpaceTheme.DustBrush, FontSize = 12, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, TextDecorations = TextDecorations.Underline };
        t.MouseEnter += (_, _) => t.Foreground = SpaceTheme.StarWhiteBrush;
        t.MouseLeave += (_, _) => t.Foreground = SpaceTheme.DustBrush;
        t.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return t;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
