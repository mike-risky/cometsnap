import Foundation

/// One look for the capture screen, from the brand "Electric" palette (assets/BRAND.md).
/// Same themes and ids as Windows (src/Cometsnap.Core/Settings/CaptureThemes.cs).
/// accent: frame, glow, loupe border, scrolling pill, Copied card. tag: the size label, with tagText on it.
public struct CaptureTheme: Equatable {
    public let id: String
    public let name: String
    public let accent: UInt32
    public let tag: UInt32
    public let tagText: UInt32
}

public enum CaptureThemes {
    public static let defaultID = "blue"
    private static let night: UInt32 = 0x07113A
    private static let white: UInt32 = 0xFFFFFF

    /// Names are English; the UI translates them with L.t.
    public static let all: [CaptureTheme] = [
        CaptureTheme(id: "blue", name: "Comet blue", accent: 0x3D6BFF, tag: 0xFFD60A, tagText: night),
        CaptureTheme(id: "yellow", name: "Sun yellow", accent: 0xFFD60A, tag: 0x3D6BFF, tagText: white),
        CaptureTheme(id: "terracotta", name: "Terracotta", accent: 0xE2553A, tag: 0xFFD60A, tagText: night),
        CaptureTheme(id: "orange", name: "Flame orange", accent: 0xF09625, tag: 0x3D6BFF, tagText: white),
        CaptureTheme(id: "stardust", name: "Stardust", accent: 0xA9B6F0, tag: 0xFFD60A, tagText: night),
    ]

    /// The theme with this id, or blue for empty or unknown ids.
    public static func get(_ id: String?) -> CaptureTheme {
        all.first { $0.id.lowercased() == (id ?? "").lowercased() } ?? all[0]
    }
}

/// Text forms of a picked color, for the color picker.
public enum ColorText {
    public static func hex(_ r: UInt8, _ g: UInt8, _ b: UInt8) -> String { String(format: "#%02X%02X%02X", r, g, b) }
    public static func rgb(_ r: UInt8, _ g: UInt8, _ b: UInt8) -> String { "rgb(\(r), \(g), \(b))" }
}
