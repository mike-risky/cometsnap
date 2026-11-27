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

    /// <summary>
    /// Drags several captures at once (blocks until the drop). Offers them as files only, which is
    /// what Explorer, Outlook, Teams, Slack and browsers take for a group. One path = <see cref="Start"/>.
    /// </summary>
    public static DragDropEffects StartFiles(UIElement source, IReadOnlyList<string> paths, ImageSource? preview)
    {
        var files = paths.Where(File.Exists).ToArray();
        if (files.Length == 0) return DragDropEffects.None;
        if (files.Length == 1) return Start(source, files[0], null, preview);

        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, files);
        DragGhost? ghost = null;
        GiveFeedbackEventHandler feedback = (_, e) =>
        {
            ghost?.Follow();
            e.UseDefaultCursors = true;
        };
        try
        {
            if (preview is not null)
            {
                ghost = new DragGhost(preview, files.Length);
                ghost.Follow();
            }
            source.GiveFeedback += feedback;
            return DragDrop.DoDragDrop(source, data, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            Log.Error("Drag out of several files failed", ex);
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

        public DragGhost(ImageSource picture, int count = 1)
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
            var card = new Border
            {
                Child = img, CornerRadius = new CornerRadius(6), BorderBrush = SpaceTheme.CometBlueBrush, BorderThickness = new Thickness(2),
                Background = SpaceTheme.VoidBrush, Margin = new Thickness(8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black },
            };
            if (count <= 1)
            {
                Content = card;
            }
            else
            {
                // A small stack of cards behind the picture, and how many files are coming along.
                var stack = new Grid();
                for (var i = Math.Min(count, 3) - 1; i >= 1; i--)
                    stack.Children.Add(new Border
                    {
                        Width = img.Width + 4, Height = img.Height + 4, CornerRadius = new CornerRadius(6),
                        Background = SpaceTheme.NavyBrush, BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1.5),
                        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(8 + i * 6, 20 - i * 6, 0, 0),
                    });
                card.HorizontalAlignment = HorizontalAlignment.Left;
                card.VerticalAlignment = VerticalAlignment.Top;
                card.Margin = new Thickness(8, 20, 20, 8);
                stack.Children.Add(card);
                stack.Children.Add(new Border
                {
                    MinWidth = 26, Height = 26, CornerRadius = new CornerRadius(13), Padding = new Thickness(7, 0, 7, 0),
                    Background = SpaceTheme.TerracottaBrush, BorderBrush = SpaceTheme.StarWhiteBrush, BorderThickness = new Thickness(1.5),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 6, 0),
                    Child = new TextBlock
                    {
                        Text = count.ToString(System.Globalization.CultureInfo.CurrentCulture), Foreground = SpaceTheme.StarWhiteBrush,
                        FontFamily = SpaceTheme.UiFont, FontWeight = FontWeights.Bold, FontSize = 13,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    },
                });
                Content = stack;
            }
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
