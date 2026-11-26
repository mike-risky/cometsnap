using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Cometsnap.Linux.Theme;
using PixelRect = Cometsnap.Linux.Core.PixelRect;

namespace Cometsnap.Linux.Recording;

/// <summary>
/// The only control shown while recording: a small pill with a breathing red dot, the elapsed time
/// and a stop button, just outside the recorded area. A thin red frame shows what is being recorded.
/// </summary>
internal sealed class RecordingPill : Window
{
    private readonly TextBlock _time;
    private readonly TextBlock _kind;
    private readonly Ellipse _dot;
    private readonly DispatcherTimer _breath;
    private readonly List<Window> _frame = new();
    private bool _bright = true;

    public event Action? StopClicked;

    public RecordingPill()
    {
        Title = "Cometsnap recording";
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None };
        Background = Brushes.Transparent;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = SpaceTheme.UiFont;

        _dot = new Ellipse { Width = 10, Height = 10, Fill = SpaceTheme.RecordRedBrush, Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        _time = new TextBlock { Text = "0:00", Foreground = SpaceTheme.StarWhiteBrush, FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 36 };
        _kind = new TextBlock { Foreground = SpaceTheme.DustBrush, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 1, 6, 0) };
        var stop = Ui.Button(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = SpaceTheme.StarWhiteBrush },
            "Stop recording (same shortcut again)", () => StopClicked?.Invoke(), SpaceTheme.RecordRedBrush,
            new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0x60, 0x66)), new Thickness(9), 14);
        stop.Margin = new Thickness(0, 0, 4, 0);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_dot);
        row.Children.Add(_time);
        row.Children.Add(_kind);
        row.Children.Add(stop);
        Content = new Border
        {
            Child = row,
            Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xF0),
            BorderBrush = SpaceTheme.WithAlpha(SpaceTheme.RecordRed, 0xC0),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(17),
            Height = 34,
            Cursor = new Cursor(StandardCursorType.Arrow),
        };
        _breath = new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Background, (_, _) =>
        {
            _bright = !_bright;
            _dot.Opacity = _bright ? 1 : 0.35;
        });
    }

    /// <summary>Shows the pill below the area (above it, or inside its bottom when there is no room), and the frame.</summary>
    public void ShowFor(PixelRect region, PixelRect monitor, string kind, double scaling)
    {
        _kind.Text = kind;
        _time.Text = "0:00";
        Show();
        var w = (int)(170 * scaling);
        var h = (int)(34 * scaling);
        var gap = (int)(8 * scaling);
        var x = Math.Clamp(region.Right - w, monitor.X + gap, monitor.Right - w - gap);
        var y = region.Bottom + gap;
        if (y + h > monitor.Bottom) y = region.Y - h - gap;
        if (y < monitor.Y) y = region.Bottom - h - gap;
        Position = new PixelPoint(x, y);
        _breath.Start();
        ShowFrame(region, scaling);
    }

    public void SetElapsed(TimeSpan t) => _time.Text = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    public void HideNow()
    {
        _breath.Stop();
        foreach (var f in _frame) f.Close();
        _frame.Clear();
        Hide();
    }

    /// <summary>Four thin red windows just outside the recorded area (outside, so they are not in the video).</summary>
    private void ShowFrame(PixelRect r, double scaling)
    {
        foreach (var f in _frame) f.Close();
        _frame.Clear();
        const int t = 2;
        var edges = new[]
        {
            new PixelRect(r.X - t, r.Y - t, r.Width + 2 * t, t),
            new PixelRect(r.X - t, r.Bottom, r.Width + 2 * t, t),
            new PixelRect(r.X - t, r.Y, t, r.Height),
            new PixelRect(r.Right, r.Y, t, r.Height),
        };
        foreach (var e in edges)
        {
            var win = new Window
            {
                SystemDecorations = SystemDecorations.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                CanResize = false,
                Background = SpaceTheme.RecordRedBrush,
                Width = Math.Max(1, e.Width / scaling),
                Height = Math.Max(1, e.Height / scaling),
                Position = new PixelPoint(e.X, e.Y),
                IsHitTestVisible = false,
            };
            win.Show();
            win.Position = new PixelPoint(e.X, e.Y);
            _frame.Add(win);
        }
    }
}
