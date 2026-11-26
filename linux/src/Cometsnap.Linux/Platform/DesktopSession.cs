namespace Cometsnap.Linux.Platform;

/// <summary>What kind of Linux desktop we run in.</summary>
public static class DesktopSession
{
    /// <summary>True on a Wayland session (Avalonia itself still runs through XWayland).</summary>
    public static bool IsWayland { get; } =
        string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    /// <summary>Lower-case desktop names from XDG_CURRENT_DESKTOP, e.g. "ubuntu:gnome" → ["ubuntu", "gnome"].</summary>
    public static IReadOnlyList<string> Desktops { get; } =
        (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "")
            .ToLowerInvariant().Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool IsGnome => Desktops.Any(d => d is "gnome" or "unity" or "budgie" or "pop" or "zorin");
    public static bool IsKde => Desktops.Contains("kde");
    public static bool IsCinnamon => Desktops.Any(d => d is "x-cinnamon" or "cinnamon");
    public static bool IsXfce => Desktops.Contains("xfce");
    public static bool IsMate => Desktops.Contains("mate");

    public static string Describe() =>
        $"{(IsWayland ? "Wayland" : "X11")}, desktop '{Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")}'";

    /// <summary>Finds a program on PATH.</summary>
    public static string? Which(string program)
    {
        if (program.Contains('/')) return File.Exists(program) ? program : null;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin").Split(':'))
        {
            if (dir.Length == 0) continue;
            var p = Path.Combine(dir, program);
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
