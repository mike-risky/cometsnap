using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cometsnap.App.Theme;
using Cometsnap.Core.Diagnostics;
using Cometsnap.Core.Native;
using Cometsnap.Core.Storage;

namespace Cometsnap.App.Services;

/// <summary>
/// Drags a capture out of Cometsnap into any app. It offers the file (Explorer, Outlook, Teams,
/// browsers, Slack…), the picture itself (Word, PowerPoint, Paint…) and PNG bytes (Office, web
/// apps), and shows the thumbnail under the cursor while dragging.
/// </summary>
public static class DragOut
{
    /// <summary>Runs the drag (blocks until the drop). Returns what the target did with it.</summary>
    public static DragDropEffects Start(UIElement source, string? path, BitmapSource? image, ImageSource? preview)
    {
        var data = new DataObject();
        var hasFile = path is not null && File.Exists(path);
        if (hasFile)
        {
            data.SetData(DataFormats.FileDrop, new[] { path! });
        }

        if (path is null || !MediaKinds.IsRecording(path))
        {
            try
            {
                byte[]? png = hasFile && path!.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? File.ReadAllBytes(path) : null;
                if (image is null && png is not null)
                {
                    image = BitmapFrame.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                }
                if (image is not null)
                {
                    data.SetImage(image);
                    png ??= Encode(image);
                    data.SetData("PNG", new MemoryStream(png), false);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Drag image data failed", ex);
            }
        }
        if (data.GetFormats().Length == 0) return DragDropEffects.None;

        DragGhost? ghost = null;
        GiveFeedbackEventHandler feedback = (_, e) =>
        {
            ghost?.Follow();
            e.UseDefaultCursors = true;
        };
        try
        {
            if ((preview ?? image) is { } picture)
            {
                ghost = new DragGhost(picture);
                ghost.Follow();
            }
            source.GiveFeedback += feedback;
            return DragDrop.DoDragDrop(source, data, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            Log.Error("Drag out failed", ex);
            return DragDropEffects.None;
        }
        finally
        {
            source.GiveFeedback -= feedback;
            ghost?.Close();
        }
    }

    private static byte[] Encode(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>The see-through thumbnail that follows the cursor during a drag. Click-through.</summary>
    private sealed class DragGhost : Window
    {
        private const double MaxSide = 132;
        private IntPtr _hwnd;

        public DragGhost(ImageSource picture)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            IsHitTestVisible = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Left = Top = -32000;
            Opacity = 0.85;

            var w = picture.Width;
            var h = picture.Height;
            var scale = Math.Min(1, MaxSide / Math.Max(1, Math.Max(w, h)));
            var img = new Image { Source = picture, Width = Math.Max(24, w * scale), Height = Math.Max(16, h * scale), Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            Content = new Border
            {
                Child = img, CornerRadius = new CornerRadius(6), BorderBrush = SpaceTheme.CometBlueBrush, BorderThickness = new Thickness(2),
                Background = SpaceTheme.VoidBrush, Margin = new Thickness(8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black },
            };
            SourceInitialized += (_, _) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                var ex = Win32.GetWindowLong(_hwnd, Win32.GWL_EXSTYLE);
                SetWindowLong(_hwnd, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_NOACTIVATE | 0x80 /* WS_EX_TOOLWINDOW */);
            };
            Show();
        }

        /// <summary>Sits just below-right of the cursor, so it never hides what you drop onto.</summary>
        public void Follow()
        {
            if (_hwnd == IntPtr.Zero || !Win32.GetCursorPos(out var p)) return;
            Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, p.X + 14, p.Y + 18, 0, 0, Win32.SWP_NOACTIVATE | 0x0001 /* SWP_NOSIZE */);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
