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

    public init() {}

    enum CodingKeys: String, CodingKey {
        case capturesFolder, loupeCorner, playSound, cometAnimation, showEditCard, copyToClipboard,
             takeOverSystemShortcut, firstRunDone, recordSystemAudio, gifFramesPerSecond, videoFramesPerSecond,
             celebrations, seasonalLooks, constellationSteps, snapCount, snapMonth, snapMonthCount, snapCountSeeded
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
        videoFramesPerSecond = min(60, max(10, (try? c.decodeIfPresent(Int.self, forKey: .videoFramesPerSecond)) ?? d.videoFramesPerSecond))
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
