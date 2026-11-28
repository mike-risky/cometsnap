import Foundation

// Update checks: once a day Cometsnap asks GitHub Releases whether a newer stable version is out.
// Nothing is downloaded or opened until the user clicks "Update now".
// The same rules as the Windows and Linux apps (src/Cometsnap.Core/Updates/Updates.cs).

/// A version number like 1.4.0. Builds numbered 0.x are test builds.
public struct AppVersion: Comparable, Hashable, CustomStringConvertible {
    public var major: Int
    public var minor: Int
    public var patch: Int

    public init(_ major: Int, _ minor: Int, _ patch: Int) {
        self.major = major
        self.minor = minor
        self.patch = patch
    }

    /// "v1.4.0", "1.4", "1.4.0.0" or "1.4.0-beta" (the suffix is ignored).
    public init?(_ text: String?) {
        guard var s = text?.trimmingCharacters(in: .whitespaces), !s.isEmpty else { return nil }
        if s.hasPrefix("v") || s.hasPrefix("V") { s.removeFirst() }
        var parts: [Int] = []
        var digits = ""
        for ch in s {
            if ch.isASCII && ch.isNumber { digits.append(ch); continue }
            guard !digits.isEmpty else { break }
            parts.append(Int(digits) ?? 0)
            digits = ""
            if ch != "." || parts.count == 3 { break }
        }
        if !digits.isEmpty && parts.count < 3 { parts.append(Int(digits) ?? 0) }
        guard let first = parts.first else { return nil }
        self.init(first, parts.count > 1 ? parts[1] : 0, parts.count > 2 ? parts[2] : 0)
    }

    public var isDevBuild: Bool { major == 0 }
    public var description: String { "\(major).\(minor).\(patch)" }

    public static func < (a: AppVersion, b: AppVersion) -> Bool {
        (a.major, a.minor, a.patch) < (b.major, b.minor, b.patch)
    }
}

public struct ReleaseAsset: Equatable {
    public let name: String
    public let url: URL
    public let size: Int
}

/// A GitHub release, as read from the releases API.
public struct ReleaseInfo: Equatable {
    public let tag: String
    public let version: AppVersion
    public let pageURL: URL?
    public let assets: [ReleaseAsset]
    public var prerelease: Bool
    public var draft: Bool

    public func asset(_ name: String) -> ReleaseAsset? {
        assets.first { $0.name.caseInsensitiveCompare(name) == .orderedSame }
    }

    public static func parse(_ data: Data) -> ReleaseInfo? {
        guard let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let tag = obj["tag_name"] as? String, let version = AppVersion(tag) else { return nil }
        let assets: [ReleaseAsset] = (obj["assets"] as? [[String: Any]] ?? []).compactMap { a in
            guard let name = a["name"] as? String, let s = a["browser_download_url"] as? String, let url = URL(string: s) else { return nil }
            return ReleaseAsset(name: name, url: url, size: a["size"] as? Int ?? 0)
        }
        return ReleaseInfo(tag: tag, version: version, pageURL: (obj["html_url"] as? String).flatMap(URL.init(string:)),
                           assets: assets, prerelease: obj["prerelease"] as? Bool ?? false, draft: obj["draft"] as? Bool ?? false)
    }

    /// Newer and stable? "Check now" (manual) also shows a skipped version, and on a test build offers the latest release.
    public static func isOffered(_ release: ReleaseInfo?, current: AppVersion, skipped: String, manual: Bool) -> Bool {
        guard let r = release, !r.draft, !r.prerelease, !r.version.isDevBuild else { return false }
        if manual && current.isDevBuild { return true }
        if r.version <= current { return false }
        return manual || AppVersion(skipped) != r.version
    }
}

/// What a new version brings, for the update card: updates/whats-new.json (8 languages), else CHANGELOG.md.
public enum WhatsNew {
    public static let maxLines = 4

    /// The lines of every version after `current` up to `target`, newest first. "latest" stands for `target`
    /// when the file has no entry of its own for it.
    public static func fromJSON(_ data: Data, current: AppVersion, target: AppVersion, language: String, max: Int = maxLines) -> [String] {
        guard let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return [] }
        var entries: [(AppVersion, [String: Any])] = []
        var latest: [String: Any]?
        for (key, value) in obj {
            guard let texts = value as? [String: Any] else { continue }
            if key.lowercased() == "latest" { latest = texts }
            else if let v = AppVersion(key), v > current, v <= target { entries.append((v, texts)) }
        }
        if let l = latest, !entries.contains(where: { $0.0 == target }) { entries.append((target, l)) }
        var lines: [String] = []
        for (_, texts) in entries.sorted(by: { $0.0 > $1.0 }) {
            let own = texts[language] as? [String] ?? []
            for line in (own.isEmpty ? texts["en"] as? [String] ?? [] : own) {
                let s = line.trimmingCharacters(in: .whitespaces)
                if !s.isEmpty && !lines.contains(s) { lines.append(s) }
            }
        }
        return Array(lines.prefix(max))
    }

    /// The bold titles of the CHANGELOG.md sections after `current` up to `target`; "## Latest build" stands for `target`.
    public static func fromChangelog(_ markdown: String, current: AppVersion, target: AppVersion, max: Int = maxLines) -> [String] {
        var sections: [(AppVersion, [String])] = []
        var latest: [String] = []
        enum Into { case none, latest, section(Int) }
        var into = Into.none
        for raw in markdown.components(separatedBy: "\n") {
            let line = raw.trimmingCharacters(in: CharacterSet(charactersIn: "\r"))
            if line.hasPrefix("## ") {
                let head = line.dropFirst(3).trimmingCharacters(in: .whitespaces)
                if head.lowercased().hasPrefix("latest") { into = .latest }
                else if let v = AppVersion(head), v > current, v <= target { sections.append((v, [])); into = .section(sections.count - 1) }
                else { into = .none }
                continue
            }
            let t = line.trimmingCharacters(in: .whitespaces)
            // Top-level items only, not their sub-points.
            guard line.hasPrefix("- **"),
                  let end = t.range(of: "**", range: t.index(t.startIndex, offsetBy: 4)..<t.endIndex) else { continue }
            var title = String(t[t.index(t.startIndex, offsetBy: 4)..<end.lowerBound]).trimmingCharacters(in: .whitespaces)
            while let last = title.last, last == "." || last == ":" { title.removeLast() }
            switch into {
            case .none: break
            case .latest: latest.append(title)
            case .section(let i): sections[i].1.append(title)
            }
        }
        if !latest.isEmpty && !sections.contains(where: { $0.0 == target }) { sections.append((target, latest)) }
        var lines: [String] = []
        for (_, ls) in sections.sorted(by: { $0.0 > $1.0 }) {
            for l in ls where !l.isEmpty && !lines.contains(l) { lines.append(l) }
        }
        return Array(lines.prefix(max))
    }
}

/// SHA256SUMS files ("hash  name" per line), names lowercased.
public enum Checksums {
    public static func parse(_ text: String) -> [String: String] {
        var map: [String: String] = [:]
        for raw in text.components(separatedBy: "\n") {
            let parts = raw.trimmingCharacters(in: .whitespacesAndNewlines).split(separator: " ", maxSplits: 1, omittingEmptySubsequences: true)
            guard parts.count == 2, parts[0].count == 64, parts[0].allSatisfy({ $0.isHexDigit }) else { continue }
            var name = parts[1].trimmingCharacters(in: .whitespaces)
            if name.hasPrefix("*") { name.removeFirst() }
            map[(name as NSString).lastPathComponent.lowercased()] = parts[0].lowercased()
        }
        return map
    }
}

/// Remembered between runs (updates.json): the on/off switch, the last check and a skipped version.
public struct UpdatePrefs: Codable, Equatable {
    public var checkAutomatically = true
    public var lastCheck: Date?
    public var skippedVersion = ""

    public static let interval: TimeInterval = 24 * 3600

    public init() {}

    enum CodingKeys: String, CodingKey { case checkAutomatically, lastCheck, skippedVersion }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        checkAutomatically = (try? c.decodeIfPresent(Bool.self, forKey: .checkAutomatically)) ?? true
        lastCheck = try? c.decodeIfPresent(Date.self, forKey: .lastCheck)
        skippedVersion = (try? c.decodeIfPresent(String.self, forKey: .skippedVersion)) ?? ""
    }

    /// Time for the daily check? Never on its own for test builds.
    public func isDue(now: Date, current: AppVersion) -> Bool {
        guard checkAutomatically, !current.isDevBuild else { return false }
        guard let last = lastCheck else { return true }
        return now.timeIntervalSince(last) >= Self.interval || last > now
    }

    public static func load(from url: URL) -> UpdatePrefs {
        guard let data = try? Data(contentsOf: url) else { return UpdatePrefs() }
        let dec = JSONDecoder()
        dec.dateDecodingStrategy = .iso8601
        return (try? dec.decode(UpdatePrefs.self, from: data)) ?? UpdatePrefs()
    }

    public func save(to url: URL) {
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        enc.dateEncodingStrategy = .iso8601
        guard let data = try? enc.encode(self) else { return }
        try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try? data.write(to: url, options: .atomic)
    }
}

public enum UpdateLinks {
    public static let repo = "mike-risky/cometsnap"
    public static let latestAPI = URL(string: "https://api.github.com/repos/\(repo)/releases/latest")!

    public static func raw(_ tag: String, _ path: String) -> URL? {
        URL(string: "https://raw.githubusercontent.com/\(repo)/\(tag.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed) ?? tag)/\(path)")
    }

    /// cometsnap.com/changelog in the app's language.
    public static func changelog(_ language: String) -> URL {
        URL(string: language == "en" ? "https://cometsnap.com/changelog" : "https://cometsnap.com/\(language)/changelog")!
    }
}
