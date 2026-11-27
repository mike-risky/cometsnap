using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Platform;
using Cometsnap.Linux.Theme;

namespace Cometsnap.Linux.Services;

/// <summary>
/// Drags a capture out of Cometsnap into any app. It offers the file (file managers, browsers,
/// chat and mail apps) and the PNG itself (office suites, image editors), and shows the
/// thumbnail under the cursor while dragging.
/// </summary>
public static class DragOut
{
    /// <summary>True while a drag runs (the release that ends it must not count as a click).</summary>
    public static bool Active { get; private set; }

    /// <summary>Starts a drag with the left button already held. The task ends with true when an app took the drop.</summary>
    public static async Task<bool> StartAsync(string? path, Bitmap? image, Bitmap? preview)
    {
        if (Active || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) return false;
        var hasFile = path is not null && File.Exists(path);
        Func<byte[]?>? png = null;
        if (path is null || !MediaKinds.IsRecording(path))
        {
            if (hasFile && path!.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) png = () => TryRead(path);
            else if (image is not null)
            {
                // Encode now, on the UI thread: the bitmap belongs to it.
                var bytes = CaptureStore.EncodePng(image);
                png = () => bytes;
            }
        }
        if (!hasFile && png is null) return false;

        Active = true;
        DragGhost? ghost = null;
        try
        {
            if ((preview ?? image) is { } picture) ghost = new DragGhost(picture);
            return await XdndSource.RunAsync(new XdndSource.Payload(hasFile ? path : null, png),
                (x, y) => Dispatcher.UIThread.Post(() => ghost?.Follow(x, y)));
        }
        catch (Exception ex)
        {
            Log.Error("Drag out failed", ex);
            return false;
        }
        finally
        {
            ghost?.Close();
            // The pointer release reaches our window right after; let it pass first.
            DispatcherTimer.RunOnce(() => Active = false, TimeSpan.FromMilliseconds(120));
        }
    }

    /// <summary>Drags several captures at once, as files (file managers, chat and mail apps take a group).</summary>
    public static async Task<bool> StartFilesAsync(IReadOnlyList<string> paths, Bitmap? preview)
    {
        var files = paths.Where(File.Exists).ToList();
        if (files.Count <= 1) return files.Count == 1 && await StartAsync(files[0], null, preview);
        if (Active || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) return false;
        Active = true;
        DragGhost? ghost = null;
        try
        {
            if (preview is not null) ghost = new DragGhost(preview, files.Count);
            return await XdndSource.RunAsync(new XdndSource.Payload(files, null),
                (x, y) => Dispatcher.UIThread.Post(() => ghost?.Follow(x, y)));
        }
        catch (Exception ex)
        {
            Log.Error("Drag out of several files failed", ex);
            return false;
        }
        finally
        {
            ghost?.Close();
            DispatcherTimer.RunOnce(() => Active = false, TimeSpan.FromMilliseconds(120));
        }
    }

    private static byte[]? TryRead(string path)
    {
        try { return File.ReadAllBytes(path); }
        catch { return null; }
    }

    /// <summary>The see-through thumbnail that follows the cursor during a drag. Click-through.</summary>
    private sealed class DragGhost : Window
    {
        private const double MaxSide = 132;

        public DragGhost(Bitmap picture, int count = 1)
        {
            Title = "Cometsnap";
            SystemDecorations = SystemDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None };
            Background = Brushes.Transparent;
            CanResize = false;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            IsHitTestVisible = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Opacity = 0.85;
            var w = (double)picture.PixelSize.Width;
            var h = (double)picture.PixelSize.Height;
            var scale = Math.Min(1, MaxSide / Math.Max(1, Math.Max(w, h)));
            var img = new Image { Source = picture, Width = Math.Max(24, w * scale), Height = Math.Max(16, h * scale), Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.HighQuality);
            var frame = new Border
            {
                Child = img, CornerRadius = new CornerRadius(6), BorderBrush = SpaceTheme.CometBlueBrush, BorderThickness = new Thickness(2),
                Background = SpaceTheme.VoidBrush, ClipToBounds = true,
            };
            if (count <= 1) Content = frame;
            else
            {
                // A small stack of cards behind the picture, and how many files are coming along.
                var stack = new Grid();
                for (var i = Math.Min(count, 3) - 1; i >= 1; i--)
                    stack.Children.Add(new Border
                    {
                        Width = img.Width + 4, Height = img.Height + 4, CornerRadius = new CornerRadius(6),
                        Background = SpaceTheme.NavyBrush, BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1.5),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                        Margin = new Thickness(i * 6, 12 - i * 6, 0, 0),
                    });
                frame.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                frame.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
                frame.Margin = new Thickness(0, 12, 12, 0);
                stack.Children.Add(frame);
                stack.Children.Add(new Border
                {
                    MinWidth = 26, Height = 26, CornerRadius = new CornerRadius(13), Padding = new Thickness(7, 0),
                    Background = SpaceTheme.TerracottaBrush, BorderBrush = SpaceTheme.StarWhiteBrush, BorderThickness = new Thickness(1.5),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                    Child = new TextBlock
                    {
                        Text = count.ToString(System.Globalization.CultureInfo.CurrentCulture), Foreground = SpaceTheme.StarWhiteBrush,
                        FontWeight = FontWeight.Bold, FontSize = 13,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    },
                });
                Content = stack;
            }
            Position = X11.PointerPosition() is { } p ? new PixelPoint(p.X + 14, p.Y + 18) : new PixelPoint(-32000, -32000);
            Opened += (_, _) => X11.MakeClickThrough(this);
            Show();
        }

        /// <summary>Sits just below-right of the cursor, so it never hides what you drop onto.</summary>
        public void Follow(int x, int y) => Position = new PixelPoint(x + 14, y + 18);
    }
}
