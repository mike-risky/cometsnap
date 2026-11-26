using System.Diagnostics;
using System.IO;
using Cometsnap.Core.Diagnostics;
using Microsoft.Win32;

namespace Cometsnap.Core.Takeover;

/// <summary>A screenshot tool Cometsnap knows how to replace.</summary>
public sealed record KnownCaptureTool(string Name, params string[] ProcessNames);

/// <summary>A tool that is running now and/or starts with Windows.</summary>
public sealed record DetectedCaptureTool(KnownCaptureTool Tool, bool IsRunning, IReadOnlyList<string> StartupEntries)
{
    public string Name => Tool.Name;
}

/// <summary>What currently stands between Print Screen and Cometsnap.</summary>
public sealed record TakeoverReport(bool SnippingToolOwnsPrintScreen, IReadOnlyList<DetectedCaptureTool> Tools)
{
    public bool NothingToReplace => !SnippingToolOwnsPrintScreen && Tools.Count == 0;
}

/// <summary>
/// Finds other screenshot tools (Windows' Snipping Tool binding, Greenshot, ShareX...) and
/// switches them off for the current user. Nothing here needs admin rights, and every change
/// can be undone in Windows Settings or Task Manager.
/// </summary>
public static class CaptureTools
{
    public static readonly IReadOnlyList<KnownCaptureTool> Known = new[]
    {
        new KnownCaptureTool("Greenshot", "Greenshot"),
        new KnownCaptureTool("ShareX", "ShareX"),
        new KnownCaptureTool("Lightshot", "Lightshot"),
        new KnownCaptureTool("Screenpresso", "Screenpresso"),
        new KnownCaptureTool("Snagit", "Snagit32", "SnagitCapture", "SnagitEditor"),
        new KnownCaptureTool("PicPick", "picpick"),
        new KnownCaptureTool("FastStone Capture", "FSCapture"),
        new KnownCaptureTool("Monosnap", "Monosnap"),
        new KnownCaptureTool("Flameshot", "flameshot"),
        new KnownCaptureTool("Gyazo", "Gyazo", "GyStation"),
        new KnownCaptureTool("Snipaste", "Snipaste"),
        new KnownCaptureTool("Shottr", "Shottr"),
    };

    private const string KeyboardKey = @"Control Panel\Keyboard";
    private const string SnippingValue = "PrintScreenKeyForSnippingEnabled";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public const string CometsnapRunName = "Cometsnap";

    /// <summary>
    /// <paramref name="printScreenRegistered"/>: whether Cometsnap managed to register Print Screen.
    /// When the Snipping Tool setting is missing, Windows 11 treats it as on, so a failed
    /// registration on Windows 11 is blamed on it.
    /// </summary>
    public static TakeoverReport Detect(bool printScreenRegistered)
    {
        var snipping = SnippingSetting();
        var snippingOwns = snipping ?? (!printScreenRegistered && IsWindows11());

        var tools = new List<DetectedCaptureTool>();
        var startup = ReadRunEntries();
        foreach (var tool in Known)
        {
            var running = tool.ProcessNames.Any(IsRunning);
            var entries = startup
                .Where(e => tool.ProcessNames.Any(n => e.Command.Contains(n, StringComparison.OrdinalIgnoreCase)))
                .Where(e => !IsDisabledAtStartup(e.Name))
                .Select(e => e.Name)
                .ToList();
            if (running || entries.Count > 0) tools.Add(new DetectedCaptureTool(tool, running, entries));
        }
        return new TakeoverReport(snippingOwns, tools);
    }

    /// <summary>null = the value is not set.</summary>
    public static bool? SnippingSetting()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyboardKey);
            return key?.GetValue(SnippingValue) is int v ? v != 0 : null;
        }
        catch (Exception ex)
        {
            Log.Error("Reading the Snipping Tool setting failed", ex);
            return null;
        }
    }

    /// <summary>Turns off "Use the Print screen key to open screen capture" (Settings > Accessibility > Keyboard).</summary>
    public static bool DisableSnippingPrintScreen()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyboardKey);
            key.SetValue(SnippingValue, 0, RegistryValueKind.DWord);
            Log.Info("Snipping Tool Print Screen binding turned off");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Turning off the Snipping Tool binding failed", ex);
            return false;
        }
    }

    /// <summary>Asks the tool to close, then ends it if it doesn't.</summary>
    public static void Close(KnownCaptureTool tool)
    {
        foreach (var name in tool.ProcessNames)
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(name); }
            catch { continue; }
            foreach (var p in processes)
            {
                try
                {
                    if (p.Id == Environment.ProcessId) continue;
                    if (!p.CloseMainWindow() || !p.WaitForExit(1500)) p.Kill();
                    Log.Info($"Closed {tool.Name} ({p.ProcessName})");
                }
                catch (Exception ex)
                {
                    Log.Error($"Could not close {tool.Name}", ex);
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Disables the tool's "start with Windows" entries the same way Task Manager does
    /// (Startup apps > Disable), so the user can turn them back on there.
    /// </summary>
    public static void DisableStartup(DetectedCaptureTool tool)
    {
        foreach (var name in tool.StartupEntries)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(StartupApprovedKey);
                var value = new byte[12];
                value[0] = 0x03; // disabled
                BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(value, 4);
                key.SetValue(name, value, RegistryValueKind.Binary);
                Log.Info($"Disabled startup entry '{name}' of {tool.Name}");
            }
            catch (Exception ex)
            {
                Log.Error($"Could not disable startup of {tool.Name}", ex);
            }
        }
    }

    public static bool IsCometsnapStartingWithWindows(string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(CometsnapRunName) is string cmd && cmd.Contains(exePath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void SetCometsnapStartsWithWindows(string exePath, bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                key.SetValue(CometsnapRunName, $"\"{exePath}\"");
                using var approved = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, writable: true);
                approved?.DeleteValue(CometsnapRunName, throwOnMissingValue: false);
            }
            else
            {
                key.DeleteValue(CometsnapRunName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not change Cometsnap's start with Windows", ex);
        }
    }

    private static bool IsRunning(string processName)
    {
        try
        {
            var found = Process.GetProcessesByName(processName);
            var any = found.Any(p => p.Id != Environment.ProcessId);
            foreach (var p in found) p.Dispose();
            return any;
        }
        catch { return false; }
    }

    private static List<(string Name, string Command)> ReadRunEntries()
    {
        var list = new List<(string, string)>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key is null) return list;
            foreach (var name in key.GetValueNames())
                if (key.GetValue(name) is string cmd && !string.Equals(name, CometsnapRunName, StringComparison.OrdinalIgnoreCase))
                    list.Add((name, cmd));
        }
        catch (Exception ex)
        {
            Log.Error("Reading startup entries failed", ex);
        }
        return list;
    }

    private static bool IsDisabledAtStartup(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupApprovedKey);
            return key?.GetValue(name) is byte[] { Length: > 0 } v && (v[0] & 1) == 1;
        }
        catch { return false; }
    }

    private static bool IsWindows11() =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
}
