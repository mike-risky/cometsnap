import Foundation

/// A line of recognized text and where it sits in the image (pixels, top-down).
public struct RecognizedLine: Equatable {
    public let text: String
    public let left: Double
    public let top: Double
    public let height: Double

    public init(text: String, left: Double, top: Double, height: Double) {
        self.text = text
        self.left = left
        self.top = top
        self.height = height
    }
}

/// Turns the lines an OCR engine found into clean text, ready for the clipboard.
/// Same rules as the Windows app (Cometsnap.Core/Text/OcrText.cs).
public enum OcrText {
    /// Joins lines in reading order. A gap taller than a line starts a new paragraph.
    public static func join(_ lines: [RecognizedLine]) -> String {
        var out = ""
        var previous: RecognizedLine?
        for line in lines where !line.text.trimmingCharacters(in: .whitespaces).isEmpty {
            if let p = previous {
                let gap = line.top - (p.top + p.height)
                let size = max(1, min(p.height, line.height))
                out += gap > size * 1.1 ? "\n\n" : "\n"
            }
            out += tidyLine(line.text)
            previous = line
        }
        return out
    }

    /// Removes spaces between two CJK characters and trims the line.
    public static func tidyLine(_ text: String) -> String {
        let chars = Array(text.trimmingCharacters(in: .whitespaces))
        guard chars.count >= 3 else { return String(chars) }
        var out = ""
        for (i, c) in chars.enumerated() {
            if c == " ", i > 0, i < chars.count - 1, isCjk(chars[i - 1]), isCjk(chars[i + 1]) { continue }
            out.append(c)
        }
        return out
    }

    public static func isCjk(_ c: Character) -> Bool {
        guard let v = c.unicodeScalars.first?.value else { return false }
        return (0x3000...0x30FF).contains(v) || (0x3400...0x4DBF).contains(v) || (0x4E00...0x9FFF).contains(v)
            || (0xF900...0xFAFF).contains(v) || (0xFF00...0xFF60).contains(v)
    }
}
