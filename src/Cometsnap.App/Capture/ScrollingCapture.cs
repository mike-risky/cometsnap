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
/// turn the wheel. The overlay steps aside and the page under the area scrolls, wherever the
/// mouse is: Cometsnap takes the wheel and hands it to the window under the area, a little at a
/// time so it can follow. Every new row that comes into view is glued under the previous ones.
/// Letting go of the button finishes; Esc cancels.
/// </summary>
public sealed class ScrollingCapture
{
    /// <summary>Taller images get heavy to paste anywhere; stop there.</summary>
    public const int MaxHeight = 30000;

    /// <summary>The smallest area worth scrolling.</summary>
    public const int MinWidth = 40, MinHeight = 60;

    /// <summary>At most this much wheel (120 = one notch) is passed on between two grabs.</summary>
    private const int MaxDeltaPerGrab = 240;

    private readonly CometApp _app;
    private ScrollingPill? _pill;
    private RecordingFrame? _frame;
    private int _frameLook = -1;
    private volatile bool _running;

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
        using var wheel = new WheelTap();
        try
        {
            CaptureLook.Use(_app.Settings.CaptureTheme);
            if (_frameLook != CaptureLook.Version) { _frame?.Close(); _frame = null; _frameLook = CaptureLook.Version; }
            _frame ??= new RecordingFrame(CaptureLook.Accent);
            _frame.ShowAround(rect);
            _pill ??= new ScrollingPill();
            _pill.ShowFor(rect);
            _app.SetTrayStatus(L.T("Cometsnap: scrolling capture. Let go to finish"));

            // From now on the wheel is ours: it goes to the window under the area, wherever the mouse is.
            wheel.Start();
            wheel.Add(firstDelta);
            await Task.Delay(30); // the overlay is gone: let the screen repaint

            var target = new WheelTarget(rect);
            var pill = _pill;
            var dispatcher = Application.Current.Dispatcher;
            var job = await Task.Run(() => Follow(rect, wheel, target, (height, lost) =>
                dispatcher.InvokeAsync(() => pill.Show(height, lost))));

            HideChrome();
            if (job is null)
            {
                Log.Info("Scrolling capture cancelled");
                return;
            }

            var (pixels, height, log) = job.Value;
            var image = BitmapSource.Create(rect.Width, height, 96, 96, PixelFormats.Bgr32, null, pixels, rect.Width * 4);
            image.Freeze();
            Log.Info($"Scrolling capture: {rect.Width}×{height}, {log}");

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
            wheel.Dispose();
            HideChrome();
            _running = false;
            _app.SetTrayStatus(null);
        }
    }

    /// <summary>
    /// The capture loop, off the UI thread: grab the area as often as the screen changes, glue
    /// each frame, and pass the wheel on bit by bit. Null when cancelled with Esc.
    /// </summary>
    private static (byte[] Pixels, int Height, string Log)? Follow(PixelRect rect, WheelTap wheel, WheelTarget target, Action<int, bool> report)
    {
        var sw = Stopwatch.StartNew();
        using var grabber = new AreaGrabber(rect);
        var stitcher = new ScrollStitcher(rect.Width, rect.Height);
        var frame = grabber.Grab();
        stitcher.Add(frame);
        var spare = new byte[frame.Length];

        int frames = 0, appended = 0, lostFrames = 0;
        long lostSince = -1;
        var warned = false;
        var lastSent = 0L;
        while (true)
        {
            if (Win32.IsKeyDown(0x1B /* VK_ESCAPE */)) return null;
            var released = !WheelTarget.PrimaryButtonDown();

            // Pass the wheel on, a little at a time, so the page never runs away from the capture.
            var delta = wheel.Take(MaxDeltaPerGrab);
            if (delta != 0)
            {
                target.Scroll(delta);
                lastSent = sw.ElapsedMilliseconds;
            }

            if (released)
            {
                // Let go: hand over what is left of the wheel, and let the page come to rest.
                for (var rest = wheel.Take(MaxDeltaPerGrab); rest != 0; rest = wheel.Take(MaxDeltaPerGrab))
                {
                    target.Scroll(rest);
                    Thread.Sleep(30);
                    AddFrame();
                }
                var still = 0;
                for (var t = 0; t < 40 && still < 3; t++) // at most ~0.6 s
                {
                    Thread.Sleep(15);
                    still = AddFrame() ? 0 : still + 1;
                }
                break;
            }

            AddFrame();
            if (stitcher.Height >= MaxHeight) break;
            // Grab again soon: while the page moves, about every frame of the screen.
            Thread.Sleep(sw.ElapsedMilliseconds - lastSent < 400 ? 4 : 15);
        }

        var pixels = stitcher.Compose(out var height);
        height = Math.Min(height, MaxHeight);
        return (pixels, height, $"{frames} frames ({appended} glued, {lostFrames} lost) in {sw.ElapsedMilliseconds} ms, header {stitcher.Header}, footer {stitcher.Footer}");

        // Grabs the area; true when it had changed.
        bool AddFrame()
        {
            grabber.Grab(spare);
            if (spare.AsSpan().SequenceEqual(frame)) return false;
            (frame, spare) = (spare, frame);
            frames++;
            // The stitcher keeps the frames it glued: give it its own copy.
            switch (stitcher.Add((byte[])frame.Clone()))
            {
                case StitchStep.Appended:
                    appended++;
                    lostSince = -1;
                    if (warned) warned = false;
                    report(stitcher.Height, false);
                    break;
                case StitchStep.Lost:
                    lostFrames++;
                    if (lostSince < 0) lostSince = sw.ElapsedMilliseconds;
                    // A frame caught mid-paint is no reason to worry: only say so when it lasts.
                    if (!warned && sw.ElapsedMilliseconds - lostSince > 250)
                    {
                        warned = true;
                        report(stitcher.Height, true);
                    }
                    break;
            }
            return true;
        }
    }

    private void HideChrome()
    {
        _pill?.HideNow();
        _frame?.Hide();
    }

    /// <summary>Grabs one area of the screen again and again into reused buffers (no CAPTUREBLT: much faster).</summary>
    private sealed class AreaGrabber : IDisposable
    {
        private readonly PixelRect _rect;
        private readonly IntPtr _screenDc, _memDc, _dib, _old, _bits;

        public AreaGrabber(PixelRect rect)
        {
            _rect = rect;
            _screenDc = Win32.GetDC(IntPtr.Zero);
            _memDc = Win32.CreateCompatibleDC(_screenDc);
            var header = new Win32.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
                biWidth = rect.Width,
                biHeight = -rect.Height, // top-down
                biPlanes = 1,
                biBitCount = 32,
            };
            _dib = Win32.CreateDIBSection(_screenDc, ref header, 0, out _bits, IntPtr.Zero, 0);
            if (_dib == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection failed");
            _old = Win32.SelectObject(_memDc, _dib);
        }

        public byte[] Grab()
        {
            var pixels = new byte[_rect.Width * 4 * _rect.Height];
            Grab(pixels);
            return pixels;
        }

        public void Grab(byte[] into)
        {
            Win32.BitBlt(_memDc, 0, 0, _rect.Width, _rect.Height, _screenDc, _rect.X, _rect.Y, Win32.SRCCOPY);
            Marshal.Copy(_bits, into, 0, into.Length);
        }

        public void Dispose()
        {
            Win32.SelectObject(_memDc, _old);
            Win32.DeleteObject(_dib);
            Win32.DeleteDC(_memDc);
            Win32.ReleaseDC(IntPtr.Zero, _screenDc);
        }
    }

    /// <summary>
    /// Takes the real mouse wheel while the capture runs (a low-level mouse hook), so the user can
    /// scroll with the mouse anywhere. The turns are kept and handed out with <see cref="Take"/>.
    /// </summary>
    private sealed class WheelTap : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const uint LLMHF_INJECTED = 0x01;

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public int X, Y;
            public uint mouseData, flags, time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr hMod, uint threadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string? name);

        private readonly HookProc _proc;
        private IntPtr _hook;
        private int _pending;

        public WheelTap() => _proc = OnMouse;

        public void Start()
        {
            if (_hook != IntPtr.Zero) return;
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) Log.Info($"Mouse hook failed ({Marshal.GetLastWin32Error()}): the wheel scrolls what is under the mouse");
        }

        public void Add(int delta) => Interlocked.Add(ref _pending, delta);

        /// <summary>Up to <paramref name="max"/> of the waiting wheel turn (either way); the rest keeps waiting.</summary>
        public int Take(int max)
        {
            while (true)
            {
                var pending = Volatile.Read(ref _pending);
                var take = Math.Clamp(pending, -max, max);
                if (Interlocked.CompareExchange(ref _pending, pending - take, pending) == pending) return take;
            }
        }

        private IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && (int)wParam == WM_MOUSEWHEEL)
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if ((info.flags & LLMHF_INJECTED) == 0)
                {
                    Add((short)(info.mouseData >> 16));
                    return 1; // ours: passed on to the area by the capture loop
                }
            }
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    /// <summary>Scrolls the window under the area, whether the mouse is over it or not.</summary>
    private sealed class WheelTarget
    {
        private const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const int SM_SWAPBUTTON = 23;
        private const uint WM_MOUSEWHEEL = 0x020A;

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
        private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        private readonly PixelRect _rect;
        private readonly int _cx, _cy;
        private readonly IntPtr _window;

        public WheelTarget(PixelRect rect)
        {
            _rect = rect;
            _cx = rect.X + rect.Width / 2;
            _cy = rect.Y + rect.Height / 2;
            _window = WindowFromPoint(new POINT { X = _cx, Y = _cy });
            // Bring it to the front, so a real wheel turn over it scrolls it even when
            // "Scroll inactive windows when hovering over them" is off.
            if (_window != IntPtr.Zero)
            {
                var root = GetAncestor(_window, 2 /* GA_ROOT */);
                Win32.SetForegroundWindow(root == IntPtr.Zero ? _window : root);
            }
        }

        public void Scroll(int delta)
        {
            Win32.GetCursorPos(out var p);
            if (_rect.Contains(p.X, p.Y) || _window == IntPtr.Zero)
            {
                // Over the area: a real wheel turn, exactly as if the user did it (smooth scrolling and all).
                var inputs = new[] { new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { mouseData = delta, dwFlags = MOUSEEVENTF_WHEEL } } };
                if (SendInput(1, inputs, Marshal.SizeOf<INPUT>()) != 1)
                    Log.Info($"SendInput (wheel) failed: {Marshal.GetLastWin32Error()}");
            }
            else
            {
                // Elsewhere: tell the window under the area that the wheel turned over its middle.
                var wParam = (IntPtr)((delta & 0xFFFF) << 16);
                var lParam = (IntPtr)((_cy & 0xFFFF) << 16 | (_cx & 0xFFFF));
                PostMessage(_window, WM_MOUSEWHEEL, wParam, lParam);
            }
        }

        /// <summary>Whether the primary button is still held (the physical right one when buttons are swapped).</summary>
        public static bool PrimaryButtonDown() =>
            Win32.IsKeyDown(Win32.GetSystemMetrics(SM_SWAPBUTTON) != 0 ? 0x02 /* VK_RBUTTON */ : 0x01 /* VK_LBUTTON */);
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

        _dot = new Ellipse { Width = 10, Height = 10, Fill = CaptureLook.AccentBrush, Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
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
        _dot.Fill = CaptureLook.AccentBrush;
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
        _dot.Fill = lost ? (CaptureLook.Theme.Id == "yellow" ? SpaceTheme.TerracottaBrush : SpaceTheme.SunBrush) : CaptureLook.AccentBrush;
    }

    public void HideNow()
    {
        _dot.BeginAnimation(OpacityProperty, null);
        Hide();
    }
}
