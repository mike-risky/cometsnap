import Foundation

/// What goes behind a styled capture.
public enum StyleBackdrop {
    /// Transparent: just the capture (and its shadow or glow).
    case none
    /// A diagonal gradient in the capture color.
    case gradient
    /// The capture itself, blurred and enlarged, like frosted glass.
    case blur
    /// Deep space: navy night with a few stars.
    case space
}

/// A "share-ready" look: rounded corners, soft shadow, glowing outline, backdrop.
/// Same styles and ids as Windows (src/Cometsnap.Core/Styling/SnapStyles.cs).
public struct SnapStyle: Equatable {
    public let id: String
    public let name: String
    public let corners: Bool
    public let shadow: Bool
    public let glow: Bool
    public let backdrop: StyleBackdrop
}

public enum SnapStyles {
    public static let defaultID = "clean"

    /// Names are English; the UI translates them with L.t.
    public static let all: [SnapStyle] = [
        SnapStyle(id: "clean", name: "Clean", corners: true, shadow: true, glow: false, backdrop: .none),
        SnapStyle(id: "showcase", name: "Showcase", corners: true, shadow: true, glow: false, backdrop: .gradient),
        SnapStyle(id: "frosted", name: "Frosted", corners: true, shadow: true, glow: false, backdrop: .blur),
        SnapStyle(id: "comet", name: "Comet", corners: true, shadow: false, glow: true, backdrop: .space),
        SnapStyle(id: "laser", name: "Laser", corners: true, shadow: false, glow: true, backdrop: .none),
    ]

    /// The style with this id, or Clean for empty or unknown ids.
    public static func get(_ id: String?) -> SnapStyle {
        all.first { $0.id.lowercased() == (id ?? "").lowercased() } ?? all[0]
    }

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

/// Draws a capture in a SnapStyle, pixel by pixel. A port of the Windows/Linux renderer
/// (src/Cometsnap.Core/Styling/StyleRenderer.cs), so the three apps look the same.
public enum StyleRenderer {
    public struct Layout: Equatable {
        public let width: Int
        public let height: Int
        public let imageX: Int
        public let imageY: Int
        public let radius: Double
        public let shadowBlur: Double
        public let shadowOffset: Double
        public let glowSize: Double
        public let lineWidth: Double
    }

    public static func measure(width: Int, height: Int, style: SnapStyle) -> Layout {
        let k = min(2.0, max(0.5, (Double(width) * Double(height)).squareRoot() / 1100.0))
        let radius = style.corners ? min(12 * k, Double(min(width, height)) / 4.0) : 0
        let blur = style.shadow ? 16 * k : 0
        let offset = style.shadow ? 6 * k : 0
        let glow = style.glow ? 20 * k : 0
        let line = style.glow ? max(1.5, 2 * k) : 0
        var pad: Int
        if style.backdrop != .none { pad = max(40, Int((56 * k).rounded())) }
        else { pad = Int(max(blur * 2.2 + offset, glow * 1.6).rounded(.up)) }
        pad = max(pad, 0)
        return Layout(width: width + 2 * pad, height: height + 2 * pad, imageX: pad, imageY: pad,
                      radius: radius, shadowBlur: blur, shadowOffset: offset, glowSize: glow, lineWidth: line)
    }

    /// Styles a capture given as 32-bit BGRA/BGRX (alpha ignored). accent is 0xRRGGBB.
    public static func render(_ source: [UInt8], width: Int, height: Int, stride: Int, style: SnapStyle, accent: UInt32) -> StyledPixels {
        precondition(width > 0 && height > 0)
        let m = measure(width: width, height: height, style: style)
        let outW = m.width, outH = m.height
        var dst = [UInt8](repeating: 0, count: outW * outH * 4)

        dst.withUnsafeMutableBufferPointer { d in
            source.withUnsafeBufferPointer { s in
                fillBackdrop(d, outW, outH, style.backdrop, s, width, height, stride, accent)

                let left = Double(m.imageX), top = Double(m.imageY)
                let right = left + Double(width), bottom = top + Double(height)
                let cx = (left + right) / 2, cy = (top + bottom) / 2
                let hx = Double(width) / 2, hy = Double(height) / 2
                let r = m.radius
                let ar = Double((accent >> 16) & 0xFF) / 255, ag = Double((accent >> 8) & 0xFF) / 255, ab = Double(accent & 0xFF) / 255
                let shadowAlpha = style.backdrop == .none ? 0.40 : 0.50
                let inner = Int(r.rounded(.up)) + 1
                let dp = d.baseAddress!, sp = s.baseAddress!

                DispatchQueue.concurrentPerform(iterations: outH) { y in
                    let row = y * outW * 4
                    let py = Double(y) + 0.5
                    let insideRows = Double(y) >= top + Double(inner) && Double(y) < bottom - Double(inner)
                    var x = 0
                    while x < outW {
                        if insideRows && x == m.imageX + 1 && width > 2 {
                            // Fast path: the middle of this row is the capture, as is.
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

                        if m.shadowBlur > 0 {
                            let ds = roundedBoxDistance(px - cx, py - (cy + m.shadowOffset), hx, hy, r)
                            let sh = shadowAlpha * 0.5 * erfc(ds / (m.shadowBlur * 0.7071))
                            b *= 1 - sh; g *= 1 - sh; rr *= 1 - sh
                            a = sh + a * (1 - sh)
                        }
                        if m.glowSize > 0 && dist > -m.lineWidth {
                            var ga: Double
                            if dist <= 0 { ga = 1 } else {
                                let t = max(0, 1 - dist / m.glowSize)
                                ga = min(1, 0.85 * t * t + (dist < m.lineWidth ? 1 - dist / m.lineWidth : 0))
                            }
                            over(&b, &g, &rr, &a, ab, ag, ar, ga)
                        }
                        var cover = min(1, max(0, 0.5 - dist))
                        if m.glowSize > 0 { cover = min(1, max(0, 0.5 - (dist + m.lineWidth))) }
                        if cover > 0 {
                            let sx = min(max(x - m.imageX, 0), width - 1)
                            let sy = min(max(y - m.imageY, 0), height - 1)
                            let si = sy * stride + sx * 4
                            over(&b, &g, &rr, &a, Double(sp[si]) / 255, Double(sp[si + 1]) / 255, Double(sp[si + 2]) / 255, cover)
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

    private static func fillBackdrop(_ d: UnsafeMutableBufferPointer<UInt8>, _ w: Int, _ h: Int, _ backdrop: StyleBackdrop,
                                     _ s: UnsafeBufferPointer<UInt8>, _ sw: Int, _ sh: Int, _ stride: Int, _ accent: UInt32) {
        switch backdrop {
        case .gradient:
            // One color family, light to deep: pretty with every capture color.
            fillGradient(d, w, h, mix(accent, 0xFFFFFF, 0.18), mix(accent, 0x07113A, 0.55))
        case .space:
            fillGradient(d, w, h, 0x1A2470, 0x07113A)
            stars(d, w, h)
        case .blur:
            fillBlur(d, w, h, s, sw, sh, stride)
        case .none:
            break
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

    static func mix(_ a: UInt32, _ b: UInt32, _ t: Double) -> UInt32 {
        func c(_ shift: UInt32) -> UInt32 {
            let x = Double((a >> shift) & 0xFF), y = Double((b >> shift) & 0xFF)
            return UInt32((x + (y - x) * t).rounded()) << shift
        }
        return c(16) | c(8) | c(0)
    }
}
