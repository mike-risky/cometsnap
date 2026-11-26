using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Cometsnap.App.Theme;
using Cometsnap.Core.Native;

namespace Cometsnap.App.Capture;

/// <summary>
/// The little celebration after a capture: a comet flies from the captured area into the tray.
/// A tiny click-through window moved along a curve; it never takes focus.
/// </summary>
public sealed class CometFlight : Window
{
    private const double SizeDip = 120;
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(520);

    private readonly RotateTransform _rotate = new();
    private readonly ScaleTransform _scale = new();
    private IntPtr _hwnd;
    private Point _from, _to, _control;
    private DateTime _start;
    private bool _flying;

    public CometFlight()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;
        Focusable = false;
        Width = Height = SizeDip;
        Left = Top = -32000;

        var c = SizeDip / 2;
        var tail = new Polygon
        {
            Points = new PointCollection { new(c, c - 9), new(c - 58, c - 2), new(c - 58, c + 2), new(c, c + 9) },
            Fill = new LinearGradientBrush(Color.FromArgb(0, 79, 195, 255), Color.FromArgb(200, 120, 200, 255), new Point(0, 0.5), new Point(1, 0.5)),
        };
        var glow = new Ellipse
        {
            Width = 34, Height = 34,
            Fill = new RadialGradientBrush(Color.FromArgb(170, 120, 200, 255), Color.FromArgb(0, 79, 195, 255)),
        };
        Canvas.SetLeft(glow, c - 17);
        Canvas.SetTop(glow, c - 17);
        var head = new Ellipse { Width = 14, Height = 14, Fill = SpaceTheme.StarWhiteBrush };
        Canvas.SetLeft(head, c - 7);
        Canvas.SetTop(head, c - 7);

        var canvas = new Canvas { Width = SizeDip, Height = SizeDip, IsHitTestVisible = false };
        canvas.Children.Add(tail);
        canvas.Children.Add(glow);
        canvas.Children.Add(head);
        var group = new TransformGroup();
        group.Children.Add(_scale);
        group.Children.Add(_rotate);
        canvas.RenderTransform = group;
        canvas.RenderTransformOrigin = new Point(0.5, 0.5);
        Content = canvas;
    }

    /// <summary>Fly from one physical screen point to another.</summary>
    public void Launch(Point from, Point to)
    {
        if (_hwnd == IntPtr.Zero)
        {
            _hwnd = new WindowInteropHelper(this).EnsureHandle();
            var ex = Win32.GetWindowLong(_hwnd, Win32.GWL_EXSTYLE);
            SetWindowLong(_hwnd, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_NOACTIVATE | 0x80 /* WS_EX_TOOLWINDOW */);
        }

        _from = from;
        _to = to;
        // Arc upwards a little, like a thrown comet.
        var mid = new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
        var lift = Math.Min(220, (to - from).Length * 0.35);
        _control = new Point(mid.X, Math.Min(from.Y, to.Y) - lift);
        _start = DateTime.UtcNow;
        Opacity = 1;
        Move(from, 0);
        if (!IsVisible) Show();
        if (!_flying)
        {
            _flying = true;
            CompositionTarget.Rendering += OnFrame;
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var t = (DateTime.UtcNow - _start).TotalMilliseconds / Duration.TotalMilliseconds;
        if (t >= 1)
        {
            CompositionTarget.Rendering -= OnFrame;
            _flying = false;
            Hide();
            return;
        }
        var eased = t * t * (3 - 2 * t);
        Move(Bezier(eased), eased);
        Opacity = t > 0.8 ? (1 - t) / 0.2 : 1;
    }

    private void Move(Point p, double t)
    {
        // Point the tail backwards along the direction of travel.
        var ahead = Bezier(Math.Min(1, t + 0.02));
        var behind = Bezier(Math.Max(0, t - 0.02));
        var angle = Math.Atan2(ahead.Y - behind.Y, ahead.X - behind.X) * 180 / Math.PI;
        _rotate.Angle = angle;
        var s = 1 - 0.45 * t;
        _scale.ScaleX = _scale.ScaleY = s;

        if (!Win32.GetWindowRect(_hwnd, out var r)) return;
        var w = Math.Max(1, r.Width);
        var h = Math.Max(1, r.Height);
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, (int)(p.X - w / 2.0), (int)(p.Y - h / 2.0), 0, 0,
            Win32.SWP_NOACTIVATE | 0x0001 /* SWP_NOSIZE */);
    }

    private Point Bezier(double t)
    {
        var u = 1 - t;
        return new Point(
            u * u * _from.X + 2 * u * t * _control.X + t * t * _to.X,
            u * u * _from.Y + 2 * u * t * _control.Y + t * t * _to.Y);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
