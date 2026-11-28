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

    /// <summary>Scrolling capture: pick a window, Cometsnap scrolls it and glues one tall image.</summary>
    public string ScrollingHotkey { get; set; } = "Ctrl+Alt+PrintScreen";

    /// <summary>Color picker: the loupe opens, a click copies the color under the cursor as #RRGGBB.</summary>
    public string ColorHotkey { get; set; } = "Win+Shift+C";

    /// <summary>Copy text (OCR): drag over text on the screen and the text goes on the clipboard.</summary>
    public string TextHotkey { get; set; } = "Ctrl+Alt+T";

    /// <summary>Folder for captures. Empty = default (Pictures\Cometsnap or beside the exe in portable mode).</summary>
    public string CapturesFolder { get; set; } = "";

    /// <summary>
    /// Name of each saved capture, with tokens ({date}, {time}, {year}, {month}, {day}, {hour},
    /// {minute}, {second}, {counter}); "/" makes subfolders. See <see cref="Storage.FileNamePattern"/>.
    /// </summary>
    public string FileNamePattern { get; set; } = Storage.FileNamePattern.Default;

    /// <summary>Format of saved captures: "png" (sharp, lossless) or "jpg" (smaller files).</summary>
    public string ImageFormat { get; set; } = "png";

    /// <summary>After each capture, ask where to save it (the clipboard copy still happens first).</summary>
    public bool AskWhereToSave { get; set; }

    /// <summary>Open every capture in the editor right away.</summary>
    public bool OpenEditorAfterCapture { get; set; }

    /// <summary>Last number used for {counter} in <see cref="FileNamePattern"/>.</summary>
    public int NameCounter { get; set; }

    /// <summary>
    /// Capture the moment the mouse is released (click a window or drag a box). When off,
    /// the selection stays open with the quick bar for annotating on the frozen screen.
    /// </summary>
    public bool InstantCapture { get; set; } = true;

    /// <summary>After a capture, show a small card in the corner with an "Edit" button.</summary>
    public bool ShowEditCard { get; set; } = true;

    /// <summary>Look of the capture screen: a <see cref="CaptureThemes"/> id ("blue" is the classic look).</summary>
    public string CaptureTheme { get; set; } = CaptureThemes.DefaultId;

    /// <summary>Share-ready look, built in the Style studio (corners, border, shadow, glow, backdrop).</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(Styling.SnapStyleJsonConverter))]
    public Styling.SnapStyle SnapStyle { get; set; } = new();

    /// <summary>Put the styled capture on the clipboard. Off: the clipboard gets the raw capture.</summary>
    public bool StyleOnCopy { get; set; }

    /// <summary>Save the styled capture (the file you drag out of the tray). Off: the file is the raw capture.</summary>
    public bool StyleOnExport { get; set; }

    /// <summary>Show a ✨ Style button on the Copied card, to style just that capture.</summary>
    public bool StylePerShot { get; set; } = true;

    public LoupeCorner LoupeCorner { get; set; } = LoupeCorner.BottomLeft;
    public bool WindowDetection { get; set; } = true;
    /// <summary>A soft click on capture. Off by default.</summary>
    public bool PlaySounds { get; set; } = false;
    public bool ShowCometAnimation { get; set; } = true;
    public bool CopyToClipboardOnCapture { get; set; } = true;
    public bool AutoSave { get; set; } = true;
    public int RecentCount { get; set; } = 30;

    // ---- Recording ("Transmissions")

    /// <summary>Start recording a region (pick it like a capture). Pressed again while recording: stop.</summary>
    public string RecordHotkey { get; set; } = "Shift+PrintScreen";

    /// <summary>Same as <see cref="RecordHotkey"/>, but records an animated GIF.</summary>
    public string RecordGifHotkey { get; set; } = "Ctrl+Shift+PrintScreen";

    public int RecordFramesPerSecond { get; set; } = 30;
    public int RecordGifFramesPerSecond { get; set; } = 15;
    /// <summary>Record the sound playing on the PC.</summary>
    public bool RecordSystemAudio { get; set; } = true;
    /// <summary>Record the default microphone. Off by default.</summary>
    public bool RecordMicrophone { get; set; }
    public bool RecordShowCursor { get; set; } = true;
    /// <summary>After a recording, put the video file on the clipboard (paste it in Teams, Slack, mail...).</summary>
    public bool RecordCopyToClipboard { get; set; } = true;

    /// <summary>
    /// Own Print Screen with a low-level keyboard hook, so it works even when Windows'
    /// Snipping Tool or another screenshot tool registered it first.
    /// </summary>
    public bool TakeOverPrintScreen { get; set; }

    /// <summary>
    /// Start Cometsnap when you log in to Windows (HKCU Run key). On by default; kept pointing
    /// at the exe you last launched, so a new download keeps auto-starting.
    /// </summary>
    public bool StartWithWindows { get; set; } = true;

    /// <summary>The "Replace your old screen capture" offer was answered (either way).</summary>
    public bool ReplaceOfferAnswered { get; set; }

    /// <summary>App language: "en", "fr", "de", "it", or empty to follow Windows (English otherwise).</summary>
    public string Language { get; set; } = "";

    // ---- playful touches (all discreet, none of them ever delays a capture)

    /// <summary>A tiny meteor shower for snap milestones, and a monthly "you captured N snaps" card.</summary>
    public bool Celebrations { get; set; } = true;

    /// <summary>A snowy comet in December and a pumpkin comet at Halloween (tray icon and panel).</summary>
    public bool SeasonalSkins { get; set; } = true;

    /// <summary>Editor: join numbered steps with faint star lines, like a constellation.</summary>
    public bool ConstellationSteps { get; set; }

    /// <summary>Screenshots taken so far (for milestones). Seeded once from the captures folder.</summary>
    public int SnapCount { get; set; }
    public bool SnapCountSeeded { get; set; }

    /// <summary>The month being counted ("yyyy-MM") and its snaps so far.</summary>
    public string SnapMonth { get; set; } = "";
    public int SnapMonthCount { get; set; }

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
