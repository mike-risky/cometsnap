import Foundation

/// Everything the user can change. Stored as JSON in
/// ~/Library/Application Support/Cometsnap/settings.json. Unknown or missing keys fall back
/// to the defaults, so older and newer versions can share the file.
public struct AppSettings: Codable, Equatable {
    public var capturesFolder: String?
    public var loupeCorner: LoupeCorner = .bottomLeft
    public var playSound = false
    public var cometAnimation = true
    public var showEditCard = true
    public var copyToClipboard = true
    public var takeOverSystemShortcut = false
    public var firstRunDone = false
    public var recordSystemAudio = true
    public var gifFramesPerSecond = 15
    public var videoFramesPerSecond = 30
    /// Milestone meteor showers and the monthly recap.
    public var celebrations = true
    /// Pumpkin comet in late October, snowy comet in December.
    public var seasonalLooks = true
    /// Editor: join numbered steps with star lines (remembered from the last toggle).
    public var constellationSteps = false
    public var snapCount = 0
    public var snapMonth: String?
    public var snapMonthCount = 0
    public var snapCountSeeded = false
    /// "" = same as macOS, else one of the codes in L.languages.
    public var language = ""
    /// Look of the capture screen: a CaptureThemes id ("blue" is the classic look).
    public var captureTheme = CaptureThemes.defaultID
    /// Share-ready look built in the Style studio (corners, border, shadow, glow, backdrop).
    public var snapStyle = SnapStyle()
    /// Put the styled capture on the clipboard. Off: the raw capture.
    public var styleOnCopy = false
    /// Save the styled capture (the file you drag out of the panel). Off: the raw capture.
    public var styleOnExport = false
    /// A ✨ Style button on the Copied card styles just that capture.
    public var stylePerShot = true
    /// Save each capture in the captures folder (the clipboard copy is separate).
    public var saveEveryCapture = true
    /// Name of each saved capture, with {date}, {time}… tokens; "/" makes subfolders (see FileNamePattern).
    public var fileNamePattern = FileNamePattern.defaultPattern
    /// "png" (sharp, lossless) or "jpg" (smaller files).
    public var imageFormat = "png"
    /// After each capture, ask where to save it (the clipboard copy still happens first).
    public var askWhereToSave = false
    /// Open every capture in the editor right away.
    public var openEditorAfterCapture = false
    /// Last number used for {counter} in the file name pattern.
    public var nameCounter = 0
    /// Shortcuts the user changed, by action id (see HotkeyAction). Missing = the built-in one.
    public var hotkeys: [String: HotkeySpec] = [:]

    public init() {}

    enum CodingKeys: String, CodingKey {
        case capturesFolder, loupeCorner, playSound, cometAnimation, showEditCard, copyToClipboard,
             takeOverSystemShortcut, firstRunDone, recordSystemAudio, gifFramesPerSecond, videoFramesPerSecond,
             celebrations, seasonalLooks, constellationSteps, snapCount, snapMonth, snapMonthCount, snapCountSeeded, language, captureTheme,
             saveEveryCapture, fileNamePattern, imageFormat, askWhereToSave, openEditorAfterCapture, nameCounter, hotkeys,
             snapStyle, styleOnCopy, styleOnExport, stylePerShot
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = AppSettings()
        capturesFolder = try c.decodeIfPresent(String.self, forKey: .capturesFolder)
        loupeCorner = (try? c.decodeIfPresent(LoupeCorner.self, forKey: .loupeCorner)) ?? d.loupeCorner
        playSound = (try? c.decodeIfPresent(Bool.self, forKey: .playSound)) ?? d.playSound
        cometAnimation = (try? c.decodeIfPresent(Bool.self, forKey: .cometAnimation)) ?? d.cometAnimation
        showEditCard = (try? c.decodeIfPresent(Bool.self, forKey: .showEditCard)) ?? d.showEditCard
        copyToClipboard = (try? c.decodeIfPresent(Bool.self, forKey: .copyToClipboard)) ?? d.copyToClipboard
        takeOverSystemShortcut = (try? c.decodeIfPresent(Bool.self, forKey: .takeOverSystemShortcut)) ?? d.takeOverSystemShortcut
        firstRunDone = (try? c.decodeIfPresent(Bool.self, forKey: .firstRunDone)) ?? d.firstRunDone
        recordSystemAudio = (try? c.decodeIfPresent(Bool.self, forKey: .recordSystemAudio)) ?? d.recordSystemAudio
        gifFramesPerSecond = min(30, max(5, (try? c.decodeIfPresent(Int.self, forKey: .gifFramesPerSecond)) ?? d.gifFramesPerSecond))
        celebrations = (try? c.decodeIfPresent(Bool.self, forKey: .celebrations)) ?? d.celebrations
        seasonalLooks = (try? c.decodeIfPresent(Bool.self, forKey: .seasonalLooks)) ?? d.seasonalLooks
        constellationSteps = (try? c.decodeIfPresent(Bool.self, forKey: .constellationSteps)) ?? d.constellationSteps
        snapCount = max(0, (try? c.decodeIfPresent(Int.self, forKey: .snapCount)) ?? 0)
        snapMonth = try? c.decodeIfPresent(String.self, forKey: .snapMonth)
        snapMonthCount = max(0, (try? c.decodeIfPresent(Int.self, forKey: .snapMonthCount)) ?? 0)
        snapCountSeeded = (try? c.decodeIfPresent(Bool.self, forKey: .snapCountSeeded)) ?? false
        language = (try? c.decodeIfPresent(String.self, forKey: .language)) ?? ""
        captureTheme = CaptureThemes.get(try? c.decodeIfPresent(String.self, forKey: .captureTheme)).id
        videoFramesPerSecond = min(60, max(10, (try? c.decodeIfPresent(Int.self, forKey: .videoFramesPerSecond)) ?? d.videoFramesPerSecond))
        saveEveryCapture = (try? c.decodeIfPresent(Bool.self, forKey: .saveEveryCapture)) ?? d.saveEveryCapture
        let pattern = (try? c.decodeIfPresent(String.self, forKey: .fileNamePattern)) ?? nil
        fileNamePattern = FileNamePattern.isUsable(pattern) ? pattern! : d.fileNamePattern
        imageFormat = SnapNaming.fileExtension(for: (try? c.decodeIfPresent(String.self, forKey: .imageFormat)) ?? nil)
        askWhereToSave = (try? c.decodeIfPresent(Bool.self, forKey: .askWhereToSave)) ?? d.askWhereToSave
        openEditorAfterCapture = (try? c.decodeIfPresent(Bool.self, forKey: .openEditorAfterCapture)) ?? d.openEditorAfterCapture
        nameCounter = max(0, (try? c.decodeIfPresent(Int.self, forKey: .nameCounter)) ?? 0)
        // Early test builds saved a preset id: it loads as that preset.
        if let style = (try? c.decodeIfPresent(SnapStyle.self, forKey: .snapStyle)) ?? nil { snapStyle = style }
        else if let id = (try? c.decodeIfPresent(String.self, forKey: .snapStyle)) ?? nil { snapStyle = SnapStylePresets.get(id) }
        styleOnCopy = (try? c.decodeIfPresent(Bool.self, forKey: .styleOnCopy)) ?? d.styleOnCopy
        styleOnExport = (try? c.decodeIfPresent(Bool.self, forKey: .styleOnExport)) ?? d.styleOnExport
        stylePerShot = (try? c.decodeIfPresent(Bool.self, forKey: .stylePerShot)) ?? d.stylePerShot
        hotkeys = ((try? c.decodeIfPresent([String: HotkeySpec].self, forKey: .hotkeys)) ?? nil) ?? [:]
    }

    public static func load(from url: URL) -> AppSettings {
        guard let data = try? Data(contentsOf: url), let s = try? JSONDecoder().decode(AppSettings.self, from: data) else {
            return AppSettings()
        }
        return s
    }

    public func save(to url: URL) {
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        guard let data = try? enc.encode(self) else { return }
        try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try? data.write(to: url, options: .atomic)
    }
}

/// A keyboard shortcut the user picked: the Carbon key code and modifiers, plus how to show it ("⌘⇧2").
public struct HotkeySpec: Codable, Equatable {
    public var keyCode: UInt32
    public var modifiers: UInt32
    public var display: String

    public init(keyCode: UInt32, modifiers: UInt32, display: String) {
        self.keyCode = keyCode
        self.modifiers = modifiers
        self.display = display
    }
}

/// The actions whose shortcut can be changed in Settings.
public enum HotkeyAction: String, CaseIterable {
    case capture, video, gif, text, scrolling, color, qr
}

/// Standard folders.
public struct AppPaths {
    public let dataDirectory: URL
    public let defaultCapturesDirectory: URL

    public init(dataDirectory: URL, defaultCapturesDirectory: URL) {
        self.dataDirectory = dataDirectory
        self.defaultCapturesDirectory = defaultCapturesDirectory
    }

    public static var standard: AppPaths {
        let fm = FileManager.default
        let support = fm.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? fm.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support")
        let pictures = fm.urls(for: .picturesDirectory, in: .userDomainMask).first
            ?? fm.homeDirectoryForCurrentUser.appendingPathComponent("Pictures")
        return AppPaths(dataDirectory: support.appendingPathComponent("Cometsnap", isDirectory: true),
                        defaultCapturesDirectory: pictures.appendingPathComponent("Cometsnap", isDirectory: true))
    }

    public var settingsFile: URL { dataDirectory.appendingPathComponent("settings.json") }
    public var logFile: URL { dataDirectory.appendingPathComponent("cometsnap.log") }

    public func capturesDirectory(_ settings: AppSettings) -> URL {
        if let custom = settings.capturesFolder, !custom.isEmpty {
            return URL(fileURLWithPath: (custom as NSString).expandingTildeInPath, isDirectory: true)
        }
        return defaultCapturesDirectory
    }
}
