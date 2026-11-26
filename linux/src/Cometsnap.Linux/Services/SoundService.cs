using System.Diagnostics;
using Cometsnap.Linux.Platform;

namespace Cometsnap.Linux.Services;

/// <summary>A soft click on capture, through the desktop's sound theme (canberra / PipeWire). Off by default.</summary>
public sealed class SoundService
{
    private readonly Func<bool> _enabled;

    public SoundService(Func<bool> enabled) => _enabled = enabled;

    public void Click()
    {
        if (!_enabled()) return;
        try
        {
            if (DesktopSession.Which("canberra-gtk-play") is { } c)
                Process.Start(new ProcessStartInfo(c, "-i screen-capture") { UseShellExecute = false });
            else if (DesktopSession.Which("pw-play") is { } pw && File.Exists("/usr/share/sounds/freedesktop/stereo/screen-capture.oga"))
                Process.Start(new ProcessStartInfo(pw, "/usr/share/sounds/freedesktop/stereo/screen-capture.oga") { UseShellExecute = false });
        }
        catch
        {
            // no sound is fine
        }
    }
}
