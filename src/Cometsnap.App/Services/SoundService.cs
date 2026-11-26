using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Cometsnap.Core.Diagnostics;
using Cometsnap.Core.Native;

namespace Cometsnap.App.Services;

/// <summary>Plays the soft capture click straight from memory (no files, no extra dependencies).</summary>
public sealed class SoundService : IDisposable
{
    private GCHandle _snap;

    public SoundService()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/snap.wav"));
            if (info is null) return;
            using var ms = new MemoryStream();
            info.Stream.CopyTo(ms);
            // PlaySound reads the buffer asynchronously: keep it pinned for the app's lifetime.
            _snap = GCHandle.Alloc(ms.ToArray(), GCHandleType.Pinned);
        }
        catch (Exception ex)
        {
            Log.Error("Could not load sounds", ex);
        }
    }

    public void Snap()
    {
        if (!_snap.IsAllocated) return;
        Win32.PlaySound(_snap.AddrOfPinnedObject(), IntPtr.Zero, Win32.SND_MEMORY | Win32.SND_ASYNC | Win32.SND_NODEFAULT);
    }

    public void Dispose()
    {
        Win32.PlaySound(IntPtr.Zero, IntPtr.Zero, 0);
        if (_snap.IsAllocated) _snap.Free();
    }
}
