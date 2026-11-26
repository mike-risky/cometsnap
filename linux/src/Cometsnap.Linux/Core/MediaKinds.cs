using System.IO;

namespace Cometsnap.Linux.Core;

/// <summary>Which files in the captures folder show up in the recent list, and which are recordings.</summary>
public static class MediaKinds
{
    public static bool IsListed(string path) =>
        HasExtension(path, ".png") || IsRecording(path);

    /// <summary>Screen recordings: MP4 videos and animated GIFs.</summary>
    public static bool IsRecording(string path) => IsVideo(path) || IsGif(path);

    public static bool IsVideo(string path) => HasExtension(path, ".mp4");
    public static bool IsGif(string path) => HasExtension(path, ".gif");

    private static bool HasExtension(string path, string ext) =>
        string.Equals(Path.GetExtension(path), ext, StringComparison.OrdinalIgnoreCase);
}
