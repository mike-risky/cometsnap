import AppKit
import CometsnapCore

/// Applies the share-ready style (Settings > Share-ready style) to a capture. Safe on any queue.
enum SnapStyler {
    static func apply(_ image: CGImage, settings: AppSettings) -> CGImage? {
        apply(image, style: SnapStyles.get(settings.snapStyle), accent: CaptureLook.theme.accent)
    }

    static func apply(_ image: CGImage, style: SnapStyle, accent: UInt32) -> CGImage? {
        let w = image.width, h = image.height
        guard w > 0, h > 0 else { return nil }
        let space = colorSpace(of: image)
        // BGRX, the layout the renderer reads (alpha ignored: a screenshot is opaque).
        let readInfo = CGImageAlphaInfo.noneSkipFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue
        guard let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4,
                                  space: space, bitmapInfo: readInfo), let data = ctx.data else { return nil }
        ctx.draw(image, in: CGRect(x: 0, y: 0, width: w, height: h))
        let pixels = [UInt8](UnsafeBufferPointer(start: data.assumingMemoryBound(to: UInt8.self), count: w * h * 4))
        let styled = StyleRenderer.render(pixels, width: w, height: h, stride: w * 4, style: style, accent: accent)
        return makeImage(styled, space: space)
    }

    /// The image on white, without transparency (JPEG has none: see-through corners would turn black).
    static func flattened(_ image: CGImage) -> CGImage {
        switch image.alphaInfo {
        case .none, .noneSkipFirst, .noneSkipLast: return image
        default: break
        }
        let w = image.width, h = image.height
        let info = CGImageAlphaInfo.noneSkipFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue
        guard let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4,
                                  space: colorSpace(of: image), bitmapInfo: info) else { return image }
        ctx.setFillColor(CGColor(red: 1, green: 1, blue: 1, alpha: 1))
        ctx.fill(CGRect(x: 0, y: 0, width: w, height: h))
        ctx.draw(image, in: CGRect(x: 0, y: 0, width: w, height: h))
        return ctx.makeImage() ?? image
    }

    /// Straight-alpha BGRA bytes as a CGImage.
    static func makeImage(_ p: StyledPixels, space: CGColorSpace) -> CGImage? {
        guard let provider = CGDataProvider(data: Data(p.bgra) as CFData) else { return nil }
        let info = CGBitmapInfo(rawValue: CGImageAlphaInfo.first.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)
        return CGImage(width: p.width, height: p.height, bitsPerComponent: 8, bitsPerPixel: 32, bytesPerRow: p.width * 4,
                       space: space, bitmapInfo: info, provider: provider, decode: nil, shouldInterpolate: true, intent: .defaultIntent)
    }

    private static func colorSpace(of image: CGImage) -> CGColorSpace {
        if let cs = image.colorSpace, cs.model == .rgb { return cs }
        return CGColorSpace(name: CGColorSpace.sRGB) ?? CGColorSpaceCreateDeviceRGB()
    }

    /// The Settings preview: a small pretend app window, styled for real.
    static func preview(style: SnapStyle, accent: UInt32) -> NSImage? {
        let w = 640, h = 380
        var p = [UInt8](repeating: 0, count: w * h * 4)
        func rect(_ x: Int, _ y: Int, _ rw: Int, _ rh: Int, _ rgb: UInt32, _ radius: Double = 0) {
            for yy in y..<(y + rh) {
                for xx in x..<(x + rw) {
                    if radius > 0 && StyleRenderer.roundedBoxDistance(Double(xx) + 0.5 - (Double(x) + Double(rw) / 2), Double(yy) + 0.5 - (Double(y) + Double(rh) / 2),
                                                                     Double(rw) / 2, Double(rh) / 2, radius) > 0 { continue }
                    let i = (yy * w + xx) * 4
                    p[i] = UInt8(rgb & 0xFF); p[i + 1] = UInt8((rgb >> 8) & 0xFF); p[i + 2] = UInt8((rgb >> 16) & 0xFF); p[i + 3] = 255
                }
            }
        }
        rect(0, 0, w, h, 0xF7F8FC)
        rect(0, 0, w, 44, 0xE6E9F3)
        rect(17, 15, 14, 14, 0xE2553A, 7)
        rect(39, 15, 14, 14, 0xFFD60A, 7)
        rect(61, 15, 14, 14, 0x3D6BFF, 7)
        rect(36, 76, 260, 26, 0x07113A, 6)
        for i in 0..<4 { rect(36, 126 + i * 30, 300 - i * 40, 14, 0xC9CFE3, 7) }
        rect(376, 76, 228, 166, 0x3D6BFF, 12)
        rect(456, 125, 68, 68, 0xFFD60A, 34)
        rect(36, 280, 140, 44, 0xE2553A, 10)
        rect(376, 270, 228, 14, 0xC9CFE3, 7)
        rect(376, 300, 170, 14, 0xC9CFE3, 7)
        let styled = StyleRenderer.render(p, width: w, height: h, stride: w * 4, style: style, accent: accent)
        guard let space = CGColorSpace(name: CGColorSpace.sRGB), let cg = makeImage(styled, space: space) else { return nil }
        return NSImage(cgImage: cg, size: NSSize(width: styled.width / 2, height: styled.height / 2))
    }
}
