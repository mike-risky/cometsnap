using System.Windows.Interop;

namespace Cometsnap.App;

/// <summary>
/// An invisible Win32 window that receives tray-icon callbacks and global hotkeys.
/// It is a real top-level window (not message-only) so it also hears the
/// "TaskbarCreated" broadcast when Explorer restarts.
/// </summary>
public static class HiddenHost
{
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    public static HwndSource Create()
    {
        var parameters = new HwndSourceParameters("CometsnapHost")
        {
            Width = 0,
            Height = 0,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = WS_POPUP,
            ExtendedWindowStyle = WS_EX_TOOLWINDOW,
        };
        return new HwndSource(parameters);
    }
}
