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
/// Scrolling capture: pick a window or an area, and Cometsnap scrolls it with the mouse wheel,
/// grabbing a frame after every step and gluing them into one tall image. It stops by itself at
/// the end of the page; Esc or moving the mouse stops it earlier. The result is delivered like any
/// capture (clipboard, file, Copied card).
/// </summary>
public sealed class ScrollingCapture
{
    /// <summary>Taller images get heavy to paste anywhere; stop there.</summary>
    public const int MaxHeight = 30000;

    private const int MaxSteps = 400;
    private const int MouseTolerance = 12;

    private readonly CometApp _app;
    private ScrollingPill? _pill;
    private bool _running;
    private bool _stopRequested;

    public ScrollingCapture(CometApp app) => _app = app;

    public bool IsRunning => _running;

    /// <summary>Hotkey / menu: pick what to scroll. Pressed again while running: stops.</summary>
    public void Start()
    {
        if (_running)
        {
            _stopRequested = true;
            return;
        }
        _app.Capture.PickRegion(rect => _ = RunAsync(rect));
    }

    private async Task RunAsync(PixelRect rect)
    {
        if (_running) return;
        if (rect.Width < 40 || rect.Height < 60)
        {
            _app.Notify(L.T("Too small to scroll"), L.T("Pick a window or an area of at least 60 pixels high."));
            return;
        }

        _running = true;
        _stopRequested = false;
        var sw = Stopwatch.StartNew();
        Win32.GetCursorPos(out var userCursor);
        try
        {
            _pill ??= new ScrollingPill();
            _pill.ShowFor(rect);
            _app.SetTrayStatus(L.T("Cometsnap: scrolling capture. Esc to stop"));

            // The overlay just closed: let the screen repaint before the first frame.
            await Task.Delay(150);
            var stitcher = new ScrollStitcher(rect.Width, rect.Height);
            var frame = await SettledFrameAsync(rect, null, minimumWait: 0);
            stitcher.Add(frame);

            // The wheel scrolls whatever is under the mouse: park it in the middle of the area.
            var parkX = rect.X + rect.Width / 2;
            var parkY = rect.Y + rect.Height / 2;
            Win32.SetCursorPos(parkX, parkY);
            await Task.Delay(60);

            var notches = 3;
            var unchanged = 0;
            var lost = false;
            for (var step = 0; step < MaxSteps && !ShouldStop(parkX, parkY); step++)
            {
                ScrollNative.Wheel(-notches);
                frame = await SettledFrameAsync(rect, frame, minimumWait: 140);
                if (ShouldStop(parkX, parkY)) break;

                var result = stitcher.Add(frame);
                if (result == StitchStep.Unchanged)
                {
                    // Slow pages scroll late: give it one more try before calling it the end.
                    if (++unchanged >= 2) break;
                    await Task.Delay(250);
                    continue;
                }
                unchanged = 0;
                if (result == StitchStep.Lost)
                {
                    lost = true;
                    Log.Info($"Scrolling capture: frame {step} does not continue the page, stopping");
                    break;
                }

                // Aim for about half of the scrolling part per step: fast, with plenty of overlap.
                var target = Math.Max(20, stitcher.ScrollingHeight / 2);
                var shift = Math.Max(1, stitcher.LastShift);
                notches = Math.Clamp((int)Math.Round(notches * (double)target / shift), 1, 10);
                if (stitcher.LastShift > stitcher.ScrollingHeight * 3 / 4) notches = Math.Max(1, notches - 1);

                _pill.SetHeight(stitcher.Height);
                if (stitcher.Height >= MaxHeight) break;
            }

            var pixels = stitcher.Compose(out var height);
            if (height > MaxHeight) height = MaxHeight;
            var image = BitmapSource.Create(rect.Width, height, 96, 96, PixelFormats.Bgr32, null, pixels, rect.Width * 4);
            image.Freeze();
            Log.Info($"Scrolling capture: {rect.Width}×{height} in {sw.ElapsedMilliseconds} ms (header {stitcher.Header}, footer {stitcher.Footer}, lost {lost})");

            HidePill();
            if (height <= rect.Height)
                _app.Notify(L.T("Nothing scrolled"), L.T("Cometsnap kept what was visible. Pick the part of the window that scrolls, and keep the mouse still."));
            _app.Capture.DeliverImage(image, rect);
        }
        catch (Exception ex)
        {
            Log.Error("Scrolling capture failed", ex);
            HidePill();
            _app.Notify(L.T("Scrolling capture failed"), ex.Message);
        }
        finally
        {
            HidePill();
            _running = false;
            _app.SetTrayStatus(null);
            // Give the mouse back where the user left it.
            Win32.SetCursorPos(userCursor.X, userCursor.Y);
        }
    }

    private bool ShouldStop(int parkX, int parkY)
    {
        if (_stopRequested || Win32.IsKeyDown(0x1B /* VK_ESCAPE */)) return true;
        Win32.GetCursorPos(out var p);
        return Math.Abs(p.X - parkX) > MouseTolerance || Math.Abs(p.Y - parkY) > MouseTolerance;
    }

    /// <summary>Grabs the area until two grabs in a row are the same (smooth scrolling has finished).</summary>
    private static async Task<byte[]> SettledFrameAsync(PixelRect rect, byte[]? previous, int minimumWait)
    {
        if (minimumWait > 0) await Task.Delay(minimumWait);
        var current = FrozenScreen.Capture(rect).Pixels;
        var waited = minimumWait;
        while (waited < 1500)
        {
            await Task.Delay(70);
            waited += 70;
            var next = FrozenScreen.Capture(rect).Pixels;
            var same = next.AsSpan().SequenceEqual(current);
            current = next;
            // Still equal to the frame before the scroll: the page may just be slow to react.
            if (same && (previous is null || waited >= 400 || !next.AsSpan().SequenceEqual(previous))) break;
        }
        return current;
    }

    private void HidePill() => _pill?.HideNow();

    private static class ScrollNative
    {
        private const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const int WHEEL_DELTA = 120;

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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        /// <summary>Turns the wheel: negative = down. One event per notch, like a real wheel.</summary>
        public static void Wheel(int notches)
        {
            var count = Math.Abs(notches);
            var inputs = new INPUT[count];
            for (var i = 0; i < count; i++)
                inputs[i] = new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { mouseData = Math.Sign(notches) * WHEEL_DELTA, dwFlags = MOUSEEVENTF_WHEEL } };
            if (SendInput((uint)count, inputs, Marshal.SizeOf<INPUT>()) != count)
                Log.Info($"SendInput (wheel) failed: {Marshal.GetLastWin32Error()}");
        }
    }
}

/// <summary>The little pill shown while a scrolling capture runs. Kept out of the captured frames.</summary>
internal sealed class ScrollingPill : Window
{
    private const double PillWidth = 250, PillHeight = 34, Gap = 8;

    private readonly TextBlock _height;
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
        var title = new TextBlock
        {
            Text = L.T("Scrolling…"), Foreground = SpaceTheme.StarWhiteBrush, FontFamily = SpaceTheme.UiFont,
            FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
        };
        _height = new TextBlock
        {
            Foreground = SpaceTheme.SunBrush, FontFamily = SpaceTheme.UiFont, FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
        };
        var hint = new TextBlock
        {
            Text = L.T("Esc to stop"), Foreground = SpaceTheme.DustBrush, FontFamily = SpaceTheme.UiFont,
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

    public void SetHeight(int pixels) => _height.Text = L.F("{0:N0} px", pixels);

    public void HideNow()
    {
        _dot.BeginAnimation(OpacityProperty, null);
        Hide();
    }
}
