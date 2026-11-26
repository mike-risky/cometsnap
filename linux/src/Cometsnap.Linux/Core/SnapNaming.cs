using System.IO;
using System.Globalization;

namespace Cometsnap.Linux.Core;

/// <summary>
/// Naming scheme for captures: <c>&lt;root&gt;\2026-11\snap_2026-11-26_08-56-12.png</c>.
/// </summary>
public static class SnapNaming
{
    public const string Extension = ".png";
    public const string SidecarSuffix = ".cometsnap.json";

    public static string MonthFolder(string root, DateTime when) =>
        Path.Combine(root, when.ToString("yyyy-MM", CultureInfo.InvariantCulture));

    public static string BaseName(DateTime when) =>
        "snap_" + when.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// Returns a full path that does not exist yet. Several captures in the same
    /// second get a numeric suffix: snap_..._08-56-12_2.png, _3, ...
    /// </summary>
    public static string NextFreePath(string root, DateTime when, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        var folder = MonthFolder(root, when);
        var baseName = BaseName(when);
        var candidate = Path.Combine(folder, baseName + Extension);
        for (var i = 2; exists(candidate); i++)
            candidate = Path.Combine(folder, $"{baseName}_{i}{Extension}");
        return candidate;
    }

    public static string SidecarFor(string imagePath) =>
        Path.ChangeExtension(imagePath, null) + SidecarSuffix;
}
