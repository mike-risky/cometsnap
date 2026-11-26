using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cometsnap.App.Theme;
using Cometsnap.Core.Diagnostics;

namespace Cometsnap.App.Recording;

/// <summary>
/// Tray icons for "recording": the normal comet plus a red dot at the bottom-right whose
/// glow breathes in and out (brand rule from BRAND.md).
/// </summary>
internal sealed class RecordingIcons : IDisposable
{
    public const int FrameCount = 10;
    private readonly IntPtr[] _icons = new IntPtr[FrameCount];

    public RecordingIcons()
    {
        try
        {
            var size = Math.Max(16, RecordingNative.GetSystemMetrics(49 /* SM_CXSMICON */));
            var logo = new BitmapImage(new Uri("pack://application:,,,/Assets/cometsnap-256.png"));
            for (var i = 0; i < FrameCount; i++)
            {
                // 0..1..0 over the cycle
                var t = (1 - Math.Cos(2 * Math.PI * i / FrameCount)) / 2;
                _icons[i] = Render(logo, size, t);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not build the recording tray icons", ex);
        }
    }

    public IntPtr this[int frame] => _icons[frame % FrameCount];

    private static IntPtr Render(BitmapSource logo, int size, double pulse)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(logo, new Rect(0, 0, size, size));
            var r = size * 0.26;
            var c = new Point(size - r - 0.5, size - r - 0.5);
            var red = SpaceTheme.RecordRed;
            // Soft halo that grows with the pulse, then the dot with a dark ring for contrast.
            var halo = new SolidColorBrush(Color.FromArgb((byte)(90 * pulse), red.R, red.G, red.B));
            dc.DrawEllipse(halo, null, c, r + size * 0.08 * pulse, r + size * 0.08 * pulse);
            var dot = new SolidColorBrush(Color.FromArgb((byte)(255 - 70 * (1 - pulse)), red.R, red.G, red.B));
            dc.DrawEllipse(dot, new Pen(SpaceTheme.VoidBrush, Math.Max(1, size / 16.0)), c, r, r);
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var straight = new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[size * size * 4];
        straight.CopyPixels(pixels, size * 4, 0);
        return CreateIcon(pixels, size);
    }

    private static IntPtr CreateIcon(byte[] bgra, int size)
    {
        var header = new RecordingNative.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<RecordingNative.BITMAPINFOHEADER>(),
            biWidth = size,
            biHeight = -size,
            biPlanes = 1,
            biBitCount = 32,
        };
        var color = RecordingNative.CreateDIBSection(IntPtr.Zero, ref header, 0, out var bits, IntPtr.Zero, 0);
        Marshal.Copy(bgra, 0, bits, bgra.Length);
        var mask = RecordingNative.CreateBitmap(size, size, 1, 1, new byte[((size + 15) / 16) * 2 * size]);
        var info = new RecordingNative.ICONINFO { fIcon = true, hbmColor = color, hbmMask = mask };
        var icon = RecordingNative.CreateIconIndirect(ref info);
        RecordingNative.DeleteObject(color);
        RecordingNative.DeleteObject(mask);
        return icon;
    }

    public void Dispose()
    {
        for (var i = 0; i < _icons.Length; i++)
        {
            if (_icons[i] != IntPtr.Zero) RecordingNative.DestroyIcon(_icons[i]);
            _icons[i] = IntPtr.Zero;
        }
    }
}
