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
    public const int CurrentVersion = 2;

    /// <summary>Lets newer versions adjust settings saved by older ones.</summary>
    public int SettingsVersion { get; set; } = CurrentVersion;

    /// <summary>Main capture hotkey (region / window).</summary>
    public string CaptureHotkey { get; set; } = "PrintScreen";

    /// <summary>Used when the main hotkey is taken by another app (or Windows' Snipping Tool).</summary>
    public string CaptureHotkeyFallback { get; set; } = "Ctrl+Shift+S";

    public string FullscreenHotkey { get; set; } = "Ctrl+PrintScreen";
    public string ActiveWindowHotkey { get; set; } = "Alt+PrintScreen";

    /// <summary>Folder for captures. Empty = default (Pictures\Cometsnap or beside the exe in portable mode).</summary>
    public string CapturesFolder { get; set; } = "";

    /// <summary>
    /// Capture the moment the mouse is released (click a window or drag a box). When off,
    /// the selection stays open with the quick bar for annotating on the frozen screen.
    /// </summary>
    public bool InstantCapture { get; set; } = true;

    /// <summary>After a capture, show a small card in the corner with an "Edit" button.</summary>
    public bool ShowEditCard { get; set; } = true;

    public LoupeCorner LoupeCorner { get; set; } = LoupeCorner.BottomLeft;
    public bool WindowDetection { get; set; } = true;
    /// <summary>A soft click on capture. Off by default.</summary>
    public bool PlaySounds { get; set; } = false;
    public bool ShowCometAnimation { get; set; } = true;
    public bool CopyToClipboardOnCapture { get; set; } = true;
    public bool AutoSave { get; set; } = true;
    public int RecentCount { get; set; } = 30;

    /// <summary>
    /// Own Print Screen with a low-level keyboard hook, so it works even when Windows'
    /// Snipping Tool or another screenshot tool registered it first.
    /// </summary>
    public bool TakeOverPrintScreen { get; set; }

    /// <summary>The "Replace your old screen capture" offer was answered (either way).</summary>
    public bool ReplaceOfferAnswered { get; set; }

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
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                if (!json.Contains("\"SettingsVersion\"", StringComparison.Ordinal)) settings.SettingsVersion = 1;
                settings.Upgrade();
                return settings;
            }
        }
        catch (Exception)
        {
            // A broken settings file must never stop the app from starting.
        }
        return new AppSettings();
    }

    /// <summary>Applies changes of defaults to settings saved by an older version.</summary>
    public bool Upgrade()
    {
        if (SettingsVersion >= CurrentVersion) return false;
        if (SettingsVersion < 2) PlaySounds = false; // the old whoosh was too loud: start quiet
        SettingsVersion = CurrentVersion;
        return true;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }
}
