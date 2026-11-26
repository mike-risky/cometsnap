using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Cometsnap.Core.Diagnostics;
using Cometsnap.Core.Native;

namespace Cometsnap.App.Services;

/// <summary>Plays the capture "whoosh" straight from memory (no files, no extra dependencies).</summary>
public sealed class SoundService : IDisposable
{
    private GCHandle _whoosh;

    public SoundService()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/whoosh.wav"));
            if (info is null) return;
            using var ms = new MemoryStream();
            info.Stream.CopyTo(ms);
            // PlaySound reads the buffer asynchronously: keep it pinned for the app's lifetime.
            _whoosh = GCHandle.Alloc(ms.ToArray(), GCHandleType.Pinned);
        }
        catch (Exception ex)
        {
            Log.Error("Could not load sounds", ex);
        }
    }

    public void Whoosh()
    {
        if (!_whoosh.IsAllocated) return;
        Win32.PlaySound(_whoosh.AddrOfPinnedObject(), IntPtr.Zero, Win32.SND_MEMORY | Win32.SND_ASYNC | Win32.SND_NODEFAULT);
    }

    public void Dispose()
    {
        Win32.PlaySound(IntPtr.Zero, IntPtr.Zero, 0);
        if (_whoosh.IsAllocated) _whoosh.Free();
    }
}
