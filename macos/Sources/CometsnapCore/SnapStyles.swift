import Foundation

/// What goes behind a styled capture.
public enum StyleBackdrop: String, Codable, CaseIterable {
    /// Transparent: just the capture with its shadow, border and glow.
    case none
    /// One solid color.
    case color
    /// A diagonal gradient between two colors.
    case gradient
    /// The capture itself, blurred and enlarged, like frosted glass.
    case blur
    /// Deep space: a night gradient between the two colors, with stars.
    case space
}

/// A fully custom "share-ready" look, built in the Style studio: rounded corners, border, drop
/// shadow (offset, blur, color, opacity), laser or neon glow, and a backdrop. Every part can be on
/// or off, so they combine freely. Sizes are in points (pixels at 1x). Colors are "#RRGGBB".
/// Same fields and defaults as Windows/Linux (src/Cometsnap.Core/Styling/SnapStyles.cs).
public struct SnapStyle: Codable, Equatable {
    /// Corner radius; 0 = square corners.
    public var cornerRadius = 12
    public var border = false
    public var borderWidth = 3
    public var borderColor = "#FFFFFF"
    public var shadow = true
    /// Shadow offset: positive X = right, positive Y = down.
    public var shadowX = 0
    public var shadowY = 10
    public var shadowBlur = 28
    public var shadowColor = "#000000"
    /// 0 to 100.
    public var shadowOpacity = 45
    /// Glow around the edge: small = a laser line, big = neon.
    public var glow = false
    public var glowColor = "#3D6BFF"
    public var glowSize = 18
    /// 0 to 100.
    public var glowStrength = 85
    public var backdrop = StyleBackdrop.none
    public var backdropColor = "#3D6BFF"
    public var backdropColor2 = "#07113A"
    /// Space around the capture when there is a backdrop.
    public var padding = 64

    public init() {}

    enum CodingKeys: String, CodingKey {
        case cornerRadius, border, borderWidth, borderColor, shadow, shadowX, shadowY, shadowBlur, shadowColor, shadowOpacity,
             glow, glowColor, glowSize, glowStrength, backdrop, backdropColor, backdropColor2, padding
    }

    /// Tolerant: a missing or odd field keeps its default (a hand-edited settings file can hold anything).
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = SnapStyle()
        func int(_ k: CodingKeys, _ v: Int) -> Int { ((try? c.decodeIfPresent(Int.self, forKey: k)) ?? nil) ?? v }
        func bool(_ k: CodingKeys, _ v: Bool) -> Bool { ((try? c.decodeIfPresent(Bool.self, forKey: k)) ?? nil) ?? v }
        func str(_ k: CodingKeys, _ v: String) -> String { ((try? c.decodeIfPresent(String.self, forKey: k)) ?? nil) ?? v }
        cornerRadius = int(.cornerRadius, d.cornerRadius)
        border = bool(.border, d.border)
        borderWidth = int(.borderWidth, d.borderWidth)
        borderColor = str(.borderColor, d.borderColor)
        shadow = bool(.shadow, d.shadow)
        shadowX = int(.shadowX, d.shadowX)
        shadowY = int(.shadowY, d.shadowY)
        shadowBlur = int(.shadowBlur, d.shadowBlur)
        shadowColor = str(.shadowColor, d.shadowColor)
        shadowOpacity = int(.shadowOpacity, d.shadowOpacity)
        glow = bool(.glow, d.glow)
        glowColor = str(.glowColor, d.glowColor)
        glowSize = int(.glowSize, d.glowSize)
        glowStrength = int(.glowStrength, d.glowStrength)
        backdrop = StyleBackdrop(rawValue: str(.backdrop, "none").lowercased()) ?? .none
        backdropColor = str(.backdropColor, d.backdropColor)
        backdropColor2 = str(.backdropColor2, d.backdropColor2)
        padding = int(.padding, d.padding)
        self = normalized()
    }

    /// Keeps every number in a sensible range.
    public func normalized() -> SnapStyle {
        func clamp(_ v: Int, _ lo: Int, _ hi: Int) -> Int { min(hi, max(lo, v)) }
        var s = self
        s.cornerRadius = clamp(s.cornerRadius, 0, 200)
        s.borderWidth = clamp(s.borderWidth, 1, 60)
        s.shadowX = clamp(s.shadowX, -200, 200)
        s.shadowY = clamp(s.shadowY, -200, 200)
        s.shadowBlur = clamp(s.shadowBlur, 0, 200)
        s.shadowOpacity = clamp(s.shadowOpacity, 0, 100)
        s.glowSize = clamp(s.glowSize, 1, 200)
        s.glowStrength = clamp(s.glowStrength, 0, 100)
        s.padding = clamp(s.padding, 0, 600)
        s.borderColor = StyleColors.normalize(s.borderColor, "#FFFFFF")
        s.shadowColor = StyleColors.normalize(s.shadowColor, "#000000")
        s.glowColor = StyleColors.normalize(s.glowColor, "#3D6BFF")
        s.backdropColor = StyleColors.normalize(s.backdropColor, "#3D6BFF")
        s.backdropColor2 = StyleColors.normalize(s.backdropColor2, "#07113A")
        return s
    }
}

/// Ready-made starting points for the Style studio. Picking one copies it; then everything can be changed.
public enum SnapStylePresets {
    /// Ids and English names (the UI translates them with L.t).
    public static let all: [(id: String, name: String)] = [
        ("clean", "Clean"), ("showcase", "Showcase"), ("frosted", "Frosted"),
        ("neon", "Neon"), ("laser", "Laser"), ("comet", "Comet"), ("polaroid", "Polaroid"),
    ]

    public static func get(_ id: String?) -> SnapStyle {
        var s = SnapStyle()
        switch (id ?? "").lowercased() {
        case "showcase":
            s.backdrop = .gradient; s.backdropColor = "#6F8FFF"; s.backdropColor2 = "#1C2C77"; s.padding = 64; s.shadowY = 14; s.shadowBlur = 36
        case "frosted":
            s.backdrop = .blur; s.padding = 64; s.shadowY = 12; s.shadowBlur = 32; s.border = true; s.borderWidth = 2; s.borderColor = "#FFFFFF"
        case "neon":
            s.shadow = false; s.glow = true; s.glowColor = "#FF2BD6"; s.glowSize = 36; s.glowStrength = 90
            s.backdrop = .color; s.backdropColor = "#0B0B1E"; s.padding = 72
        case "laser":
            s.shadow = false; s.glow = true; s.glowColor = "#FF3B30"; s.glowSize = 10; s.glowStrength = 100; s.cornerRadius = 6
        case "comet":
            s.shadow = false; s.glow = true; s.glowColor = "#3D6BFF"; s.glowSize = 24
            s.backdrop = .space; s.backdropColor = "#1A2470"; s.backdropColor2 = "#07113A"; s.padding = 64
        case "polaroid":
            s.cornerRadius = 0; s.border = true; s.borderWidth = 14; s.borderColor = "#FFFFFF"
            s.shadowX = 8; s.shadowY = 10; s.shadowBlur = 16; s.shadowOpacity = 40
        default: break
        }
        return s
    }
}

/// "#RRGGBB" helpers.
public enum StyleColors {
    /// A palette for the color chips (the hex box and color panel take any color).
    public static let palette = ["#000000", "#FFFFFF", "#8A8FA3", "#FF3B30", "#E2553A", "#F09625", "#FFD60A",
                                 "#34C759", "#00E5FF", "#3D6BFF", "#7B5CFF", "#FF2BD6", "#07113A"]

    /// Parses "#RGB", "#RRGGBB" or "RRGGBB" into 0xRRGGBB; nil when it isn't a color.
    public static func tryParse(_ text: String?) -> UInt32? {
        guard var t = text?.trimmingCharacters(in: .whitespaces), !t.isEmpty else { return nil }
        while t.hasPrefix("#") { t.removeFirst() }
        if t.count == 3 { t = t.map { "\($0)\($0)" }.joined() }
        guard t.count == 6, t.allSatisfy(\.isHexDigit) else { return nil }
        return UInt32(t, radix: 16)
    }

    public static func parse(_ text: String?, _ fallback: UInt32) -> UInt32 { tryParse(text) ?? fallback }

    public static func hex(_ rgb: UInt32) -> String { String(format: "#%06X", rgb & 0xFFFFFF) }

    public static func normalize(_ text: String?, _ fallback: String) -> String { tryParse(text).map(hex) ?? fallback }
}

public enum StyledFiles {
    /// "snap_1.png" → "snap_1-styled.png", in the same folder.
    public static func styledURL(_ url: URL) -> URL {
        let ext = url.pathExtension
        let name = url.deletingPathExtension().lastPathComponent + "-styled"
        let dir = url.deletingLastPathComponent()
        return ext.isEmpty ? dir.appendingPathComponent(name) : dir.appendingPathComponent(name).appendingPathExtension(ext)
    }
}

/// Styled pixels: 32-bit BGRA, straight (not premultiplied) alpha, rows of width * 4 bytes.
public struct StyledPixels {
    public let width: Int
    public let height: Int
    public var bgra: [UInt8]
}

/// Draws a capture in a SnapStyle: backdrop, drop shadow, glow, border, then the capture with
/// rounded corners. A line-by-line port of src/Cometsnap.Core/Styling/StyleRenderer.cs, so the
/// three apps look the same.
public enum StyleRenderer {
    public struct Layout: Equatable {
        public let width: Int
        public let height: Int
        public let imageX: Int
        public let imageY: Int
    }

    /// Canvas size and where the capture sits. scale multiplies every size of the style
    /// (2 on a Retina screen, so 10 points of shadow look the same everywhere).
    public static func measure(width: Int, height: Int, style: SnapStyle, scale: Double = 1) -> Layout {
        let st = style.normalized()
        let bw = st.border ? Double(st.borderWidth) * scale : 0
        var left: Double, right: Double, top: Double, bottom: Double
        if st.backdrop != .none {
            left = Double(st.padding) * scale + bw
            right = left; top = left; bottom = left
        } else {
            let glow = st.glow ? Double(st.glowSize) * scale * 1.3 + lineWidth(st, scale) : 0
            let shadow = st.shadow && st.shadowOpacity > 0 ? Double(st.shadowBlur) * scale * 1.6 + 1 : 0
            let sx = st.shadow ? Double(st.shadowX) * scale : 0
            let sy = st.shadow ? Double(st.shadowY) * scale : 0
            left = bw + max(glow, shadow > 0 ? shadow - sx : 0)
            right = bw + max(glow, shadow > 0 ? shadow + sx : 0)
            top = bw + max(glow, shadow > 0 ? shadow - sy : 0)
            bottom = bw + max(glow, shadow > 0 ? shadow + sy : 0)
        }
        func l(_ v: Double) -> Int { Int(max(0, v).rounded(.up)) }
        return Layout(width: width + l(left) + l(right), height: height + l(top) + l(bottom), imageX: l(left), imageY: l(top))
    }

    /// Width of the bright line of a glow: thin, like a laser, whatever the reach.
    static func lineWidth(_ st: SnapStyle, _ scale: Double) -> Double { max(1.5 * scale, min(4 * scale, Double(st.glowSize) * scale / 8)) }

    /// Styles a capture given as 32-bit BGRA/BGRX (alpha ignored: a screenshot is opaque).
    public static func render(_ source: [UInt8], width: Int, height: Int, stride: Int, style: SnapStyle, scale: Double = 1) -> StyledPixels {
        precondition(width > 0 && height > 0)
        let st = style.normalized()
        let m = measure(width: width, height: height, style: st, scale: scale)
        let outW = m.width, outH = m.height
        var dst = [UInt8](repeating: 0, count: outW * outH * 4)

        let left = Double(m.imageX), top = Double(m.imageY)
        let cx = left + Double(width) / 2, cy = top + Double(height) / 2
        let hx = Double(width) / 2, hy = Double(height) / 2
        let r = min(Double(st.cornerRadius) * scale, Double(min(width, height)) / 2)
        let bw = st.border ? Double(st.borderWidth) * scale : 0
        let ohx = hx + bw, ohy = hy + bw
        let orad = r > 0 ? r + bw : 0

        let shadowOn = st.shadow && st.shadowOpacity > 0
        let shadowAlpha = Double(st.shadowOpacity) / 100
        let sigma = Double(st.shadowBlur) * scale / 2
        let sx = Double(st.shadowX) * scale, sy = Double(st.shadowY) * scale
        let (shr, shg, shb) = split(StyleColors.parse(st.shadowColor, 0))

        let glowOn = st.glow && st.glowStrength > 0
        let glowReach = Double(st.glowSize) * scale
        let glowStrength = Double(st.glowStrength) / 100
        let line = lineWidth(st, scale)
        let (gr, gg, gb) = split(StyleColors.parse(st.glowColor, 0x3D6BFF))
        let (br, bg, bb) = split(StyleColors.parse(st.borderColor, 0xFFFFFF))
        let inner = Int(r.rounded(.up)) + 1

        dst.withUnsafeMutableBufferPointer { d in
            source.withUnsafeBufferPointer { s in
                fillBackdrop(d, outW, outH, st, s, width, height, stride)
                let dp = d.baseAddress!, sp = s.baseAddress!

                DispatchQueue.concurrentPerform(iterations: outH) { y in
                    let row = y * outW * 4
                    let py = Double(y) + 0.5
                    let insideRows = Double(y) >= top + Double(inner) && Double(y) < top + Double(height) - Double(inner)
                    var x = 0
                    while x < outW {
                        if insideRows && x == m.imageX + 1 && width > 2 {
                            // Fast path: the whole middle of this row is the capture, as is.
                            let count = width - 2
                            let si = (y - m.imageY) * stride + 4
                            let di = row + x * 4
                            for p in 0..<count {
                                dp[di + p * 4] = sp[si + p * 4]
                                dp[di + p * 4 + 1] = sp[si + p * 4 + 1]
                                dp[di + p * 4 + 2] = sp[si + p * 4 + 2]
                                dp[di + p * 4 + 3] = 255
                            }
                            x += count
                            continue
                        }
                        let px = Double(x) + 0.5
                        let i = row + x * 4
                        var a = Double(dp[i + 3]) / 255
                        var b = Double(dp[i]) / 255 * a, g = Double(dp[i + 1]) / 255 * a, rr = Double(dp[i + 2]) / 255 * a

                        let dist = roundedBoxDistance(px - cx, py - cy, hx, hy, r)
                        let dOuter = bw > 0 ? roundedBoxDistance(px - cx, py - cy, ohx, ohy, orad) : dist

                        if shadowOn && dOuter > -1 {
                            let ds = roundedBoxDistance(px - cx - sx, py - cy - sy, ohx, ohy, orad)
                            let cover = sigma < 0.5 ? min(1, max(0, 0.5 - ds)) : 0.5 * erfc(ds / (sigma * 1.41421356))
                            over(&b, &g, &rr, &a, shb, shg, shr, shadowAlpha * cover)
                        }
                        if glowOn && dOuter > -1 {
                            let dd = max(0, dOuter)
                            let t = max(0, 1 - dd / glowReach)
                            let ga = 0.8 * t * t + (dd < line ? 1 - dd / line : 0)
                            over(&b, &g, &rr, &a, gb, gg, gr, glowStrength * min(1, ga))
                        }
                        if bw > 0 {
                            let cover = min(1, max(0, 0.5 - dOuter))
                            if cover > 0 { over(&b, &g, &rr, &a, bb, bg, br, cover) }
                        }
                        // The capture, with an anti-aliased rounded edge.
                        let imageCover = min(1, max(0, 0.5 - dist))
                        if imageCover > 0 {
                            let ix = min(max(x - m.imageX, 0), width - 1)
                            let iy = min(max(y - m.imageY, 0), height - 1)
                            let si = iy * stride + ix * 4
                            over(&b, &g, &rr, &a, Double(sp[si]) / 255, Double(sp[si + 1]) / 255, Double(sp[si + 2]) / 255, imageCover)
                        }
                        if a <= 0.0001 {
                            dp[i] = 0; dp[i + 1] = 0; dp[i + 2] = 0; dp[i + 3] = 0
                        } else {
                            dp[i] = toByte(b / a)
                            dp[i + 1] = toByte(g / a)
                            dp[i + 2] = toByte(rr / a)
                            dp[i + 3] = toByte(a)
                        }
                        x += 1
                    }
                }
            }
        }
        return StyledPixels(width: outW, height: outH, bgra: dst)
    }

    /// True when some pixel is not fully opaque.
    public static func hasTransparency(_ image: StyledPixels) -> Bool {
        var i = 3
        while i < image.bgra.count {
            if image.bgra[i] != 255 { return true }
            i += 4
        }
        return false
    }

    public static func roundedBoxDistance(_ px: Double, _ py: Double, _ hx: Double, _ hy: Double, _ r: Double) -> Double {
        let qx = abs(px) - (hx - r)
        let qy = abs(py) - (hy - r)
        let ox = max(qx, 0), oy = max(qy, 0)
        return (ox * ox + oy * oy).squareRoot() + min(max(qx, qy), 0) - r
    }

    // MARK: backdrops

    private static func fillBackdrop(_ d: UnsafeMutableBufferPointer<UInt8>, _ w: Int, _ h: Int, _ st: SnapStyle,
                                     _ s: UnsafeBufferPointer<UInt8>, _ sw: Int, _ sh: Int, _ stride: Int) {
        let c1 = StyleColors.parse(st.backdropColor, 0x3D6BFF)
        let c2 = StyleColors.parse(st.backdropColor2, 0x07113A)
        switch st.backdrop {
        case .color: fillGradient(d, w, h, c1, c1)
        case .gradient: fillGradient(d, w, h, c1, c2)
        case .space:
            fillGradient(d, w, h, c1, c2)
            stars(d, w, h)
        case .blur: fillBlur(d, w, h, s, sw, sh, stride)
        case .none: break
        }
    }

    private static func fillGradient(_ d: UnsafeMutableBufferPointer<UInt8>, _ w: Int, _ h: Int, _ from: UInt32, _ to: UInt32) {
        let r0 = Double((from >> 16) & 0xFF), g0 = Double((from >> 8) & 0xFF), b0 = Double(from & 0xFF)
        let r1 = Double((to >> 16) & 0xFF), g1 = Double((to >> 8) & 0xFF), b1 = Double(to & 0xFF)
        let span = max(1.0, Double(w + h - 2))
        let dp = d.baseAddress!
        DispatchQueue.concurrentPerform(iterations: h) { y in
            let row = y * w * 4
            for x in 0..<w {
                let t = Double(x + y) / span
                let i = row + x * 4
                dp[i] = UInt8(b0 + (b1 - b0) * t)
                dp[i + 1] = UInt8(g0 + (g1 - g0) * t)
                dp[i + 2] = UInt8(r0 + (r1 - r0) * t)
                dp[i + 3] = 255
            }
        }
    }

    /// A sprinkle of soft stars, always the same pattern for the same size.
    private static func stars(_ d: UnsafeMutableBufferPointer<UInt8>, _ w: Int, _ h: Int) {
        var seed: UInt64 = 2026
        func next() -> Double {
            seed = seed &* 6364136223846793005 &+ 1442695040888963407
            return Double(seed >> 11) / Double(1 << 53)
        }
        let count = min(1500, max(12, w * h / 5000))
        for _ in 0..<count {
            let x = Int(next() * Double(w)), y = Int(next() * Double(h))
            let big = next() < 0.12
            let bright = 0.35 + next() * 0.65
            let rad = big ? 1.6 : 0.9
            for dy in -2...2 {
                for dx in -2...2 {
                    let xx = x + dx, yy = y + dy
                    if xx < 0 || yy < 0 || xx >= w || yy >= h { continue }
                    let dist = Double(dx * dx + dy * dy).squareRoot()
                    let alpha = bright * min(1, max(0, 1 - dist / (rad + 0.6)))
                    if alpha <= 0 { continue }
                    let i = (yy * w + xx) * 4
                    for c in 0..<3 { d[i + c] = UInt8(Double(d[i + c]) + (255 - Double(d[i + c])) * alpha) }
                }
            }
        }
    }

    /// The capture, shrunk, blurred and stretched to cover the canvas, a little darker.
    private static func fillBlur(_ d: UnsafeMutableBufferPointer<UInt8>, _ w: Int, _ h: Int,
                                 _ s: UnsafeBufferPointer<UInt8>, _ sw: Int, _ sh: Int, _ stride: Int) {
        let scale = Double(max(sw, sh)) / 64.0
        let tw = max(1, Int((Double(sw) / scale).rounded()))
        let th = max(1, Int((Double(sh) / scale).rounded()))
        var small = [Double](repeating: 0, count: tw * th * 3)
        for ty in 0..<th {
            let y0 = ty * sh / th, y1 = max(y0 + 1, (ty + 1) * sh / th)
            for tx in 0..<tw {
                let x0 = tx * sw / tw, x1 = max(x0 + 1, (tx + 1) * sw / tw)
                var b = 0.0, g = 0.0, r = 0.0, n = 0.0
                let stepY = max(1, (y1 - y0) / 8), stepX = max(1, (x1 - x0) / 8)
                var y = y0
                while y < y1 {
                    var x = x0
                    while x < x1 {
                        let i = y * stride + x * 4
                        b += Double(s[i]); g += Double(s[i + 1]); r += Double(s[i + 2]); n += 1
                        x += stepX
                    }
                    y += stepY
                }
                let o = (ty * tw + tx) * 3
                small[o] = b / n; small[o + 1] = g / n; small[o + 2] = r / n
            }
        }
        for _ in 0..<3 { boxBlur(&small, tw, th, 3) }

        let cover = max(Double(w) / Double(tw), Double(h) / Double(th))
        let offX = (Double(tw) * cover - Double(w)) / 2
        let offY = (Double(th) * cover - Double(h)) / 2
        let dp = d.baseAddress!
        small.withUnsafeBufferPointer { sm in
            let smp = sm.baseAddress!
            DispatchQueue.concurrentPerform(iterations: h) { y in
                let fy = min(max((Double(y) + 0.5 + offY) / cover - 0.5, 0), Double(th - 1))
                let y0 = Int(fy), y1 = min(y0 + 1, th - 1)
                let wy = fy - Double(y0)
                let row = y * w * 4
                for x in 0..<w {
                    let fx = min(max((Double(x) + 0.5 + offX) / cover - 0.5, 0), Double(tw - 1))
                    let x0 = Int(fx), x1 = min(x0 + 1, tw - 1)
                    let wx = fx - Double(x0)
                    let i = row + x * 4
                    for c in 0..<3 {
                        let top = smp[(y0 * tw + x0) * 3 + c] * (1 - wx) + smp[(y0 * tw + x1) * 3 + c] * wx
                        let bot = smp[(y1 * tw + x0) * 3 + c] * (1 - wx) + smp[(y1 * tw + x1) * 3 + c] * wx
                        dp[i + c] = toByte((top * (1 - wy) + bot * wy) * 0.82 / 255.0)
                    }
                    dp[i + 3] = 255
                }
            }
        }
    }

    private static func boxBlur(_ px: inout [Double], _ w: Int, _ h: Int, _ radius: Int) {
        var tmp = [Double](repeating: 0, count: px.count)
        for y in 0..<h {
            for x in 0..<w {
                for c in 0..<3 {
                    var sum = 0.0
                    for k in -radius...radius { sum += px[(y * w + min(max(x + k, 0), w - 1)) * 3 + c] }
                    tmp[(y * w + x) * 3 + c] = sum / Double(2 * radius + 1)
                }
            }
        }
        for y in 0..<h {
            for x in 0..<w {
                for c in 0..<3 {
                    var sum = 0.0
                    for k in -radius...radius { sum += tmp[(min(max(y + k, 0), h - 1) * w + x) * 3 + c] }
                    px[(y * w + x) * 3 + c] = sum / Double(2 * radius + 1)
                }
            }
        }
    }

    // MARK: helpers

    private static func over(_ b: inout Double, _ g: inout Double, _ r: inout Double, _ a: inout Double,
                             _ sb: Double, _ sg: Double, _ sr: Double, _ alpha: Double) {
        b = sb * alpha + b * (1 - alpha)
        g = sg * alpha + g * (1 - alpha)
        r = sr * alpha + r * (1 - alpha)
        a = alpha + a * (1 - alpha)
    }

    private static func toByte(_ v: Double) -> UInt8 { UInt8(min(255, max(0, Int((v * 255).rounded())))) }

    static func erfc(_ x: Double) -> Double {
        let z = abs(x)
        let t = 1 / (1 + 0.3275911 * z)
        let y = t * (0.254829592 + t * (-0.284496736 + t * (1.421413741 + t * (-1.453152027 + t * 1.061405429)))) * exp(-z * z)
        return x >= 0 ? y : 2 - y
    }

    static func split(_ rgb: UInt32) -> (Double, Double, Double) {
        (Double((rgb >> 16) & 0xFF) / 255, Double((rgb >> 8) & 0xFF) / 255, Double(rgb & 0xFF) / 255)
    }
}
