using System.IO;

namespace Cometsnap.Core.Styling;

/// <summary>What goes behind a styled capture.</summary>
public enum StyleBackdrop
{
    /// <summary>Transparent: just the capture (and its shadow or glow).</summary>
    None,
    /// <summary>A diagonal gradient in the capture colors.</summary>
    Gradient,
    /// <summary>The capture itself, blurred and enlarged, like frosted glass.</summary>
    Blur,
    /// <summary>Deep space: navy night with a few stars.</summary>
    Space,
}

/// <summary>
/// A "share-ready" look for captures: rounded corners, a soft shadow, a glowing outline and a
/// backdrop. Applied to one capture from the Copied card, or to every capture when the user turns
/// that on (off by default, so capture stays raw and instant).
/// </summary>
public sealed record SnapStyle(string Id, string Name, bool Corners, bool Shadow, bool Glow, StyleBackdrop Backdrop);

public static class SnapStyles
{
    public const string DefaultId = "clean";

    // Names are English; the UI translates them with L.T.
    public static readonly IReadOnlyList<SnapStyle> All = new[]
    {
        new SnapStyle("clean", "Clean", Corners: true, Shadow: true, Glow: false, StyleBackdrop.None),
        new SnapStyle("showcase", "Showcase", Corners: true, Shadow: true, Glow: false, StyleBackdrop.Gradient),
        new SnapStyle("frosted", "Frosted", Corners: true, Shadow: true, Glow: false, StyleBackdrop.Blur),
        new SnapStyle("comet", "Comet", Corners: true, Shadow: false, Glow: true, StyleBackdrop.Space),
        new SnapStyle("laser", "Laser", Corners: true, Shadow: false, Glow: true, StyleBackdrop.None),
    };

    public static SnapStyle Default => All[0];

    /// <summary>The style with this id, or the default (Clean) for empty or unknown ids.</summary>
    public static SnapStyle Get(string? id) =>
        All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;

    /// <summary>Name of the file that holds the styled copy of a capture: "snap_1.png" → "snap_1-styled.png".</summary>
    public static string StyledPath(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        return Path.Combine(dir, name + "-styled" + ext);
    }
}
