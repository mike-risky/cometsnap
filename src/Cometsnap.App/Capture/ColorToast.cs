using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Cometsnap.App.Editing;
using Cometsnap.App.Services;
using Cometsnap.App.Theme;
using Cometsnap.Core.Localization;

namespace Cometsnap.App.Capture;

/// <summary>
/// "Copied #FFAA00" with a swatch, bottom-right of the screen you are on, after the color picker. Click-through, never
/// takes focus, fades out by itself.
/// </summary>
internal sealed class ColorToast : Window
{
    private readonly Border _swatch;
    private readonly TextBlock _title;
    private readonly TextBlock _value;
    private readonly Border _card;
    private readonly DispatcherTimer _timer;
    private readonly CornerPlacement _corner;

    public ColorToast()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = SpaceTheme.UiFont;
        Left = Top = -32000;

        _swatch = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(8),
            BorderBrush = SpaceTheme.WithAlpha(SpaceTheme.StarWhite, 0x80), BorderThickness = new Thickness(1.5),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _title = new TextBlock { FontFamily = SpaceTheme.HeadingFont, FontSize = 14 };
        _value = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"), FontSize = 15, FontWeight = FontWeights.SemiBold,
            Foreground = SpaceTheme.StarWhiteBrush, Margin = new Thickness(0, 2, 0, 0),
        };
        var text = new StackPanel { Margin = new Thickness(12, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_title);
        text.Children.Add(_value);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_swatch);
        row.Children.Add(text);

        _card = new Border
        {
            Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xF2), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(10), Margin = new Thickness(12),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.5, Color = Colors.Black },
            Child = row,
        };
        Content = _card;
        _corner = new CornerPlacement(this);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        _timer.Tick += (_, _) => FadeOut();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
        };
    }

    public void ShowFor(Color color, string text)
    {
        _swatch.Background = new SolidColorBrush(color);
        _card.BorderBrush = CaptureLook.AccentBrush;
        _title.Foreground = CaptureLook.TagBrush;
        _title.Text = L.T("COPIED!");
        _value.Text = text;

        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        if (!IsVisible) Show();
        EditorUi.Entrance(_card, lift: 14, ms: 180);
        _corner.PlaceNearCursor();
        _timer.Stop();
        _timer.Start();
    }

    private void FadeOut()
    {
        _timer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => { if (Opacity < 0.05) Hide(); };
        BeginAnimation(OpacityProperty, fade);
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
