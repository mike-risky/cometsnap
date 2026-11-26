import CoreGraphics
import Foundation

public enum LoupeCorner: String, Codable, CaseIterable {
    case topLeft, topRight, bottomLeft, bottomRight, followCursor

    public var title: String { L.t(englishTitle) }

    public var englishTitle: String {
        switch self {
        case .topLeft: return "Top left"
        case .topRight: return "Top right"
        case .bottomLeft: return "Bottom left"
        case .bottomRight: return "Bottom right"
        case .followCursor: return "Follow the cursor"
        }
    }
}

/// Decides where the magnifier loupe sits. It lives in a corner of the screen under the
/// cursor and jumps to another corner as soon as the cursor comes close, so it never hides
/// the pixels you are aiming at. It only returns to the preferred corner once the cursor has
/// clearly moved away again (no flicker at the boundary).
///
/// Coordinates are AppKit style: y grows upwards, so "top" is maxY.
public enum LoupePlacement {
    public static let margin: CGFloat = 16
    public static let avoidDistance: CGFloat = 48
    public static let returnHysteresis: CGFloat = 64
    public static let followOffset: CGFloat = 28

    public static func rect(for corner: LoupeCorner, in screen: CGRect, size: CGSize, margin: CGFloat = margin) -> CGRect {
        switch corner {
        case .topLeft:
            return CGRect(x: screen.minX + margin, y: screen.maxY - margin - size.height, width: size.width, height: size.height)
        case .topRight:
            return CGRect(x: screen.maxX - margin - size.width, y: screen.maxY - margin - size.height, width: size.width, height: size.height)
        case .bottomRight:
            return CGRect(x: screen.maxX - margin - size.width, y: screen.minY + margin, width: size.width, height: size.height)
        case .bottomLeft, .followCursor:
            return CGRect(x: screen.minX + margin, y: screen.minY + margin, width: size.width, height: size.height)
        }
    }

    /// Picks the corner to use now. `current` is the corner used for the previous mouse
    /// position (pass `preferred` the first time).
    public static func resolveCorner(preferred: LoupeCorner, current: LoupeCorner, screen: CGRect,
                                     size: CGSize, cursor: CGPoint) -> LoupeCorner {
        if preferred == .followCursor { return .followCursor }
        func near(_ c: LoupeCorner, _ extra: CGFloat) -> Bool {
            rect(for: c, in: screen, size: size).insetBy(dx: -(avoidDistance + extra), dy: -(avoidDistance + extra)).contains(cursor)
        }
        // Still hovering around the preferred corner: stay where we are if that spot is safe.
        if current != preferred && current != .followCursor && near(preferred, returnHysteresis) && !near(current, 0) {
            return current
        }
        for c in candidates(preferred) where !near(c, 0) { return c }
        return preferred
    }

    /// Rectangle for "follow cursor" mode: beside the cursor, flipped near screen edges.
    public static func followRect(in screen: CGRect, size: CGSize, cursor: CGPoint) -> CGRect {
        var x = cursor.x + followOffset
        var y = cursor.y - followOffset - size.height
        if x + size.width > screen.maxX - margin { x = cursor.x - followOffset - size.width }
        if y < screen.minY + margin { y = cursor.y + followOffset }
        return CGRect(x: x, y: y, width: size.width, height: size.height).kept(inside: screen)
    }

    /// Preferred first, then the horizontal neighbour, the vertical one, and the diagonal.
    public static func candidates(_ preferred: LoupeCorner) -> [LoupeCorner] {
        [preferred, flipH(preferred), flipV(preferred), flipV(flipH(preferred))]
    }

    static func flipH(_ c: LoupeCorner) -> LoupeCorner {
        switch c {
        case .topLeft: return .topRight
        case .topRight: return .topLeft
        case .bottomRight: return .bottomLeft
        default: return .bottomRight
        }
    }

    static func flipV(_ c: LoupeCorner) -> LoupeCorner {
        switch c {
        case .topLeft: return .bottomLeft
        case .topRight: return .bottomRight
        case .bottomRight: return .topRight
        default: return .topLeft
        }
    }
}

/// Pulls selection edges onto nearby window edges while dragging.
public enum EdgeSnapper {
    public static let threshold: CGFloat = 8

    /// Returns the candidate closest to `value` if it lies within `threshold`, else `value`.
    public static func snap(_ value: CGFloat, to candidates: [CGFloat], threshold: CGFloat = threshold) -> CGFloat {
        var best = value
        var bestDistance = threshold + 0.001
        for c in candidates {
            let d = abs(c - value)
            if d < bestDistance { best = c; bestDistance = d }
        }
        return best
    }

    /// Snaps a point to the x and y edges of the given rectangles.
    public static func snap(_ p: CGPoint, toEdgesOf rects: [CGRect], threshold: CGFloat = threshold) -> CGPoint {
        let xs = rects.flatMap { [$0.minX, $0.maxX] }
        let ys = rects.flatMap { [$0.minY, $0.maxY] }
        return CGPoint(x: snap(p.x, to: xs, threshold: threshold), y: snap(p.y, to: ys, threshold: threshold))
    }
}

public extension CGRect {
    /// Normalised rectangle spanning two points.
    init(corner a: CGPoint, corner b: CGPoint) {
        self.init(x: min(a.x, b.x), y: min(a.y, b.y), width: abs(a.x - b.x), height: abs(a.y - b.y))
    }

    /// Moves (not resizes) the rectangle so it fits inside `outer` when possible.
    func kept(inside outer: CGRect) -> CGRect {
        var r = self
        if r.maxX > outer.maxX { r.origin.x = outer.maxX - r.width }
        if r.minX < outer.minX { r.origin.x = outer.minX }
        if r.maxY > outer.maxY { r.origin.y = outer.maxY - r.height }
        if r.minY < outer.minY { r.origin.y = outer.minY }
        return r
    }

    /// Rounds the edges outwards to whole numbers (pixel-aligned crops).
    var pixelAligned: CGRect {
        let x0 = floor(minX), y0 = floor(minY), x1 = ceil(maxX), y1 = ceil(maxY)
        return CGRect(x: x0, y: y0, width: x1 - x0, height: y1 - y0)
    }
}

public enum Angles {
    /// Constrains `end` so the line from `start` points in a multiple of 45°.
    public static func snap45(from start: CGPoint, to end: CGPoint) -> CGPoint {
        let dx = end.x - start.x, dy = end.y - start.y
        let length = (dx * dx + dy * dy).squareRoot()
        guard length > 0 else { return end }
        let step = CGFloat.pi / 4
        let angle = (atan2(dy, dx) / step).rounded() * step
        return CGPoint(x: start.x + cos(angle) * length, y: start.y + sin(angle) * length)
    }

    /// Makes the box spanned by two points square (Shift while drawing a box or circle).
    public static func square(from start: CGPoint, to end: CGPoint) -> CGPoint {
        let dx = end.x - start.x, dy = end.y - start.y
        let side = max(abs(dx), abs(dy))
        return CGPoint(x: start.x + (dx < 0 ? -side : side), y: start.y + (dy < 0 ? -side : side))
    }
}
