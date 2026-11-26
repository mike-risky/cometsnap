import Foundation

/// A span of time in a recording, in seconds, from `start` (inclusive) to `end`.
public struct TimeRange: Equatable {
    public var start: Double
    public var end: Double

    public init(_ start: Double, _ end: Double) {
        self.start = start
        self.end = end
    }

    public var length: Double { end - start }
    public func contains(_ t: Double) -> Bool { t >= start && t < end }
}

/// A piece of the source kept in the output: source span and where it lands in the output.
public struct KeptPiece: Equatable {
    public let sourceStart: Double
    public let sourceEnd: Double
    public let outputStart: Double
    public var length: Double { sourceEnd - sourceStart }
}

/// What to keep of a recording: a trimmed start and end, and sections cut out of the middle.
/// Same rules and saved format as the Windows version (name.cuts.json: {Start, End, Cuts}).
public struct CutList: Equatable {
    /// The shortest piece you can keep or cut (a tenth of a second).
    public static let minPiece = 0.1

    public let duration: Double
    private var _trimStart = 0.0
    private var _trimEnd: Double
    public private(set) var cuts: [TimeRange] = []

    public init(duration: Double) {
        self.duration = max(0, duration)
        _trimEnd = self.duration
    }

    /// Where the output starts in the source.
    public var trimStart: Double {
        get { _trimStart }
        set { _trimStart = Self.clamp(newValue, 0, max(0, _trimEnd - Self.minPiece)) }
    }

    /// Where the output ends in the source.
    public var trimEnd: Double {
        get { _trimEnd }
        set { _trimEnd = Self.clamp(newValue, min(duration, _trimStart + Self.minPiece), duration) }
    }

    public var isUntouched: Bool { _trimStart == 0 && _trimEnd == duration && cuts.isEmpty }

    /// Cuts out a section. Overlapping or touching cuts merge. Refused (false) when it is too short,
    /// lies outside the trimmed part, or would leave less than `minPiece` to keep.
    @discardableResult
    public mutating func addCut(_ a0: Double, _ b0: Double) -> Bool {
        var a = min(a0, b0), b = max(a0, b0)
        a = max(a, _trimStart)
        b = min(b, _trimEnd)
        if b - a < Self.minPiece { return false }
        var merged: [TimeRange] = []
        var cut = TimeRange(a, b)
        for c in cuts {
            if c.end < cut.start || c.start > cut.end { merged.append(c) } else { cut = TimeRange(min(c.start, cut.start), max(c.end, cut.end)) }
        }
        merged.append(cut)
        merged.sort { $0.start < $1.start }
        let before = cuts
        cuts = merged
        if keptLength < Self.minPiece {
            cuts = before
            return false
        }
        return true
    }

    /// Puts back the cut under `t`. False when there is none.
    @discardableResult
    public mutating func restoreCut(at t: Double) -> Bool {
        guard let i = cuts.firstIndex(where: { $0.contains(t) }) else { return false }
        cuts.remove(at: i)
        return true
    }

    public func cut(at t: Double) -> TimeRange? { cuts.first { $0.contains(t) } }

    /// The parts of the source that end up in the output, in order.
    public var kept: [KeptPiece] {
        var pieces: [KeptPiece] = []
        var at = _trimStart
        var output = 0.0
        for c in cuts {
            if c.end <= _trimStart || c.start >= _trimEnd { continue }
            let end = max(at, c.start)
            if end > at {
                pieces.append(KeptPiece(sourceStart: at, sourceEnd: end, outputStart: output))
                output += end - at
            }
            at = max(at, c.end)
        }
        if _trimEnd > at { pieces.append(KeptPiece(sourceStart: at, sourceEnd: _trimEnd, outputStart: output)) }
        return pieces
    }

    public var keptLength: Double { kept.reduce(0) { $0 + $1.length } }

    public func isKept(_ t: Double) -> Bool { kept.contains { t >= $0.sourceStart && t < $0.sourceEnd } }

    /// For playback: `t` if it is kept, else the start of the next kept piece, or nil when
    /// nothing is kept after it.
    public func nextKept(_ t: Double) -> Double? {
        for p in kept {
            if t < p.sourceStart { return p.sourceStart }
            if t < p.sourceEnd { return t }
        }
        return nil
    }

    /// Source time → output time (a removed moment maps to where the cut joins).
    public func toOutput(_ t: Double) -> Double {
        var last = 0.0
        for p in kept {
            if t < p.sourceStart { return p.outputStart }
            if t < p.sourceEnd { return p.outputStart + (t - p.sourceStart) }
            last = p.outputStart + p.length
        }
        return last
    }

    /// For a source frame shown from `from` until `to`: the output times at which it must be
    /// written (none when the frame is cut, one per kept piece that starts while it is on screen).
    public func frameOutputTimes(from: Double, to: Double) -> [Double] {
        kept.compactMap { p in
            if p.sourceEnd <= from || p.sourceStart >= to { return nil }
            return p.outputStart + (max(from, p.sourceStart) - p.sourceStart)
        }
    }

    // MARK: saving

    private struct Saved: Codable {
        var Start: Double
        var End: Double
        var Cuts: [[Double]]?
    }

    public func json() -> Data {
        let s = Saved(Start: _trimStart, End: _trimEnd, Cuts: cuts.map { [$0.start, $0.end] })
        return (try? JSONEncoder().encode(s)) ?? Data()
    }

    /// Reads what `json()` wrote; anything odd falls back to "untouched".
    public static func from(json data: Data, duration: Double) -> CutList {
        var list = CutList(duration: duration)
        guard let s = try? JSONDecoder().decode(Saved.self, from: data) else { return list }
        list.trimEnd = s.End
        list.trimStart = s.Start
        for c in s.Cuts ?? [] where c.count == 2 { list.addCut(c[0], c[1]) }
        return list
    }

    /// "1:05.3" style label for the timeline.
    public static func label(_ t: Double, tenths: Bool = true) -> String {
        let t = max(0, t)
        let minutes = Int(t / 60)
        let seconds = t - Double(minutes) * 60
        if tenths { return String(format: "%d:%04.1f", minutes, (seconds * 10).rounded(.down) / 10) }
        return String(format: "%d:%02d", minutes, Int(seconds.rounded(.down)))
    }

    private static func clamp(_ v: Double, _ lo: Double, _ hi: Double) -> Double { v < lo ? lo : v > hi ? hi : v }
}
