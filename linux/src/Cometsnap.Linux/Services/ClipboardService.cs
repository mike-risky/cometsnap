using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Platform;

namespace Cometsnap.Linux.Services;

/// <summary>
/// Puts images and files on the clipboard. On Wayland, wl-copy (wl-clipboard) is used when present,
/// because it keeps serving the data natively; otherwise the X11 clipboard (XWayland is bridged by the
/// compositor). Cometsnap stays running in the tray, so the clipboard content stays available.
/// </summary>
public sealed class ClipboardService
{
    private readonly Func<IClipboard?> _clipboard;

    public ClipboardService(Func<IClipboard?> clipboard) => _clipboard = clipboard;

    public async Task<bool> SetPngAsync(byte[] png)
    {
        try
        {
            if (DesktopSession.IsWayland && DesktopSession.Which("wl-copy") is { } wl)
            {
                if (await PipeAsync(wl, new[] { "--type", "image/png" }, png)) return true;
            }
            var cb = _clipboard();
            if (cb is null) return false;
            var item = new DataTransferItem();
            item.Set(DataFormat.CreateBytesPlatformFormat("image/png"), png);
            var data = new DataTransfer();
            data.Add(item);
            await cb.SetDataAsync(data);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Copying the image failed", ex);
            return false;
        }
    }

    /// <summary>Copies files (recordings): paste them into a file manager, chat or mail.</summary>
    public async Task<bool> SetFilesAsync(IReadOnlyList<string> paths)
    {
        try
        {
            var uris = string.Join("\r\n", paths.Select(p => new Uri(p).AbsoluteUri));
            if (DesktopSession.IsWayland && DesktopSession.Which("wl-copy") is { } wl)
            {
                if (await PipeAsync(wl, new[] { "--type", "text/uri-list" }, System.Text.Encoding.UTF8.GetBytes(uris + "\r\n"))) return true;
            }
            var cb = _clipboard();
            if (cb is null) return false;
            var item = new DataTransferItem();
            item.Set(DataFormat.CreateBytesPlatformFormat("text/uri-list"), System.Text.Encoding.UTF8.GetBytes(uris + "\r\n"));
            item.Set(DataFormat.CreateBytesPlatformFormat("x-special/gnome-copied-files"), System.Text.Encoding.UTF8.GetBytes("copy\n" + uris.Replace("\r\n", "\n")));
            item.SetText(string.Join("\n", paths));
            var data = new DataTransfer();
            data.Add(item);
            await cb.SetDataAsync(data);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Copying files failed", ex);
            return false;
        }
    }

    public async Task SetTextAsync(string text)
    {
        try
        {
            if (_clipboard() is { } cb) await cb.SetTextAsync(text);
        }
        catch (Exception ex)
        {
            Log.Error("Copying text failed", ex);
        }
    }

    private static async Task<bool> PipeAsync(string program, IEnumerable<string> args, byte[] data)
    {
        var psi = new ProcessStartInfo(program) { RedirectStandardInput = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        if (p is null) return false;
        await p.StandardInput.BaseStream.WriteAsync(data);
        p.StandardInput.Close();
        // wl-copy forks a server and returns at once.
        var exited = await Task.WhenAny(p.WaitForExitAsync(), Task.Delay(2000));
        return p.HasExited && p.ExitCode == 0;
    }
}
