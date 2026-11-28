using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cometsnap.Linux.Core;

public enum LoupeCorner
{
    BottomLeft,
    BottomRight,
    TopLeft,
    TopRight,
    FollowCursor,
}

/// <summary>User settings, stored as JSON in ~/.config/cometsnap. Unknown/missing values fall back to defaults.</summary>
public sealed class AppSettings
{
    public const int CurrentVersion = 1;

    public int SettingsVersion { get; set; } = CurrentVersion;

    /// <summary>Main capture hotkey (region / window).</summary>
    public string CaptureHotkey { get; set; } = "PrintScreen";

    /// <summary>Used when the main hotkey is taken by another app (the desktop's own screenshot tool).</summary>
    public string CaptureHotkeyFallback { get; set; } = "Ctrl+Shift+S";

    public string FullscreenHotkey { get; set; } = "Ctrl+PrintScreen";

    /// <summary>Scrolling capture (X11): Cometsnap scrolls the picked window and glues one tall image.</summary>
    public string ScrollingHotkey { get; set; } = "Ctrl+Alt+PrintScreen";

    /// <summary>Color picker: the loupe opens, a click copies the color under the cursor as #RRGGBB.</summary>
    public string ColorHotkey { get; set; } = "Super+Shift+C";

    /// <summary>Look of the capture screen: a CaptureThemes id ("blue" is the classic look).</summary>
    public string CaptureTheme { get; set; } = Cometsnap.Core.Settings.CaptureThemes.DefaultId;

    /// <summary>Copy text (OCR with Tesseract).</summary>
    public string TextHotkey { get; set; } = "Ctrl+Alt+T";

    /// <summary>Start recording a region (pick it like a capture). Pressed again while recording: stop.</summary>
    public string RecordHotkey { get; set; } = "Shift+PrintScreen";

    /// <summary>Same as <see cref="RecordHotkey"/>, but records an animated GIF.</summary>
    public string RecordGifHotkey { get; set; } = "Ctrl+Shift+PrintScreen";

    /// <summary>Folder for captures. Empty = default (~/Pictures/Cometsnap, or beside the app in portable mode).</summary>
    public string CapturesFolder { get; set; } = "";

    /// <summary>Capture the moment the mouse is released: copy + save, no toolbar in the way.</summary>
    public bool InstantCapture { get; set; } = true;

    /// <summary>After a capture, show a small card in the corner with an "Edit" button.</summary>
    public bool ShowEditCard { get; set; } = true;

    public LoupeCorner LoupeCorner { get; set; } = LoupeCorner.BottomLeft;
    public bool WindowDetection { get; set; } = true;

    /// <summary>A soft click on capture. Off by default.</summary>
    public bool PlaySounds { get; set; }

    public bool CopyToClipboardOnCapture { get; set; } = true;
    public bool AutoSave { get; set; } = true;

    /// <summary>Name of each saved capture, with {date}, {time}... tokens; "/" makes subfolders (see FileNamePattern).</summary>
    public string FileNamePattern { get; set; } = Cometsnap.Core.Storage.FileNamePattern.Default;

    /// <summary>Format of saved captures: "png" or "jpg".</summary>
    public string ImageFormat { get; set; } = "png";

    /// <summary>After each capture, ask where to save it (the clipboard copy still happens first).</summary>
    public bool AskWhereToSave { get; set; }

    /// <summary>Open every capture in the editor right away.</summary>
    public bool OpenEditorAfterCapture { get; set; }

    /// <summary>Last number used for {counter} in <see cref="FileNamePattern"/>.</summary>
    public int NameCounter { get; set; }
    public int RecentCount { get; set; } = 30;

    // ---- Recording ("Transmissions")
    public int RecordFramesPerSecond { get; set; } = 30;
    public int RecordGifFramesPerSecond { get; set; } = 15;
    public bool RecordSystemAudio { get; set; } = true;
    public bool RecordMicrophone { get; set; }
    public bool RecordShowCursor { get; set; } = true;
    public bool RecordCopyToClipboard { get; set; } = true;

    /// <summary>Wayland: lets the desktop remember the screen you share, so recording starts without asking each time.</summary>
    public string ScreenCastRestoreToken { get; set; } = "";

    /// <summary>Start Cometsnap when you log in (~/.config/autostart). On by default.</summary>
    public bool StartAtLogin { get; set; } = true;

    /// <summary>The "Replace your old screenshot tool" offer was answered (either way).</summary>
    public bool ReplaceOfferAnswered { get; set; }

    /// <summary>The Print Screen key was handed over to Cometsnap through the desktop's keyboard settings.</summary>
    public bool ReplacedDesktopTool { get; set; }

    /// <summary>The desktop's own screenshot shortcuts before Cometsnap took Print Screen (to give them back).</summary>
    public string DesktopShortcutsBackup { get; set; } = "";

    /// <summary>"en", "fr", "de" or "it"; empty follows the desktop's language.</summary>
    public string Language { get; set; } = "";

    // ---- Playful touches
    /// <summary>Tiny meteor showers for snap milestones and the monthly recap.</summary>
    public bool Celebrations { get; set; } = true;

    /// <summary>The capture flies off on a comet after it is taken.</summary>
    public bool ShowCometAnimation { get; set; } = true;

    /// <summary>Snowy comet in December, pumpkin comet at Halloween.</summary>
    public bool SeasonalSkins { get; set; } = true;

    /// <summary>Editor: numbered steps joined by faint star lines.</summary>
    public bool ConstellationSteps { get; set; }

    public int SnapCount { get; set; }
    public bool SnapCountSeeded { get; set; }

    /// <summary>The month being counted ("2026-11") and its snaps so far.</summary>
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
