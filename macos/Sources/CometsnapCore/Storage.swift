import Foundation

public enum MediaKind: String, Codable {
    case image, video, gif

    public static func of(_ url: URL) -> MediaKind? {
        switch url.pathExtension.lowercased() {
        case "png", "jpg", "jpeg": return .image
        case "mp4", "mov": return .video
        case "gif": return .gif
        default: return nil
        }
    }
}

/// Naming scheme for captures: `<root>/2026-11/snap_2026-11-26_08-56-12.png`,
/// recordings: `<root>/2026-11/rec_2026-11-26_08-56-12.mp4`.
public enum SnapNaming {
    public static let snapPrefix = "snap_"
    public static let recordingPrefix = "rec_"

    static func formatter(_ format: String) -> DateFormatter {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.calendar = Calendar(identifier: .gregorian)
        f.dateFormat = format
        return f
    }

    public static func monthFolder(root: URL, date: Date) -> URL {
        root.appendingPathComponent(formatter("yyyy-MM").string(from: date), isDirectory: true)
    }

    public static func baseName(prefix: String = snapPrefix, date: Date) -> String {
        prefix + formatter("yyyy-MM-dd_HH-mm-ss").string(from: date)
    }

    /// A full path that does not exist yet. Several captures in the same second get a
    /// numeric suffix: snap_..._08-56-12_2.png, _3, ...
    public static func nextFreeURL(root: URL, date: Date, prefix: String = snapPrefix, ext: String = "png",
                                   exists: (URL) -> Bool = { FileManager.default.fileExists(atPath: $0.path) }) -> URL {
        let folder = monthFolder(root: root, date: date)
        let base = baseName(prefix: prefix, date: date)
        var candidate = folder.appendingPathComponent("\(base).\(ext)")
        var i = 2
        while exists(candidate) {
            candidate = folder.appendingPathComponent("\(base)_\(i).\(ext)")
            i += 1
        }
        return candidate
    }
}

/// Where the editor keeps what it needs to re-open a capture with its shapes still editable:
/// the untouched original and the annotations, in a hidden ".cometsnap" folder beside the
/// image (same layout as the Windows version, so a synced folder works on both).
public enum EditSidecar {
    public static let folderName = ".cometsnap"

    public static func folder(for image: URL) -> URL {
        image.deletingLastPathComponent().appendingPathComponent(folderName, isDirectory: true)
    }

    public static func original(for image: URL) -> URL {
        folder(for: image).appendingPathComponent(image.deletingPathExtension().lastPathComponent + ".original.png")
    }

    public static func annotations(for image: URL) -> URL {
        folder(for: image).appendingPathComponent(image.deletingPathExtension().lastPathComponent + ".annotations.json")
    }

    public static func delete(for image: URL) {
        try? FileManager.default.removeItem(at: original(for: image))
        try? FileManager.default.removeItem(at: annotations(for: image))
    }
}

public struct RecentItem: Equatable {
    public let url: URL
    public let modified: Date
    public let kind: MediaKind

    public init(url: URL, modified: Date, kind: MediaKind) {
        self.url = url
        self.modified = modified
        self.kind = kind
    }

    public var name: String { url.lastPathComponent }

    public func matches(_ query: String) -> Bool {
        let q = query.trimmingCharacters(in: .whitespaces).lowercased()
        return q.isEmpty || name.lowercased().contains(q)
    }
}

/// Lists the latest captures and recordings (newest first) in the captures folder and its
/// month subfolders. Hidden folders such as ".cometsnap" are skipped.
public enum RecentCaptures {
    public static func scan(root: URL, limit: Int = 60) -> [RecentItem] {
        let fm = FileManager.default
        var items: [RecentItem] = []
        let keys: [URLResourceKey] = [.contentModificationDateKey, .isDirectoryKey]
        guard let top = try? fm.contentsOfDirectory(at: root, includingPropertiesForKeys: keys, options: [.skipsHiddenFiles]) else {
            return []
        }
        var folders: [URL] = [root]
        for u in top where (try? u.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) == true { folders.append(u) }
        for folder in folders {
            guard let files = try? fm.contentsOfDirectory(at: folder, includingPropertiesForKeys: keys, options: [.skipsHiddenFiles]) else { continue }
            for f in files {
                guard let kind = MediaKind.of(f) else { continue }
                let values = try? f.resourceValues(forKeys: Set(keys))
                if values?.isDirectory == true { continue }
                items.append(RecentItem(url: f, modified: values?.contentModificationDate ?? .distantPast, kind: kind))
            }
        }
        items.sort { $0.modified > $1.modified }
        return Array(items.prefix(limit))
    }
}
