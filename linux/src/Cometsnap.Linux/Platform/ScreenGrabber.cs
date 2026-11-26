using System.Diagnostics;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Cometsnap.Linux.Core;
using PixelRect = Cometsnap.Linux.Core.PixelRect;

namespace Cometsnap.Linux.Platform;

/// <summary>
/// Grabs the desktop. X11: one XGetImage of the root window (tens of milliseconds, no dialog).
/// Wayland: the xdg-desktop-portal screenshot (the only way an app may see the screen there).
/// </summary>
public sealed class ScreenGrabber
{
    private IntPtr _display;
    private readonly object _gate = new();

    public bool UsesPortal => DesktopSession.IsWayland && !ForceX11;

    /// <summary>For tests / exotic setups: COMETSNAP_FORCE_X11=1 grabs through XWayland.</summary>
    public static bool ForceX11 { get; } = Environment.GetEnvironmentVariable("COMETSNAP_FORCE_X11") == "1";

    /// <param name="desktop">The desktop bounds in overlay coordinates (all monitors), as Avalonia reports them.</param>
    public async Task<FrozenScreen?> GrabAsync(PixelRect desktop)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var shot = UsesPortal ? await GrabPortalAsync(desktop) : GrabX11(desktop);
            Log.Info($"Screen grabbed in {sw.ElapsedMilliseconds} ms ({(UsesPortal ? "portal" : "X11")}, {shot?.Bounds})");
            return shot;
        }
        catch (Exception ex)
        {
            Log.Error("Screen grab failed", ex);
            return null;
        }
    }

    private unsafe FrozenScreen? GrabX11(PixelRect desktop)
    {
        lock (_gate)
        {
            if (_display == IntPtr.Zero) _display = X11.XOpenDisplay(IntPtr.Zero);
            if (_display == IntPtr.Zero) return null;
            var root = X11.XDefaultRootWindow(_display);
            var screen = X11.XDefaultScreen(_display);
            var rootW = X11.XDisplayWidth(_display, screen);
            var rootH = X11.XDisplayHeight(_display, screen);
            var r = desktop.IsEmpty ? new PixelRect(0, 0, rootW, rootH) : desktop.Intersect(new PixelRect(0, 0, rootW, rootH));
            if (r.IsEmpty) r = new PixelRect(0, 0, rootW, rootH);

            var image = X11.XGetImage(_display, root, r.X, r.Y, (uint)r.Width, (uint)r.Height, X11.AllPlanes, X11.ZPixmap);
            if (image == IntPtr.Zero) return null;
            try
            {
                var img = (X11.XImage*)image;
                var stride = r.Width * 4;
                var pixels = GC.AllocateUninitializedArray<byte>(stride * r.Height);
                if (img->bits_per_pixel != 32) return null;
                fixed (byte* dst = pixels)
                {
                    for (var y = 0; y < r.Height; y++)
                    {
                        var s = (uint*)((byte*)img->data + y * img->bytes_per_line);
                        var d = (uint*)(dst + y * stride);
                        for (var x = 0; x < r.Width; x++) d[x] = s[x] | 0xFF000000u; // BGRX → opaque BGRA
                    }
                }
                return new FrozenScreen(r, pixels, stride);
            }
            finally
            {
                X11.DestroyImage(image);
            }
        }
    }

    private static async Task<FrozenScreen?> GrabPortalAsync(PixelRect desktop)
    {
        var portal = await Portal.GetAsync();
        var path = await portal.ScreenshotAsync();
        if (path is null || !File.Exists(path)) return null;
        try
        {
            using var bitmap = new Bitmap(path);
            var w = bitmap.PixelSize.Width;
            var h = bitmap.PixelSize.Height;
            var stride = w * 4;
            var pixels = GC.AllocateUninitializedArray<byte>(stride * h);
            unsafe
            {
                fixed (byte* p = pixels)
                    bitmap.CopyPixels(new Avalonia.PixelRect(0, 0, w, h), (IntPtr)p, pixels.Length, stride);
            }
            // Make it opaque BGRA whatever the decoder produced.
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

            // The portal image covers the whole layout. Place it on the desktop the overlay knows;
            // when XWayland scales, the overlay maps it back (see CaptureOverlay).
            var origin = desktop.IsEmpty ? new PixelRect(0, 0, w, h) : desktop;
            return new FrozenScreen(origin, w, h, pixels, stride);
        }
        finally
        {
            // The portal saved the shot for us only: don't leave it behind.
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}
