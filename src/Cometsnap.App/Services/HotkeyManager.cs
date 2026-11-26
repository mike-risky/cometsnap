using System.Windows.Interop;
using Cometsnap.Core.Diagnostics;
using Cometsnap.Core.Input;
using Cometsnap.Core.Native;

namespace Cometsnap.App.Services;

/// <summary>System-wide hotkeys via Win32 RegisterHotKey on the hidden host window.</summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly HwndSource _host;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public HotkeyManager(HwndSource host)
    {
        _host = host;
        _host.AddHook(WndProc);
    }

    /// <summary>Registers a hotkey. Returns false if the text is invalid or another app owns it.</summary>
    public bool TryRegister(string gestureText, Action action)
    {
        if (!HotkeyGesture.TryParse(gestureText, out var gesture))
        {
            Log.Error($"Invalid hotkey in settings: '{gestureText}'");
            return false;
        }

        var id = _nextId++;
        var ok = Win32.RegisterHotKey(_host.Handle, id, (uint)gesture.Modifiers | Win32.MOD_NOREPEAT, gesture.VirtualKey);
        if (ok)
        {
            _actions[id] = action;
            Log.Info($"Hotkey {gesture} registered");
        }
        else
        {
            Log.Info($"Hotkey {gesture} is not available (used by another app or Windows)");
        }
        return ok;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys) Win32.UnregisterHotKey(_host.Handle, id);
        _actions.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            try { action(); }
            catch (Exception ex) { Log.Error("Hotkey action failed", ex); }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _host.RemoveHook(WndProc);
    }
}
