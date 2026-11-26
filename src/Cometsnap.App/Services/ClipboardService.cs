using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Cometsnap.Core.Diagnostics;

namespace Cometsnap.App.Services;

public static class ClipboardService
{
    /// <summary>
    /// Puts the image on the clipboard as a bitmap (Word, Outlook, Paint) and as PNG
    /// (Teams, browsers, Slack keep it lossless that way).
    /// </summary>
    public static bool SetImage(BitmapSource image)
    {
        var data = new DataObject();
        data.SetImage(image);
        try
        {
            var png = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(png);
            png.Position = 0;
            data.SetData("PNG", png, false);
        }
        catch (Exception ex)
        {
            Log.Error("PNG clipboard format failed", ex);
        }
        return Retry(() => Clipboard.SetDataObject(data, copy: true));
    }

    public static bool SetText(string text) => Retry(() => Clipboard.SetText(text));

    /// <summary>The clipboard is a shared resource; another app may hold it for a moment.</summary>
    private static bool Retry(Action action)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                action();
                return true;
            }
            catch (COMException) when (attempt < 4)
            {
                Thread.Sleep(15);
            }
            catch (Exception ex)
            {
                Log.Error("Clipboard write failed", ex);
                return false;
            }
        }
        return false;
    }
}
