import Foundation

/// The keys the Konami code is made of.
public enum KonamiKey: Equatable {
    case up, down, left, right, b, a, other
}

/// Watches key presses for up up down down left right left right B A.
public struct KonamiCode {
    public static let sequence: [KonamiKey] = [.up, .up, .down, .down, .left, .right, .left, .right, .b, .a]
    public private(set) var matched = 0

    public init() {}

    /// Feeds one key. True when the whole code has just been typed.
    public mutating func feed(_ key: KonamiKey) -> Bool {
        if key == KonamiCode.sequence[matched] {
            matched += 1
            if matched == KonamiCode.sequence.count {
                matched = 0
                return true
            }
        } else if key == .up {
            // "up up up down…" still counts: keep the two ups we have.
            matched = matched == 2 ? 2 : 1
        } else {
            matched = 0
        }
        return false
    }

    public mutating func reset() { matched = 0 }
}

/// Seasonal looks for the comet.
public enum Season: Equatable {
    case none, halloween, winter

    public static func at(_ date: Date, calendar: Calendar = .current) -> Season {
        let c = calendar.dateComponents([.month, .day], from: date)
        if c.month == 10, (c.day ?? 0) >= 18 { return .halloween }
        if c.month == 12 { return .winter }
        return .none
    }
}

/// A month that just ended, for the "You captured N snaps" recap.
public struct MonthSummary: Equatable {
    public let month: String // yyyy-MM
    public let count: Int

    /// "October 2026" (in the app's language).
    public var title: String {
        let parts = month.split(separator: "-")
        guard parts.count == 2, let y = Int(parts[0]), let m = Int(parts[1]), (1...12).contains(m) else { return month }
        if L.language == "zh" { return "\(y)年\(m)月" }
        let f = DateFormatter()
        f.locale = L.language == "en" ? Locale(identifier: "en_US_POSIX") : L.locale
        // Mid-sentence: "in October 2026", "en octobre 2026", "im Oktober 2026".
        return "\(f.monthSymbols[m - 1]) \(y)"
    }
}

public struct SnapEvent: Equatable {
    public let milestone: Int?
    public let finishedMonth: MonthSummary?
}

/// Counts snaps and spots the round numbers worth a tiny celebration.
public enum SnapMilestones {
    public static let steps = [10, 50, 100, 250, 500, 1000, 2500, 5000, 10000]

    public static func isMilestone(_ n: Int) -> Bool {
        if steps.contains(n) { return true }
        return n > 10000 && n % 10000 == 0
    }

    /// "1,000th snap!"
    public static func title(_ n: Int) -> String {
        if L.language == "en" { return "\(formatted(n))\(suffix(n)) snap!" }
        return L.f("{0}th snap!", formatted(n))
    }

    /// "1,000" ("1 000" in French, "1.000" in German and Italian).
    public static func formatted(_ n: Int) -> String {
        let f = NumberFormatter()
        f.numberStyle = .decimal
        f.locale = L.language == "en" ? Locale(identifier: "en_US") : L.locale
        return f.string(from: NSNumber(value: n)) ?? "\(n)"
    }

    public static func suffix(_ n: Int) -> String {
        if (11...13).contains(n % 100) { return "th" }
        switch n % 10 {
        case 1: return "st"
        case 2: return "nd"
        case 3: return "rd"
        default: return "th"
        }
    }

    public static func monthKey(_ date: Date, calendar: Calendar = .current) -> String {
        let c = calendar.dateComponents([.year, .month], from: date)
        return String(format: "%04d-%02d", c.year ?? 0, c.month ?? 0)
    }

    /// Counts one snap. Returns a milestone when one is reached, and last month's total
    /// when this is the first snap of a new month.
    public static func record(_ s: inout AppSettings, now: Date, calendar: Calendar = .current) -> SnapEvent {
        let key = monthKey(now, calendar: calendar)
        var finished: MonthSummary?
        if s.snapMonth != key {
            if let old = s.snapMonth, s.snapMonthCount > 0 { finished = MonthSummary(month: old, count: s.snapMonthCount) }
            s.snapMonth = key
            s.snapMonthCount = 0
        }
        s.snapMonthCount += 1
        s.snapCount += 1
        return SnapEvent(milestone: isMilestone(s.snapCount) ? s.snapCount : nil, finishedMonth: finished)
    }

    /// First launch with this feature: count the captures already on disk, silently.
    public static func seed(_ s: inout AppSettings, existing: [Date], now: Date, calendar: Calendar = .current) {
        guard !s.snapCountSeeded else { return }
        let key = monthKey(now, calendar: calendar)
        let thisMonth = existing.filter { monthKey($0, calendar: calendar) == key }.count
        s.snapCount = max(s.snapCount, existing.count)
        if s.snapMonth == nil || s.snapMonth == key {
            s.snapMonth = key
            s.snapMonthCount = max(s.snapMonthCount, thisMonth)
        }
        s.snapCountSeeded = true
    }
}
