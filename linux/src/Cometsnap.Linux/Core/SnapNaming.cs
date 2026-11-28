using System.IO;
using System.Globalization;

using Cometsnap.Core.Storage;

namespace Cometsnap.Linux.Core;

/// <summary>
/// Naming scheme for captures: <c>&lt;root&gt;\2026-11\snap_2026-11-26_08-56-12.png</c> by default,
/// or whatever the user's <see cref="FileNamePattern"/> says.
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
    public static string NextFreePath(string root, DateTime when, Func<string, bool>? exists = null) =>
        NextFreePath(root, when, FileNamePattern.Default, Extension, 1, exists);

    /// <summary>Same, with the user's name pattern and image format (".png" or ".jpg").</summary>
    public static string NextFreePath(string root, DateTime when, string? pattern, string extension, int counter = 1, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        var relative = FileNamePattern.Expand(pattern, when, counter);
        var stem = Path.Combine(root, relative);
        var candidate = stem + extension;
        for (var i = 2; exists(candidate); i++)
            candidate = $"{stem}_{i}{extension}";
        return candidate;
    }

    /// <summary>The file name (no folders, no extension) the pattern gives, for Save as dialogs.</summary>
    public static string SuggestedName(string? pattern, DateTime when, int counter = 1) =>
        Path.GetFileName(FileNamePattern.Expand(pattern, when, counter));

    /// <summary>".jpg" for "jpg"/"jpeg", otherwise ".png".</summary>
    public static string ExtensionFor(string? format) =>
        string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase) || string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : Extension;

    public static string SidecarFor(string imagePath) =>
        Path.ChangeExtension(imagePath, null) + SidecarSuffix;
}
