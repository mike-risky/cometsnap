using System.Globalization;

namespace Cometsnap.Core.Input;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
}

/// <summary>
/// A global hotkey like "PrintScreen" or "Ctrl+Shift+S", stored as text in settings
/// and translated to Win32 <c>RegisterHotKey</c> modifiers + virtual key code.
/// </summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, uint VirtualKey)
{
    private static readonly Dictionary<string, uint> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PrintScreen"] = 0x2C, ["PrtSc"] = 0x2C, ["PrtScn"] = 0x2C, ["Snapshot"] = 0x2C,
        ["Space"] = 0x20, ["Enter"] = 0x0D, ["Tab"] = 0x09, ["Escape"] = 0x1B, ["Esc"] = 0x1B,
        ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Home"] = 0x24, ["End"] = 0x23,
        ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["Pause"] = 0x13, ["ScrollLock"] = 0x91,
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
    };

    public static HotkeyGesture Parse(string text)
    {
        if (!TryParse(text, out var gesture))
            throw new FormatException($"Not a valid hotkey: '{text}'");
        return gesture;
    }

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        var mods = HotkeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= HotkeyModifiers.Control; break;
                case "shift": mods |= HotkeyModifiers.Shift; break;
                case "alt": mods |= HotkeyModifiers.Alt; break;
                case "win": case "windows": mods |= HotkeyModifiers.Win; break;
                default: return false;
            }
        }

        if (!TryParseKey(parts[^1], out var vk)) return false;
        gesture = new HotkeyGesture(mods, vk);
        return true;
    }

    private static bool TryParseKey(string key, out uint vk)
    {
        if (NamedKeys.TryGetValue(key, out vk)) return true;

        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') { vk = c; return true; }
        }

        if (key.Length is >= 2 and <= 3 && (key[0] == 'F' || key[0] == 'f')
            && int.TryParse(key.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var f)
            && f is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + f - 1);
            return true;
        }

        vk = 0;
        return false;
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyName(VirtualKey));
        return string.Join("+", parts);
    }

    private static string KeyName(uint vk)
    {
        if (vk == 0x2C) return "PrintScreen";
        if (vk is >= 'A' and <= 'Z' or >= '0' and <= '9') return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x70 + 1);
        foreach (var (name, code) in NamedKeys)
            if (code == vk) return name;
        return "0x" + vk.ToString("X2", CultureInfo.InvariantCulture);
    }
}
