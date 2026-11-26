using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Cometsnap.App.Theme;
using Cometsnap.Core.Geometry;
using Cometsnap.Core.Native;

namespace Cometsnap.App.Recording;

/// <summary>
/// The only control shown while recording: a small pill with a breathing red dot, the elapsed
/// time and a stop button. Sits just outside the recorded region and is kept out of the video.
/// </summary>
internal sealed class RecordingPill : Window
{
    private const double PillWidth = 118, PillHeight = 34, Margin8 = 8;

    private readonly TextBlock _time;
    private readonly TextBlock _kind;
    private readonly Ellipse _dot;
    private IntPtr _hwnd;

    public event Action? StopClicked;

    public RecordingPill()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Left = -32000;
        Top = -32000;
        Width = PillWidth;
        Height = PillHeight;

        _dot = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(SpaceTheme.RecordRed), Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        _time = new TextBlock
        {
            Text = "0:00",
            Foreground = SpaceTheme.StarWhiteBrush,
            FontFamily = SpaceTheme.UiFont,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 36,
        };
        _kind = new TextBlock
        {
            Foreground = SpaceTheme.DustBrush,
            FontFamily = SpaceTheme.UiFont,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 1, 0, 0),
        };

        var stopIcon = new Rectangle { Width = 10, Height = 10, RadiusX = 2, RadiusY = 2, Fill = SpaceTheme.StarWhiteBrush };
        var stop = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13),
            Background = SpaceTheme.WithAlpha(SpaceTheme.RecordRed, 0xCC),
            Child = stopIcon,
            Margin = new Thickness(0, 0, 4, 0),
            Cursor = Cursors.Hand,
            ToolTip = "Stop recording",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stop.MouseEnter += (_, _) => stop.Background = new SolidColorBrush(SpaceTheme.RecordRed);
        stop.MouseLeave += (_, _) => stop.Background = SpaceTheme.WithAlpha(SpaceTheme.RecordRed, 0xCC);
        stop.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            StopClicked?.Invoke();
        };

        var row = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(stop, Dock.Right);
        row.Children.Add(stop);
        row.Children.Add(_dot);
        row.Children.Add(_time);
        row.Children.Add(_kind);

        Content = new Border
        {
            CornerRadius = new CornerRadius(PillHeight / 2),
            Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xEE),
            BorderBrush = SpaceTheme.NavyLightBrush,
            BorderThickness = new Thickness(1),
            Child = row,
        };
    }

    /// <summary>Shows the pill below the region (or above it, or inside its bottom edge when it fills the screen).</summary>
    public void ShowFor(PixelRect region, string kind)
    {
        _kind.Text = kind;
        _time.Text = "0:00";
        if (_hwnd == IntPtr.Zero)
        {
            _hwnd = new WindowInteropHelper(this).EnsureHandle();
            RecordingNative.MakeOverlay(_hwnd, clickThrough: false);
        }

        var work = Win32.GetWorkArea(region.CenterX, region.CenterY, out var monitor, out var scale) ?? default;
        var w = (int)Math.Round(PillWidth * scale);
        var h = (int)Math.Round(PillHeight * scale);
        var gap = (int)Math.Round(Margin8 * scale);
        var x = Math.Clamp(region.CenterX - w / 2, work.Left, Math.Max(work.Left, work.Right - w));
        int y;
        if (region.Bottom + gap + h <= work.Bottom) y = region.Bottom + gap;
        else if (region.Y - gap - h >= work.Top) y = region.Y - gap - h;
        else y = Math.Min(region.Bottom, work.Bottom) - gap - h; // fullscreen: inside, bottom centre
        _ = monitor;

        Show();
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, y, w, h, Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);

        var breathe = new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(700))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        _dot.BeginAnimation(OpacityProperty, breathe);
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
    }

    public void SetElapsed(TimeSpan t) =>
        _time.Text = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    public void HideNow()
    {
        _dot.BeginAnimation(OpacityProperty, null);
        Hide();
    }
}
