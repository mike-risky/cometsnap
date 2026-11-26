using System.IO;

namespace Cometsnap.Core.Storage;

/// <summary>
/// Where the editor keeps what it needs to re-open a capture with its shapes still editable:
/// the untouched original and the annotations, in a hidden ".cometsnap" folder beside the image.
/// </summary>
public static class EditSidecar
{
    public const string FolderName = ".cometsnap";

    public static string FolderFor(string imagePath) =>
        Path.Combine(Path.GetDirectoryName(imagePath) ?? "", FolderName);

    public static string OriginalFor(string imagePath) =>
        Path.Combine(FolderFor(imagePath), Path.GetFileNameWithoutExtension(imagePath) + ".original.png");

    public static string AnnotationsFor(string imagePath) =>
        Path.Combine(FolderFor(imagePath), Path.GetFileNameWithoutExtension(imagePath) + ".annotations.json");

    public static void EnsureFolder(string imagePath)
    {
        var folder = Directory.CreateDirectory(FolderFor(imagePath));
        if (!folder.Attributes.HasFlag(FileAttributes.Hidden))
            folder.Attributes |= FileAttributes.Hidden;
    }

    /// <summary>Removes the sidecar files of a deleted capture.</summary>
    public static void DeleteFor(string imagePath)
    {
        foreach (var f in new[] { OriginalFor(imagePath), AnnotationsFor(imagePath) })
        {
            try { if (File.Exists(f)) File.Delete(f); }
            catch { /* best effort */ }
        }
    }

    public static bool IsSidecarFolder(string folder) =>
        string.Equals(Path.GetFileName(folder.TrimEnd('\\', '/')), FolderName, StringComparison.OrdinalIgnoreCase);
}
