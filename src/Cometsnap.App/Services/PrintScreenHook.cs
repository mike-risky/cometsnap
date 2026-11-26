using System.Runtime.InteropServices;
using System.Windows.Threading;
using Cometsnap.Core.Diagnostics;
using Cometsnap.Core.Native;

namespace Cometsnap.App.Services;

/// <summary>
/// Owns the Print Screen key with a low-level keyboard hook. The hook sees the key before
/// any registered hotkey does, so this works even when Windows' Snipping Tool, OneDrive or
/// another screenshot tool already claimed Print Screen. Ctrl and Alt variants are passed
/// through as separate actions; the key is swallowed so the other tool never sees it.
/// </summary>
public sealed class PrintScreenHook : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action _plain;
    private readonly Action _ctrl;
    private readonly Action _alt;
    private readonly Win32.LowLevelKeyboardProc _proc; // kept alive: the hook holds a raw pointer to it
    private IntPtr _hook;
    private bool _down;

    public PrintScreenHook(Dispatcher dispatcher, Action plain, Action ctrl, Action alt)
    {
        _dispatcher = dispatcher;
        _plain = plain;
        _ctrl = ctrl;
        _alt = alt;
        _proc = HookProc;
    }

    /// <summary>Optional: Shift+Print Screen (start / stop a video recording).</summary>
    public Action? Shift { get; set; }

    /// <summary>Optional: Ctrl+Shift+Print Screen (start / stop a GIF recording).</summary>
    public Action? CtrlShift { get; set; }

    public bool IsInstalled => _hook != IntPtr.Zero;

    public bool Install()
    {
        if (_hook != IntPtr.Zero) return true;
        _hook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _proc, Win32.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            Log.Error($"Print Screen hook failed (error {Marshal.GetLastWin32Error()})");
        else
            Log.Info("Print Screen hook installed");
        return _hook != IntPtr.Zero;
    }

    public void Uninstall()
    {
        if (_hook == IntPtr.Zero) return;
        Win32.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
            if (info.vkCode == Win32.VK_SNAPSHOT && (info.flags & Win32.LLKHF_INJECTED) == 0)
            {
                var msg = wParam.ToInt32();
                var isDown = msg is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN;
                var isUp = msg is Win32.WM_KEYUP or Win32.WM_SYSKEYUP;

                // Win+Print Screen stays Windows' own "save screenshot" shortcut.
                if (Win32.IsKeyDown(Win32.VK_LWIN) || Win32.IsKeyDown(Win32.VK_RWIN))
                    return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);

                if (isDown && !_down)
                {
                    _down = true;
                    var shift = Win32.IsKeyDown(Win32.VK_SHIFT);
                    var ctrl = Win32.IsKeyDown(Win32.VK_CONTROL);
                    var action = shift && ctrl && CtrlShift is not null ? CtrlShift
                        : shift && !ctrl && Shift is not null ? Shift
                        : ctrl ? _ctrl
                        : Win32.IsKeyDown(Win32.VK_MENU) ? _alt
                        : _plain;
                    // Never do real work inside the hook: Windows drops slow hooks.
                    _dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
                    {
                        try { action(); }
                        catch (Exception ex) { Log.Error("Print Screen action failed", ex); }
                    });
                }
                else if (isUp)
                {
                    _down = false;
                }
                return (IntPtr)1; // swallow it
            }
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
