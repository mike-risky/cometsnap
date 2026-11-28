import Foundation

/// The name a capture gets when it is saved, written by the user with tokens like `{date}` and
/// `{time}`. A "/" makes a subfolder: the default `{year}-{month}/snap_{date}_{time}` gives
/// `2026-11/snap_2026-11-26_08-56-12.png`. Same rules as the Windows and Linux apps (FileNamePattern.cs).
public enum FileNamePattern {
    public static let defaultPattern = "{year}-{month}/snap_{date}_{time}"

    /// Most folder levels a pattern may create, so a typo can't dig a deep tunnel.
    public static let maxFolders = 3

    /// The tokens, in the order Settings shows them.
    public static let tokens = ["{date}", "{time}", "{year}", "{month}", "{day}", "{hour}", "{minute}", "{second}", "{counter}"]

    /// The path of the capture relative to the captures folder, without extension, "/" between
    /// folders. Never empty, never escapes the captures folder.
    public static func expand(_ pattern: String?, date: Date, counter: Int = 1) -> String {
        let p = (pattern ?? "").trimmingCharacters(in: .whitespaces).isEmpty ? defaultPattern : pattern!
        var segments = clean(expandTokens(p, date: date, counter: counter))
        if segments.isEmpty { segments = clean(expandTokens(defaultPattern, date: date, counter: counter)) }
        return segments.joined(separator: "/")
    }

    /// True when the pattern gives a usable name.
    public static func isUsable(_ pattern: String?) -> Bool {
        guard let pattern, !pattern.trimmingCharacters(in: .whitespaces).isEmpty else { return false }
        return !clean(expandTokens(pattern, date: Date(timeIntervalSince1970: 0), counter: 1)).isEmpty
    }

    /// True when the pattern has {counter}, so each save uses up a number.
    public static func usesCounter(_ pattern: String?) -> Bool {
        let p = (pattern ?? "").trimmingCharacters(in: .whitespaces).isEmpty ? defaultPattern : pattern!
        return p.lowercased().contains("{counter}")
    }

    static func expandTokens(_ pattern: String, date: Date, counter: Int) -> String {
        var out = ""
        var rest = Substring(pattern)
        while let open = rest.firstIndex(of: "{") {
            out += rest[rest.startIndex..<open]
            let afterOpen = rest.index(after: open)
            if let close = rest[afterOpen...].firstIndex(of: "}"), let value = token(String(rest[afterOpen..<close]), date: date, counter: counter) {
                out += value
                rest = rest[rest.index(after: close)...]
            } else {
                out += "{"
                rest = rest[afterOpen...]
            }
        }
        out += rest
        return out
    }

    static func token(_ name: String, date: Date, counter: Int) -> String? {
        func f(_ format: String) -> String { SnapNaming.formatter(format).string(from: date) }
        switch name.trimmingCharacters(in: .whitespaces).lowercased() {
        case "date": return f("yyyy-MM-dd")
        case "time": return f("HH-mm-ss")
        case "year": return f("yyyy")
        case "month": return f("MM")
        case "day": return f("dd")
        case "hour": return f("HH")
        case "minute": return f("mm")
        case "second": return f("ss")
        case "counter": return String(format: "%03d", max(1, counter))
        default: return nil
        }
    }

    // Characters Windows refuses in a file name: kept out on every platform, so a synced folder works everywhere.
    private static let invalid: Set<Character> = ["<", ">", ":", "\"", "|", "?", "*"]

    static func clean(_ path: String) -> [String] {
        var result: [String] = []
        for raw in path.split(omittingEmptySubsequences: false, whereSeparator: { $0 == "/" || $0 == "\\" }) {
            var seg = String(raw.map { c -> Character in
                if invalid.contains(c) { return "_" }
                if let a = c.asciiValue, a < 32 { return "_" }
                return c
            })
            seg = seg.trimmingCharacters(in: .whitespaces)
            while seg.hasSuffix(".") { seg.removeLast() }
            while seg.hasPrefix(".") { seg.removeFirst() }
            seg = seg.trimmingCharacters(in: .whitespaces)
            if seg.isEmpty { continue }
            if seg.count > 120 { seg = String(seg.prefix(120)) }
            if isReservedDeviceName(seg) { seg = "_" + seg }
            result.append(seg)
        }
        if result.count > maxFolders + 1 {
            let folders = Array(result.prefix(maxFolders))
            result = folders + [result.dropFirst(maxFolders).joined(separator: "_")]
        }
        return result
    }

    private static func isReservedDeviceName(_ segment: String) -> Bool {
        let stem = segment.split(separator: ".", omittingEmptySubsequences: false).first.map { $0.uppercased() } ?? ""
        if ["CON", "PRN", "AUX", "NUL"].contains(stem) { return true }
        if stem.count == 4, stem.hasPrefix("COM") || stem.hasPrefix("LPT"), let last = stem.last, last.isNumber, last != "0" { return true }
        return false
    }
}
