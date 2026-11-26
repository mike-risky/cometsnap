using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cometsnap.Core.Settings;

public enum LoupeCorner
{
    BottomLeft,
    BottomRight,
    TopLeft,
    TopRight,
    FollowCursor,
}

/// <summary>User settings, stored as JSON. Unknown/missing values fall back to defaults.</summary>
public sealed class AppSettings
{
    /// <summary>Main capture hotkey (region / window).</summary>
    public string CaptureHotkey { get; set; } = "PrintScreen";

    /// <summary>Used when the main hotkey is taken by another app (or Windows' Snipping Tool).</summary>
    public string CaptureHotkeyFallback { get; set; } = "Ctrl+Shift+S";

    public string FullscreenHotkey { get; set; } = "Ctrl+PrintScreen";
    public string ActiveWindowHotkey { get; set; } = "Alt+PrintScreen";

    /// <summary>Folder for captures. Empty = default (Pictures\Cometsnap or beside the exe in portable mode).</summary>
    public string CapturesFolder { get; set; } = "";

    public LoupeCorner LoupeCorner { get; set; } = LoupeCorner.BottomLeft;
    public bool WindowDetection { get; set; } = true;
    public bool PlaySounds { get; set; } = true;
    public bool ShowCometAnimation { get; set; } = true;
    public bool CopyToClipboardOnCapture { get; set; } = true;
    public bool AutoSave { get; set; } = true;
    public int RecentCount { get; set; } = 30;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            // A broken settings file must never stop the app from starting.
        }
        return new AppSettings();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }
}
