namespace Cometsnap.Linux.Core;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Shift = 1,
    Ctrl = 4,
    Alt = 8,
    Super = 64,
}

/// <summary>A global shortcut like "Ctrl+Shift+S" or "PrintScreen", with its X11 keysym.</summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, string Key, uint KeySym)
{
    private static readonly Dictionary<string, uint> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PrintScreen"] = 0xff61, ["Print"] = 0xff61, ["PrtSc"] = 0xff61,
        ["Pause"] = 0xff13, ["ScrollLock"] = 0xff14, ["Insert"] = 0xff63, ["Home"] = 0xff50, ["End"] = 0xff57,
        ["Space"] = 0x20, ["Enter"] = 0xff0d, ["Tab"] = 0xff09, ["Escape"] = 0xff1b, ["Esc"] = 0xff1b,
    };

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var mods = HotkeyModifiers.None;
        string? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control" or "primary": mods |= HotkeyModifiers.Ctrl; break;
                case "shift": mods |= HotkeyModifiers.Shift; break;
                case "alt": mods |= HotkeyModifiers.Alt; break;
                case "super" or "win" or "meta" or "logo": mods |= HotkeyModifiers.Super; break;
                default:
                    if (key is not null) return false;
                    key = raw;
                    break;
            }
        }
        if (key is null) return false;

        uint sym;
        if (Named.TryGetValue(key, out var named)) sym = named;
        else if (key.Length == 1 && char.IsLetterOrDigit(key[0])) sym = char.ToLowerInvariant(key[0]);
        else if (key.Length >= 2 && (key[0] is 'F' or 'f') && int.TryParse(key[1..], out var f) && f is >= 1 and <= 24) sym = (uint)(0xffbe + f - 1);
        else return false;

        gesture = new HotkeyGesture(mods, key, sym);
        return true;
    }

    /// <summary>GTK / GNOME accelerator syntax, e.g. "&lt;Control&gt;&lt;Shift&gt;Print".</summary>
    public string ToGtkAccelerator()
    {
        var s = "";
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) s += "<Control>";
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) s += "<Shift>";
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) s += "<Alt>";
        if (Modifiers.HasFlag(HotkeyModifiers.Super)) s += "<Super>";
        return s + (KeySym == 0xff61 ? "Print" : Key.Length == 1 ? Key.ToLowerInvariant() : Key);
    }

    /// <summary>Shortcut text in the xdg-desktop-portal "shortcuts" trigger format, e.g. "CTRL+SHIFT+Print".</summary>
    public string ToPortalTrigger()
    {
        var s = "";
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) s += "CTRL+";
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) s += "SHIFT+";
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) s += "ALT+";
        if (Modifiers.HasFlag(HotkeyModifiers.Super)) s += "LOGO+";
        return s + (KeySym == 0xff61 ? "Print" : Key.Length == 1 ? Key.ToLowerInvariant() : Key);
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Super)) parts.Add("Super");
        parts.Add(KeySym == 0xff61 ? "Print Screen" : Key.Length == 1 ? Key.ToUpperInvariant() : Key);
        return string.Join("+", parts);
    }
}
