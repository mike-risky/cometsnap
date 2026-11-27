using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Cometsnap.App.Theme;
using Cometsnap.Core.Geometry;
using Cometsnap.Core.Native;

namespace Cometsnap.App.Recording;

/// <summary>
/// A thin red line drawn just outside the recorded region, so you always know what is being
/// recorded (blue around a scrolling capture). Click-through, never focused, and outside the region (and excluded from capture).
/// </summary>
internal sealed class RecordingFrame : Window
{
    private const int Gap = 1;   // physical pixels between the region and the line
    private const int Line = 2;  // line thickness in physical pixels

    private readonly Border _border;
    private IntPtr _hwnd;

    public RecordingFrame(Color? color = null)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        Left = -32000;
        Top = -32000;
        Width = 1;
        Height = 1;
        _border = new Border
        {
            BorderBrush = SpaceTheme.WithAlpha(color ?? SpaceTheme.RecordRed, 0xE6),
            CornerRadius = new CornerRadius(1),
        };
        Content = _border;
    }

    public void ShowAround(PixelRect region)
    {
        if (_hwnd == IntPtr.Zero)
        {
            _hwnd = new WindowInteropHelper(this).EnsureHandle();
            RecordingNative.MakeOverlay(_hwnd, clickThrough: true);
        }
        var outer = region.Inflate(Gap + Line);
        Show();
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, outer.X, outer.Y, outer.Width, outer.Height, Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        _border.BorderThickness = new Thickness(Line / dpi);
        _border.Margin = new Thickness(0);
        Opacity = 0;
        BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
    }
}
