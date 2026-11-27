using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Cometsnap.App.Recording;
using Cometsnap.App.Theme;
using Cometsnap.Core.Capture;
using Cometsnap.Core.Diagnostics;
using Cometsnap.Core.Geometry;
using Cometsnap.Core.Localization;
using Cometsnap.Core.Native;

namespace Cometsnap.App.Capture;

/// <summary>
/// Scrolling capture, the natural way: start an area selection, keep the mouse button held and
/// turn the wheel. The overlay steps aside, the page scrolls under the area as usual, and every
/// new row that comes into view is glued under the previous ones. Letting go of the button
/// finishes: the tall image is delivered like any capture (clipboard, file, Copied card).
/// Esc cancels. Scrolling too fast (or back up) is fine: the page is picked up again once it
/// lines up with the last good frame.
/// </summary>
public sealed class ScrollingCapture
{
    /// <summary>Taller images get heavy to paste anywhere; stop there.</summary>
    public const int MaxHeight = 30000;

    /// <summary>The smallest area worth scrolling.</summary>
    public const int MinWidth = 40, MinHeight = 60;

    private const int GrabEveryMs = 33;

    private readonly CometApp _app;
    private ScrollingPill? _pill;
    private RecordingFrame? _frame;
    private bool _running;

    public ScrollingCapture(CometApp app) => _app = app;

    public bool IsRunning => _running;

    /// <summary>
    /// Hotkey / menu: opens the usual capture overlay with a reminder of how it works
    /// (hold the button and scroll). Any capture can turn into a scrolling one.
    /// </summary>
    public void Start()
    {
        if (_running) return;
        _app.Capture.StartRegion(scrollHint: true);
    }

    /// <summary>
    /// Called by the overlay when the wheel turned while the button was held over <paramref name="rect"/>.
    /// <paramref name="firstDelta"/> is that first wheel turn, which the overlay took: it is passed on to the page.
    /// </summary>
    public void RunHeld(PixelRect rect, int firstDelta, Action<BitmapSource, PixelRect>? deliver)
    {
        if (_running) return;
        _ = RunAsync(rect, firstDelta, deliver);
    }

    private async Task RunAsync(PixelRect rect, int firstDelta, Action<BitmapSource, PixelRect>? deliver)
    {
        _running = true;
        var sw = Stopwatch.StartNew();
        var cancelled = false;
        var frames = 0;
        try
        {
            _frame ??= new RecordingFrame(SpaceTheme.CometBlue);
            _frame.ShowAround(rect);
            _pill ??= new ScrollingPill();
            _pill.ShowFor(rect);
            _app.SetTrayStatus(L.T("Cometsnap: scrolling capture. Let go to finish"));

            // The overlay is gone: the window under the mouse gets the wheel from now on.
            ScrollNative.FocusWindowUnderCursor();
            await Task.Delay(30);

            var stitcher = new ScrollStitcher(rect.Width, rect.Height);
            var frame = FrozenScreen.Capture(rect).Pixels;
            stitcher.Add(frame);
            ScrollNative.Wheel(firstDelta);

            var lost = false;
            while (true)
            {
                await Task.Delay(GrabEveryMs);
                if (Win32.IsKeyDown(0x1B /* VK_ESCAPE */))
                {
                    cancelled = true;
                    break;
                }

                var released = !ScrollNative.PrimaryButtonDown();
                // Let go: wait for the last turn of the wheel to finish scrolling.
                var next = released ? await SettledFrameAsync(rect) : FrozenScreen.Capture(rect).Pixels;
                if (!next.AsSpan().SequenceEqual(frame))
                {
                    frame = next;
                    frames++;
                    switch (stitcher.Add(next))
                    {
                        case StitchStep.Appended:
                            lost = false;
                            _pill.Show(stitcher.Height, lost: false);
                            break;
                        case StitchStep.Lost when !lost:
                            lost = true;
                            _pill.Show(stitcher.Height, lost: true);
                            break;
                    }
                }
                if (released || stitcher.Height >= MaxHeight) break;
            }

            HideChrome();
            if (cancelled)
            {
                Log.Info("Scrolling capture cancelled");
                return;
            }

            var pixels = stitcher.Compose(out var height);
            height = Math.Min(height, MaxHeight);
            var image = BitmapSource.Create(rect.Width, height, 96, 96, PixelFormats.Bgr32, null, pixels, rect.Width * 4);
            image.Freeze();
            Log.Info($"Scrolling capture: {rect.Width}×{height} from {frames} frames in {sw.ElapsedMilliseconds} ms (header {stitcher.Header}, footer {stitcher.Footer}, ended lost {lost})");

            if (deliver is not null) deliver(image, rect);
            else _app.Capture.DeliverImage(image, rect);
        }
        catch (Exception ex)
        {
            Log.Error("Scrolling capture failed", ex);
            HideChrome();
            _app.Notify(L.T("Scrolling capture failed"), ex.Message);
        }
        finally
        {
            HideChrome();
            _running = false;
            _app.SetTrayStatus(null);
        }
    }

    /// <summary>Grabs the area until two grabs in a row are the same (smooth scrolling has finished), at most ~0.6 s.</summary>
    private static async Task<byte[]> SettledFrameAsync(PixelRect rect)
    {
        var current = FrozenScreen.Capture(rect).Pixels;
        for (var waited = 0; waited < 600; waited += 60)
        {
            await Task.Delay(60);
            var next = FrozenScreen.Capture(rect).Pixels;
            var same = next.AsSpan().SequenceEqual(current);
            current = next;
            if (same) break;
        }
        return current;
    }

    private void HideChrome()
    {
        _pill?.HideNow();
        _frame?.Hide();
    }

    private static class ScrollNative
    {
        private const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const int SM_SWAPBUTTON = 23;
        private const uint GA_ROOT = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx, dy;
            public int mouseData;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X, Y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        /// <summary>Turns the wheel by <paramref name="delta"/> (120 = one notch up, negative = down).</summary>
        public static void Wheel(int delta)
        {
            if (delta == 0) return;
            var inputs = new[] { new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { mouseData = delta, dwFlags = MOUSEEVENTF_WHEEL } } };
            if (SendInput(1, inputs, Marshal.SizeOf<INPUT>()) != 1)
                Log.Info($"SendInput (wheel) failed: {Marshal.GetLastWin32Error()}");
        }

        /// <summary>Whether the primary button is still held (the physical right one when buttons are swapped).</summary>
        public static bool PrimaryButtonDown() =>
            Win32.IsKeyDown(Win32.GetSystemMetrics(SM_SWAPBUTTON) != 0 ? 0x02 /* VK_RBUTTON */ : 0x01 /* VK_LBUTTON */);

        /// <summary>
        /// Brings the window under the mouse to the front, so the wheel scrolls it even when
        /// "Scroll inactive windows when hovering over them" is off.
        /// </summary>
        public static void FocusWindowUnderCursor()
        {
            Win32.GetCursorPos(out var p);
            var hwnd = WindowFromPoint(new POINT { X = p.X, Y = p.Y });
            if (hwnd == IntPtr.Zero) return;
            var root = GetAncestor(hwnd, GA_ROOT);
            Win32.SetForegroundWindow(root == IntPtr.Zero ? hwnd : root);
        }
    }
}

/// <summary>The little pill shown while a scrolling capture runs. Kept out of the captured frames.</summary>
internal sealed class ScrollingPill : Window
{
    private const double PillWidth = 300, PillHeight = 34, Gap = 8;

    private readonly TextBlock _height;
    private readonly TextBlock _title;
    private readonly Ellipse _dot;
    private IntPtr _hwnd;

    public ScrollingPill()
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

        _dot = new Ellipse { Width = 10, Height = 10, Fill = SpaceTheme.CometBlueBrush, Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = _title = new TextBlock
        {
            Text = L.T("Scroll, then let go"), Foreground = SpaceTheme.StarWhiteBrush, FontFamily = SpaceTheme.UiFont,
            FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
        };
        _height = new TextBlock
        {
            Foreground = SpaceTheme.SunBrush, FontFamily = SpaceTheme.UiFont, FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
        };
        var hint = new TextBlock
        {
            Text = L.T("Esc cancels"), Foreground = SpaceTheme.DustBrush, FontFamily = SpaceTheme.UiFont,
            FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0),
        };
        var row = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(hint, Dock.Right);
        row.Children.Add(hint);
        row.Children.Add(_dot);
        row.Children.Add(title);
        row.Children.Add(_height);

        Content = new Border
        {
            CornerRadius = new CornerRadius(PillHeight / 2),
            Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xEE),
            BorderBrush = SpaceTheme.NavyLightBrush,
            BorderThickness = new Thickness(1),
            Child = row,
        };
    }

    /// <summary>Shows the pill above the area (or below, or inside its top edge when it fills the screen).</summary>
    public void ShowFor(PixelRect region)
    {
        _height.Text = "";
        _title.Text = L.T("Scroll, then let go");
        _dot.Fill = SpaceTheme.CometBlueBrush;
        if (_hwnd == IntPtr.Zero)
        {
            _hwnd = new WindowInteropHelper(this).EnsureHandle();
            RecordingNative.MakeOverlay(_hwnd, clickThrough: true);
        }

        var work = Win32.GetWorkArea(region.CenterX, region.CenterY, out _, out var scale) ?? default;
        var w = (int)Math.Round(PillWidth * scale);
        var h = (int)Math.Round(PillHeight * scale);
        var gap = (int)Math.Round(Gap * scale);
        var x = Math.Clamp(region.CenterX - w / 2, work.Left, Math.Max(work.Left, work.Right - w));
        int y;
        if (region.Y - gap - h >= work.Top) y = region.Y - gap - h;
        else if (region.Bottom + gap + h <= work.Bottom) y = region.Bottom + gap;
        else y = Math.Max(region.Y, work.Top) + gap; // the area fills the screen: inside, top centre

        Show();
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, y, w, h, Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
        _dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(500))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        });
    }

    /// <summary>The height so far, or a warning when the page went by too fast to follow.</summary>
    public void Show(int pixels, bool lost)
    {
        _height.Text = lost ? "" : L.F("{0:N0} px", pixels);
        _title.Text = lost ? L.T("Too fast: scroll back a little") : L.T("Scroll, then let go");
        _dot.Fill = lost ? SpaceTheme.SunBrush : SpaceTheme.CometBlueBrush;
    }

    public void HideNow()
    {
        _dot.BeginAnimation(OpacityProperty, null);
        Hide();
    }
}
