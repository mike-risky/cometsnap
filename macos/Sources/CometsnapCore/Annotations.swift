import CoreGraphics
import Foundation

/// The 13 editor tools, same set and keys as the Windows editor.
public enum EditorTool: String, CaseIterable, Codable {
    case select, arrow, line, rectangle, ellipse, text, callout, highlighter, pen, step, blur, spotlight, crop

    public var key: Character {
        switch self {
        case .select: return "v"
        case .arrow: return "a"
        case .line: return "l"
        case .rectangle: return "r"
        case .ellipse: return "e"
        case .text: return "t"
        case .callout: return "k"
        case .highlighter: return "h"
        case .pen: return "p"
        case .step: return "n"
        case .blur: return "b"
        case .spotlight: return "s"
        case .crop: return "c"
        }
    }

    public var label: String {
        switch self {
        case .select: return "Select"
        case .arrow: return "Arrow"
        case .line: return "Line"
        case .rectangle: return "Box"
        case .ellipse: return "Circle"
        case .text: return "Text"
        case .callout: return "Bubble"
        case .highlighter: return "Marker"
        case .pen: return "Pen"
        case .step: return "Steps"
        case .blur: return "Blur"
        case .spotlight: return "Spotlight"
        case .crop: return "Crop"
        }
    }

    public var tip: String {
        switch self {
        case .select: return "Select (V): click a shape to move, recolor or delete it"
        case .arrow: return "Arrow (A) · Shift = 45° steps"
        case .line: return "Line (L) · Shift = 45° steps"
        case .rectangle: return "Rectangle (R) · Shift = square"
        case .ellipse: return "Circle (E) · Shift = perfect circle"
        case .text: return "Text (T) · Return to finish, Option+Return for a new line"
        case .callout: return "Speech bubble (K): drag from what you point at to where the bubble goes"
        case .highlighter: return "Highlighter (H)"
        case .pen: return "Freehand pen (P)"
        case .step: return "Numbered steps (N): each click adds the next number"
        case .blur: return "Blur / pixelate (B): hide passwords, e-mails, faces"
        case .spotlight: return "Spotlight (S): greys out everything except the area you drag"
        case .crop: return "Crop (C): keep only the area you drag"
        }
    }

    /// SF Symbol used for the tool button.
    public var symbol: String {
        switch self {
        case .select: return "cursorarrow"
        case .arrow: return "arrow.up.right"
        case .line: return "line.diagonal"
        case .rectangle: return "rectangle"
        case .ellipse: return "circle"
        case .text: return "textformat"
        case .callout: return "bubble.left"
        case .highlighter: return "highlighter"
        case .pen: return "pencil.tip"
        case .step: return "1.circle"
        case .blur: return "square.grid.3x3.fill"
        case .spotlight: return "flashlight.on.fill"
        case .crop: return "crop"
        }
    }

    public static func forKey(_ c: Character) -> EditorTool? {
        allCases.first { $0.key == c }
    }
}

public struct RGBA: Codable, Equatable {
    public var r: Double, g: Double, b: Double, a: Double

    public init(r: Double, g: Double, b: Double, a: Double = 1) {
        self.r = r; self.g = g; self.b = b; self.a = a
    }

    public init?(hex: String) {
        var s = hex.trimmingCharacters(in: .whitespaces)
        if s.hasPrefix("#") { s.removeFirst() }
        guard s.count == 6 || s.count == 8, let v = UInt64(s, radix: 16) else { return nil }
        if s.count == 6 {
            self.init(r: Double((v >> 16) & 0xFF) / 255, g: Double((v >> 8) & 0xFF) / 255, b: Double(v & 0xFF) / 255)
        } else {
            self.init(r: Double((v >> 16) & 0xFF) / 255, g: Double((v >> 8) & 0xFF) / 255, b: Double(v & 0xFF) / 255,
                      a: Double((v >> 24) & 0xFF) / 255)
        }
    }

    public var hex: String {
        String(format: "#%02X%02X%02X", Int((r * 255).rounded()), Int((g * 255).rounded()), Int((b * 255).rounded()))
    }

    public var cgColor: CGColor { CGColor(srgbRed: r, green: g, blue: b, alpha: a) }

    public func withAlpha(_ alpha: Double) -> RGBA { RGBA(r: r, g: g, b: b, a: alpha) }

    /// Relative luminance, used to pick white or black text on top of the color.
    public var isLight: Bool { (0.299 * r + 0.587 * g + 0.114 * b) > 0.6 }

    public static let palette: [RGBA] = ["#E5484D", "#F09625", "#FFD60A", "#2ED573", "#3D6BFF", "#A55EEA", "#FFFFFF", "#111111"]
        .compactMap { RGBA(hex: $0) }
    public static let paletteNames = ["Red", "Orange", "Yellow", "Green", "Blue", "Purple", "White", "Black"]
}

public struct Pt: Codable, Equatable {
    public var x: Double, y: Double
    public init(_ p: CGPoint) { x = Double(p.x); y = Double(p.y) }
    public init(x: Double, y: Double) { self.x = x; self.y = y }
    public var cg: CGPoint { CGPoint(x: x, y: y) }
}

public enum ShapeKind: String, Codable {
    case arrow, line, rectangle, ellipse, text, callout, highlighter, pen, step, blur, spotlight
}

/// One editable shape. Coordinates are image pixels with the origin at the top-left.
public struct Annotation: Codable, Equatable, Identifiable {
    public var id: UUID = UUID()
    public var kind: ShapeKind
    public var points: [Pt]
    public var color: RGBA
    public var width: Double
    public var text: String = ""
    public var number: Int = 0

    public init(kind: ShapeKind, points: [Pt], color: RGBA, width: Double, text: String = "", number: Int = 0) {
        self.kind = kind
        self.points = points
        self.color = color
        self.width = width
        self.text = text
        self.number = number
    }

    public var start: CGPoint { points.first?.cg ?? .zero }
    public var end: CGPoint { points.last?.cg ?? .zero }

    /// Rectangle spanned by the first and last point.
    public var box: CGRect { CGRect(corner: start, corner: end) }

    public var fontSize: Double { 14 + width * 3 }

    /// Rough bounds used for hit testing and selection outlines.
    public var bounds: CGRect {
        switch kind {
        case .step:
            let r = stepRadius
            return CGRect(x: start.x - r, y: start.y - r, width: r * 2, height: r * 2)
        case .text:
            let lines = max(1, text.components(separatedBy: "\n").count)
            let longest = text.components(separatedBy: "\n").map(\.count).max() ?? 1
            return CGRect(x: start.x, y: start.y, width: max(20, Double(longest) * fontSize * 0.6), height: Double(lines) * fontSize * 1.3)
        case .callout:
            return box.union(CGRect(origin: start, size: .zero))
        default:
            var minX = Double.greatestFiniteMagnitude, minY = minX, maxX = -minX, maxY = -minX
            for p in points { minX = min(minX, p.x); minY = min(minY, p.y); maxX = max(maxX, p.x); maxY = max(maxY, p.y) }
            if points.isEmpty { return .zero }
            return CGRect(x: minX, y: minY, width: maxX - minX, height: maxY - minY)
        }
    }

    public var stepRadius: Double { 12 + width * 2.5 }

    public func hitTest(_ p: CGPoint, tolerance: CGFloat) -> Bool {
        switch kind {
        case .arrow, .line:
            return distance(p, toSegment: start, end) <= tolerance + width
        case .pen, .highlighter:
            for i in 1..<max(1, points.count) where distance(p, toSegment: points[i - 1].cg, points[i].cg) <= tolerance + width {
                return true
            }
            return false
        case .rectangle, .ellipse:
            let outer = box.insetBy(dx: -(tolerance + width), dy: -(tolerance + width))
            let inner = box.insetBy(dx: tolerance + width, dy: tolerance + width)
            return outer.contains(p) && (inner.isNull || inner.isEmpty || !inner.contains(p))
        default:
            return bounds.insetBy(dx: -tolerance, dy: -tolerance).contains(p)
        }
    }

    public mutating func move(by d: CGSize) {
        points = points.map { Pt(x: $0.x + Double(d.width), y: $0.y + Double(d.height)) }
    }
}

func distance(_ p: CGPoint, toSegment a: CGPoint, _ b: CGPoint) -> CGFloat {
    let dx = b.x - a.x, dy = b.y - a.y
    let len2 = dx * dx + dy * dy
    if len2 == 0 { return hypot(p.x - a.x, p.y - a.y) }
    let t = max(0, min(1, ((p.x - a.x) * dx + (p.y - a.y) * dy) / len2))
    return hypot(p.x - (a.x + t * dx), p.y - (a.y + t * dy))
}

/// Everything needed to re-open a capture with its shapes still editable.
public struct AnnotationDocument: Codable, Equatable {
    public var version = 1
    public var annotations: [Annotation] = []
    /// Crop in image pixels (top-left origin), nil = whole image.
    public var crop: CGRect?
    /// Constellation mode: numbered steps are joined, in order, by faint star lines.
    public var connectSteps = false

    public init() {}

    enum CodingKeys: String, CodingKey { case version, annotations, crop, connectSteps }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        version = (try? c.decodeIfPresent(Int.self, forKey: .version)) ?? 1
        annotations = try c.decodeIfPresent([Annotation].self, forKey: .annotations) ?? []
        crop = try? c.decodeIfPresent(CGRect.self, forKey: .crop)
        connectSteps = (try? c.decodeIfPresent(Bool.self, forKey: .connectSteps)) ?? false
    }

    /// Steps in number order: the path the constellation follows.
    public var orderedSteps: [Annotation] {
        annotations.filter { $0.kind == .step }.sorted { $0.number < $1.number }
    }

    public var nextStepNumber: Int {
        (annotations.filter { $0.kind == .step }.map(\.number).max() ?? 0) + 1
    }

    /// Topmost shape under the point, if any.
    public func hit(_ p: CGPoint, tolerance: CGFloat) -> Int? {
        annotations.indices.reversed().first { annotations[$0].hitTest(p, tolerance: tolerance) }
    }

    public func encoded() -> Data? {
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted]
        return try? enc.encode(self)
    }

    public static func decode(_ data: Data) -> AnnotationDocument? {
        try? JSONDecoder().decode(AnnotationDocument.self, from: data)
    }
}

/// Undo/redo as whole-document snapshots: simple and always correct.
public struct UndoStack<T> {
    public private(set) var past: [T] = []
    public private(set) var future: [T] = []
    public let limit: Int

    public init(limit: Int = 200) { self.limit = limit }

    public mutating func push(_ state: T) {
        past.append(state)
        if past.count > limit { past.removeFirst() }
        future.removeAll()
    }

    public mutating func undo(current: T) -> T? {
        guard let prev = past.popLast() else { return nil }
        future.append(current)
        return prev
    }

    public mutating func redo(current: T) -> T? {
        guard let next = future.popLast() else { return nil }
        past.append(current)
        return next
    }

    public var canUndo: Bool { !past.isEmpty }
    public var canRedo: Bool { !future.isEmpty }
}
