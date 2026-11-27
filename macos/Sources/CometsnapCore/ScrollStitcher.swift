import Foundation

/// What happened to a frame given to `ScrollStitcher.add`.
public enum StitchStep: Equatable {
    /// The first frame: the start of the image.
    case first
    /// The page moved and the new rows were added at the bottom.
    case appended
    /// Nothing moved (the end of the page, or the scroll has not happened yet).
    case unchanged
    /// The frame does not continue the previous one (the page jumped, or changed too much).
    case lost
}

/// Glues the frames of a scrolling capture into one tall image. Each new frame is compared with
/// the previous one row by row to find how far the page scrolled; only the rows that came into
/// view are kept. Rows that never move at the top and bottom (a sticky header, a toolbar)
/// appear once. Works on 4-byte pixels (BGRA or RGBA), top-down, rows of `width × 4` bytes.
/// Same algorithm as the Windows app (Cometsnap.Core/Capture/ScrollStitcher.cs).
public final class ScrollStitcher {
    public let width: Int
    public let frameHeight: Int
    /// Columns on the right ignored when comparing rows: the scrollbar moves at every step.
    public let ignoreRight: Int

    public private(set) var header = 0
    public private(set) var footer = 0
    private var edgesKnown = false

    /// How far the last appended frame had scrolled, in pixels.
    public private(set) var lastShift = 0

    public var scrollingHeight: Int { frameHeight - header - footer }

    /// Height of the image so far.
    public var height: Int { first == nil ? 0 : frameHeight + strips.reduce(0) { $0 + $1.count } / (width * 4) }

    private var first: [UInt8]?
    private var last: [UInt8]?
    private var lastHashes: [UInt64] = []
    private var lastPlain: [Bool] = []
    private var lastSig: [Int32] = []
    /// Columns of each row's coarse signature (average brightness of that many slices).
    static let slices = 16
    private var strips: [[UInt8]] = []

    public init(width: Int, frameHeight: Int, ignoreRight: Int? = nil) {
        precondition(width > 0 && frameHeight > 0)
        self.width = width
        self.frameHeight = frameHeight
        self.ignoreRight = max(0, min(width - 1, ignoreRight ?? min(24, width / 8)))
    }

    public func add(_ pixels: [UInt8]) -> StitchStep {
        precondition(pixels.count >= width * 4 * frameHeight, "frame is too small")
        let (hashes, plain, sig) = hashRows(pixels)
        guard last != nil else {
            first = pixels
            last = pixels
            lastHashes = hashes
            lastPlain = plain
            lastSig = sig
            return .first
        }
        if hashes == lastHashes { return .unchanged }
        if !edgesKnown {
            (header, footer) = Self.fixedEdges(previous: lastHashes, previousPlain: lastPlain, current: hashes)
            edgesKnown = true
        }
        var shift = Self.findShift(previous: lastHashes, current: hashes, currentPlain: plain,
                                   top: header, bottom: frameHeight - footer, expected: lastShift)
        // No exact match (smooth scrolling, Retina scaling, hover effects): match loosely.
        if shift < 0 {
            shift = Self.findShiftLoose(previous: lastSig, current: sig, currentPlain: plain,
                                        top: header, bottom: frameHeight - footer, expected: lastShift)
        }
        if shift <= 0 { return shift == 0 ? .unchanged : .lost }

        let rowBytes = width * 4
        let start = (frameHeight - footer - shift) * rowBytes
        strips.append(Array(pixels[start..<(start + shift * rowBytes)]))
        last = pixels
        lastHashes = hashes
        lastPlain = plain
        lastSig = sig
        lastShift = shift
        return .appended
    }

    /// The finished image: first frame (without its footer), the new rows, then the footer of the last frame.
    public func compose() -> (pixels: [UInt8], height: Int) {
        guard let first, let last else { return ([], 0) }
        let rowBytes = width * 4
        let top = (frameHeight - footer) * rowBytes
        var out = [UInt8]()
        out.reserveCapacity(height * rowBytes)
        out.append(contentsOf: first[0..<top])
        for s in strips { out.append(contentsOf: s) }
        out.append(contentsOf: last[top..<(frameHeight * rowBytes)])
        return (out, height)
    }

    private func hashRows(_ pixels: [UInt8]) -> ([UInt64], [Bool], [Int32]) {
        var hashes = [UInt64](repeating: 0, count: frameHeight)
        var plain = [Bool](repeating: false, count: frameHeight)
        let slices = Self.slices
        var sig = [Int32](repeating: 0, count: frameHeight * slices)
        let rowBytes = width * 4
        let usedPixels = width - ignoreRight
        let used = usedPixels * 4
        pixels.withUnsafeBufferPointer { p in
            for y in 0..<frameHeight {
                let base = y * rowBytes
                var h: UInt64 = 14695981039346656037
                let firstPixel = UInt32(p[base]) | UInt32(p[base + 1]) << 8 | UInt32(p[base + 2]) << 16
                var same = true
                var i = 0
                while i < used {
                    let px = UInt32(p[base + i]) | UInt32(p[base + i + 1]) << 8 | UInt32(p[base + i + 2]) << 16
                    if px != firstPixel { same = false }
                    h = (h ^ UInt64(px)) &* 1099511628211
                    i += 4
                }
                hashes[y] = h
                plain[y] = same
                // Average brightness (×4) of each slice of the row.
                for k in 0..<slices {
                    let from = k * usedPixels / slices
                    let to = max(from + 1, (k + 1) * usedPixels / slices)
                    var sum = 0
                    for x in from..<to {
                        let j = base + x * 4
                        sum += Int(p[j]) + 2 * Int(p[j + 1]) + Int(p[j + 2])
                    }
                    sig[y * slices + k] = Int32(sum / (to - from))
                }
            }
        }
        return (hashes, plain, sig)
    }

    /// Like `findShift`, but rows only have to look alike (same brightness slice by slice), not be
    /// identical. Same rules as the Windows app. 0 = nothing moved, -1 = no shift fits.
    static func findShiftLoose(previous: [Int32], current: [Int32], currentPlain: [Bool], top: Int, bottom: Int, expected: Int) -> Int {
        let height = bottom - top
        if height < 16 { return -1 }
        let minOverlap = max(12, height / 6)
        let step = height > 300 ? 2 : 1
        let count = height - minOverlap
        var errors = [Double](repeating: .greatestFiniteMagnitude, count: count)
        current.withUnsafeBufferPointer { cur in
            previous.withUnsafeBufferPointer { prev in
                for shift in 0..<count {
                    var sum = 0, n = 0, i = 0
                    while i < height - shift {
                        let y = top + i
                        i += step
                        if currentPlain[y] { continue }
                        let a = y * slices, b = (y + shift) * slices
                        for k in 0..<slices { sum += abs(Int(cur[a + k]) - Int(prev[b + k])) }
                        n += 1
                    }
                    if n >= 6 { errors[shift] = Double(sum) / Double(n * slices) }
                }
            }
        }
        guard let minError = errors.min(), minError <= 80 else { return -1 }
        if errors[0] <= minError * 1.15 + 1 { return 0 }
        if errors[0] < minError * 4 { return -1 }
        var best = -1, bestDistance = Int.max
        for shift in 1..<count {
            if errors[shift] > minError * 1.1 + 0.5 { continue }
            if errors[shift] > errors[shift - 1] || (shift + 1 < count && errors[shift] > errors[shift + 1]) { continue }
            let distance = expected > 0 ? abs(shift - expected) : shift
            if distance < bestDistance { bestDistance = distance; best = shift }
        }
        return best
    }

    /// Rows that stayed put at the top and the bottom, ending at the last row with some content.
    static func fixedEdges(previous: [UInt64], previousPlain: [Bool], current: [UInt64]) -> (Int, Int) {
        let n = previous.count
        let limit = n / 3
        func run(_ start: Int, _ step: Int) -> Int {
            var count = 0, lastContent = 0, y = start
            while count < limit, y >= 0, y < n, previous[y] == current[y] {
                count += 1
                if !previousPlain[y] { lastContent = count }
                y += step
            }
            return lastContent
        }
        return (run(0, 1), run(n - 1, -1))
    }

    /// How many pixels the part between `top` and `bottom` moved up. 0 = nothing moved, -1 = no shift fits.
    static func findShift(previous: [UInt64], current: [UInt64], currentPlain: [Bool], top: Int, bottom: Int, expected: Int) -> Int {
        let height = bottom - top
        if height < 8 { return -1 }
        var bestShift = -1, bestScore = 0, bestDistance = Int.max
        for shift in 0..<(height - 4) {
            var matches = 0, misses = 0
            for i in 0..<(height - shift) {
                let y = top + i
                if currentPlain[y] { continue }
                if current[y] == previous[y + shift] { matches += 1 } else { misses += 1 }
            }
            let total = matches + misses
            if total == 0 || matches < min(6, total) || matches * 10 < total * 9 { continue }
            let score = matches - 4 * misses
            let distance = expected > 0 ? abs(shift - expected) : shift
            if score > bestScore || (score == bestScore && distance < bestDistance) {
                bestScore = score
                bestShift = shift
                bestDistance = distance
            }
        }
        return bestShift
    }
}
