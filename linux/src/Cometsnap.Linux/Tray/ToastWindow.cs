using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Cometsnap.Linux.Theme;

namespace Cometsnap.Linux.Tray;

/// <summary>A short message in the corner ("Copied", "Making the GIF…"). Never takes the focus.</summary>
public sealed class ToastWindow : Window
{
    private readonly TextBlock _text;
    private readonly Border _card;
    private IDisposable? _timer;

    public ToastWindow()
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
        _text = new TextBlock { Foreground = SpaceTheme.StarWhiteBrush, FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxWidth = 380, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(new Image { Source = SpaceTheme.Logo, Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(_text);
        _card = new Border
        {
            Child = row,
            Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xF2),
            BorderBrush = SpaceTheme.NavyLightBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10),
            Margin = new Thickness(12),
        };
        Content = _card;
        PointerPressed += (_, _) => Hide();
    }

    public void ShowMessage(string text, int ms, PixelRect workArea, double scaling)
    {
        _text.Text = text;
        if (!IsVisible) Show();
        Ui.Entrance(_card, lift: 10, ms: 160);
        void Place() => Position = new PixelPoint(
            workArea.Right - (int)(Math.Max(Bounds.Width, 200) * scaling) - 4,
            workArea.Bottom - (int)(Math.Max(Bounds.Height, 60) * scaling) - 4);
        Place();
        Dispatcher.UIThread.Post(Place, DispatcherPriority.Loaded);
        _timer?.Dispose();
        _timer = DispatcherTimer.RunOnce(Hide, TimeSpan.FromMilliseconds(ms));
    }
}
