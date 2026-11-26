using System.Runtime.InteropServices;
using System.Windows.Interop;
using Cometsnap.Core.Diagnostics;
using Cometsnap.Core.Native;

namespace Cometsnap.App.Tray;

/// <summary>
/// System tray icon built directly on Shell_NotifyIcon (version 4), so left click,
/// double click and right click are all ours to handle instantly.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage = Win32.WM_USER + 0x42;
    private const uint IconId = 1;

    private readonly HwndSource _host;
    private readonly uint _taskbarCreatedMessage;
    private readonly IntPtr _icon;
    private readonly bool _ownsIcon;
    private string _tooltip;
    private bool _added;

    /// <summary>Left click (or Enter/Space on the focused icon).</summary>
    public event Action? Activated;
    public event Action? DoubleClicked;
    /// <summary>Right click / context-menu key. Arguments are physical screen coordinates.</summary>
    public event Action<int, int>? ContextMenuRequested;

    public TrayIcon(HwndSource host, string tooltip)
    {
        _host = host;
        _tooltip = tooltip;
        _taskbarCreatedMessage = Win32.RegisterWindowMessage("TaskbarCreated");
        (_icon, _ownsIcon) = LoadAppIcon();
        _host.AddHook(WndProc);
        Add();
    }

    public string Tooltip
    {
        get => _tooltip;
        set
        {
            _tooltip = value;
            if (!_added) return;
            var data = CreateData(Win32.NIF_TIP | Win32.NIF_SHOWTIP);
            Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref data);
        }
    }

    /// <summary>Screen rectangle of the icon in physical pixels, if Windows can tell us.</summary>
    public RECT? GetIconRect()
    {
        var id = new Win32.NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<Win32.NOTIFYICONIDENTIFIER>(),
            hWnd = _host.Handle,
            uID = IconId,
        };
        return Win32.Shell_NotifyIconGetRect(ref id, out var rect) == 0 ? rect : null;
    }

    private void Add()
    {
        var data = CreateData(Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP | Win32.NIF_SHOWTIP);
        _added = Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref data);
        if (!_added)
        {
            Log.Error("Shell_NotifyIcon(NIM_ADD) failed");
            return;
        }
        data.uVersion = Win32.NOTIFYICON_VERSION_4;
        Win32.Shell_NotifyIcon(Win32.NIM_SETVERSION, ref data);
    }

    private Win32.NOTIFYICONDATAW CreateData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<Win32.NOTIFYICONDATAW>(),
        hWnd = _host.Handle,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tooltip.Length > 127 ? _tooltip[..127] : _tooltip,
        szInfo = "",
        szInfoTitle = "",
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            // NOTIFYICON_VERSION_4: LOWORD(lParam) = event, wParam = anchor x/y.
            switch (Win32.LOWORD(lParam))
            {
                case Win32.NIN_SELECT:
                case Win32.NIN_KEYSELECT:
                    Activated?.Invoke();
                    break;
                case Win32.WM_LBUTTONDBLCLK:
                    DoubleClicked?.Invoke();
                    break;
                case Win32.WM_CONTEXTMENU:
                    ContextMenuRequested?.Invoke(Win32.LOWORD(wParam), Win32.HIWORD(wParam));
                    break;
            }
            handled = true;
        }
        else if (msg == (int)_taskbarCreatedMessage)
        {
            // Explorer restarted: our icon is gone, put it back.
            _added = false;
            Add();
        }
        return IntPtr.Zero;
    }

    private static (IntPtr icon, bool owns) LoadAppIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is not null)
            {
                var small = new IntPtr[1];
                if (Win32.ExtractIconEx(exe, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero)
                    return (small[0], true);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not load tray icon", ex);
        }
        return (Win32.LoadIcon(IntPtr.Zero, Win32.IDI_APPLICATION), false);
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = CreateData(0);
            Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref data);
            _added = false;
        }
        _host.RemoveHook(WndProc);
        if (_ownsIcon) Win32.DestroyIcon(_icon);
    }
}
