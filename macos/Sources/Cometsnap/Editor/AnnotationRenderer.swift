import AppKit
import CometsnapCore

/// Draws a capture and its shapes. Works in image pixels with a y-down (top-left origin)
/// context, the same in the editor canvas and when exporting the final PNG.
enum AnnotationRenderer {
    /// Draws an image right side up into a y-down context.
    static func drawImage(_ img: CGImage, in r: CGRect, ctx: CGContext) {
        ctx.saveGState()
        ctx.translateBy(x: r.minX, y: r.maxY)
        ctx.scaleBy(x: 1, y: -1)
        ctx.draw(img, in: CGRect(origin: .zero, size: r.size))
        ctx.restoreGState()
    }

    /// Base image, then shapes in order. Spotlights are merged into one dim layer.
    static func draw(base: CGImage, doc: AnnotationDocument, ctx: CGContext, skip: UUID? = nil) {
        let full = CGRect(x: 0, y: 0, width: base.width, height: base.height)
        ctx.interpolationQuality = .high
        drawImage(base, in: full, ctx: ctx)
        var spotlightDone = false
        // Constellation lines sit under the shapes (after blur and spotlight).
        var constellationDone = !doc.connectSteps
        for a in doc.annotations where a.id != skip {
            if !constellationDone, a.kind != .blur, a.kind != .spotlight {
                constellationDone = true
                drawConstellation(doc.orderedSteps, ctx: ctx)
            }
            if a.kind == .spotlight {
                if !spotlightDone {
                    spotlightDone = true
                    drawSpotlights(doc.annotations.filter { $0.kind == .spotlight }, full: full, ctx: ctx)
                }
                continue
            }
            draw(a, base: base, ctx: ctx)
        }
        if !constellationDone { drawConstellation(doc.orderedSteps, ctx: ctx) }
    }

    /// Faint dotted lines from step 1 to 2 to 3…, with a little four-pointed star halfway along each.
    static func drawConstellation(_ steps: [Annotation], ctx: CGContext) {
        guard steps.count > 1 else { return }
        for i in 1..<steps.count {
            let a = steps[i - 1], b = steps[i]
            let ra = CGFloat(a.stepRadius), rb = CGFloat(b.stepRadius)
            let from = a.start, to = b.start
            let dx = to.x - from.x, dy = to.y - from.y
            let length = hypot(dx, dy)
            guard length > ra + rb + 4 else { continue }
            let ux = dx / length, uy = dy / length
            let start = CGPoint(x: from.x + ux * (ra + 3), y: from.y + uy * (ra + 3))
            let end = CGPoint(x: to.x - ux * (rb + 3), y: to.y - uy * (rb + 3))
            let width = max(1.2, (ra + rb) / 16)
            ctx.saveGState()
            ctx.setLineCap(.round)
            ctx.setStrokeColor(NSColor(srgbRed: 0xA9 / 255.0, green: 0xC8 / 255.0, blue: 1, alpha: 0x38 / 255.0).cgColor)
            ctx.setLineWidth(width * 4)
            ctx.move(to: start)
            ctx.addLine(to: end)
            ctx.strokePath()
            ctx.setStrokeColor(NSColor.white.withAlphaComponent(0xC0 / 255.0).cgColor)
            ctx.setLineWidth(width)
            ctx.setLineDash(phase: 0, lengths: [0, width * 3.2])
            ctx.move(to: start)
            ctx.addLine(to: end)
            ctx.strokePath()
            ctx.restoreGState()
            drawSparkle(CGPoint(x: (start.x + end.x) / 2, y: (start.y + end.y) / 2), r: max(4, width * 3.2), ctx: ctx)
        }
    }

    private static func drawSparkle(_ c: CGPoint, r: CGFloat, ctx: CGContext) {
        let k = r * 0.22
        ctx.saveGState()
        ctx.setFillColor(NSColor(srgbRed: 1, green: 0xE8 / 255.0, blue: 0x80 / 255.0, alpha: 0x50 / 255.0).cgColor)
        ctx.fillEllipse(in: CGRect(x: c.x - r * 0.8, y: c.y - r * 0.8, width: r * 1.6, height: r * 1.6))
        let star = CGMutablePath()
        star.addLines(between: [
            CGPoint(x: c.x, y: c.y - r), CGPoint(x: c.x + k, y: c.y - k), CGPoint(x: c.x + r, y: c.y), CGPoint(x: c.x + k, y: c.y + k),
            CGPoint(x: c.x, y: c.y + r), CGPoint(x: c.x - k, y: c.y + k), CGPoint(x: c.x - r, y: c.y), CGPoint(x: c.x - k, y: c.y - k),
        ])
        star.closeSubpath()
        ctx.addPath(star)
        ctx.setFillColor(NSColor(srgbRed: 1, green: 0xF4 / 255.0, blue: 0xC2 / 255.0, alpha: 1).cgColor)
        ctx.fillPath()
        ctx.restoreGState()
    }

    static func drawSpotlights(_ spots: [Annotation], full: CGRect, ctx: CGContext) {
        let path = CGMutablePath()
        path.addRect(full)
        for s in spots where s.box.width > 1 && s.box.height > 1 {
            let r = s.box
            let radius = min(14, min(r.width, r.height) / 4)
            path.addPath(CGPath(roundedRect: r, cornerWidth: radius, cornerHeight: radius, transform: nil))
        }
        ctx.saveGState()
        ctx.addPath(path)
        ctx.setFillColor(NSColor.black.withAlphaComponent(0.58).cgColor)
        ctx.fillPath(using: .evenOdd)
        ctx.restoreGState()
    }

    static func draw(_ a: Annotation, base: CGImage?, ctx: CGContext) {
        let color = a.color.cgColor
        let w = CGFloat(max(1, a.width))
        ctx.saveGState()
        defer { ctx.restoreGState() }
        ctx.setLineCap(.round)
        ctx.setLineJoin(.round)
        switch a.kind {
        case .line:
            ctx.setStrokeColor(color)
            ctx.setLineWidth(w)
            ctx.move(to: a.start)
            ctx.addLine(to: a.end)
            ctx.strokePath()
        case .arrow:
            drawArrow(from: a.start, to: a.end, width: w, color: color, ctx: ctx)
        case .rectangle:
            let r = a.box
            ctx.setStrokeColor(color)
            ctx.setLineWidth(w)
            let radius = min(4 + w, min(r.width, r.height) / 2)
            ctx.addPath(CGPath(roundedRect: r, cornerWidth: radius, cornerHeight: radius, transform: nil))
            ctx.strokePath()
        case .ellipse:
            ctx.setStrokeColor(color)
            ctx.setLineWidth(w)
            ctx.strokeEllipse(in: a.box)
        case .pen, .highlighter:
            guard let first = a.points.first else { return }
            if a.kind == .highlighter {
                ctx.setBlendMode(.multiply)
                ctx.setStrokeColor(a.color.withAlpha(0.45).cgColor)
                ctx.setLineWidth(w * 5 + 6)
                ctx.setLineCap(.square)
            } else {
                ctx.setStrokeColor(color)
                ctx.setLineWidth(w)
            }
            ctx.move(to: first.cg)
            if a.points.count == 1 { ctx.addLine(to: CGPoint(x: first.x + 0.1, y: first.y)) }
            for p in a.points.dropFirst() { ctx.addLine(to: p.cg) }
            ctx.strokePath()
        case .step:
            let r = CGFloat(a.stepRadius)
            let circle = CGRect(x: a.start.x - r, y: a.start.y - r, width: r * 2, height: r * 2)
            ctx.setShadow(offset: CGSize(width: 0, height: 1.5), blur: 4, color: NSColor.black.withAlphaComponent(0.45).cgColor)
            ctx.setFillColor(color)
            ctx.fillEllipse(in: circle)
            ctx.setShadow(offset: .zero, blur: 0, color: nil)
            ctx.setStrokeColor(NSColor.white.cgColor)
            ctx.setLineWidth(max(1.5, r / 8))
            ctx.strokeEllipse(in: circle.insetBy(dx: 1, dy: 1))
            let font = NSFont.systemFont(ofSize: r * 1.15, weight: .heavy)
            let s = NSAttributedString(string: "\(a.number)", attributes: [
                .font: font, .foregroundColor: a.color.isLight ? NSColor.black : NSColor.white,
            ])
            drawText(s, centeredIn: circle, ctx: ctx)
        case .text:
            guard !a.text.isEmpty else { return }
            let s = textString(a)
            ctx.setShadow(offset: .zero, blur: CGFloat(a.fontSize) / 5,
                          color: (a.color.isLight ? NSColor.black : NSColor.white).withAlphaComponent(0.7).cgColor)
            drawText(s, at: a.start, ctx: ctx)
        case .callout:
            drawCallout(a, ctx: ctx)
        case .blur:
            guard let base else { return }
            pixelate(base: base, rect: a.box, ctx: ctx)
        case .spotlight:
            drawSpotlights([a], full: CGRect(x: 0, y: 0, width: base?.width ?? 0, height: base?.height ?? 0), ctx: ctx)
        }
    }

    static func textString(_ a: Annotation) -> NSAttributedString {
        NSAttributedString(string: a.text, attributes: [
            .font: NSFont.systemFont(ofSize: CGFloat(a.fontSize), weight: .bold),
            .foregroundColor: NSColor(cgColor: a.color.cgColor) ?? .red,
        ])
    }

    static func calloutBubble(_ a: Annotation) -> CGRect {
        let text = a.text.isEmpty ? " " : a.text
        let s = NSAttributedString(string: text, attributes: [.font: NSFont.systemFont(ofSize: CGFloat(a.fontSize), weight: .semibold)])
        let size = s.boundingRect(with: CGSize(width: 2000, height: 2000), options: [.usesLineFragmentOrigin], context: nil).size
        let pad = CGFloat(a.fontSize) * 0.6
        return CGRect(x: a.end.x, y: a.end.y, width: max(40, ceil(size.width) + pad * 2), height: ceil(size.height) + pad * 1.4)
    }

    static func drawCallout(_ a: Annotation, ctx: CGContext) {
        let bubble = calloutBubble(a)
        let tip = a.start
        let radius = min(12, bubble.height / 2)
        let path = CGMutablePath()
        path.addPath(CGPath(roundedRect: bubble, cornerWidth: radius, cornerHeight: radius, transform: nil))
        // Tail: a triangle from the bubble's edge (closest point to the tip) to the tip.
        let c = CGPoint(x: bubble.midX, y: bubble.midY)
        let base = min(bubble.width, bubble.height) * 0.35
        let angle = atan2(tip.y - c.y, tip.x - c.x)
        let n = CGPoint(x: -sin(angle) * base / 2, y: cos(angle) * base / 2)
        path.move(to: CGPoint(x: c.x + n.x, y: c.y + n.y))
        path.addLine(to: tip)
        path.addLine(to: CGPoint(x: c.x - n.x, y: c.y - n.y))
        path.closeSubpath()
        ctx.setShadow(offset: CGSize(width: 0, height: 2), blur: 6, color: NSColor.black.withAlphaComponent(0.35).cgColor)
        ctx.addPath(path)
        ctx.setFillColor(a.color.cgColor)
        ctx.fillPath(using: .winding)
        ctx.setShadow(offset: .zero, blur: 0, color: nil)
        let pad = CGFloat(a.fontSize) * 0.6
        let s = NSAttributedString(string: a.text, attributes: [
            .font: NSFont.systemFont(ofSize: CGFloat(a.fontSize), weight: .semibold),
            .foregroundColor: a.color.isLight ? NSColor.black : NSColor.white,
        ])
        drawText(s, at: CGPoint(x: bubble.minX + pad, y: bubble.minY + pad * 0.7), ctx: ctx)
    }

    static func drawArrow(from s: CGPoint, to e: CGPoint, width w: CGFloat, color: CGColor, ctx: CGContext) {
        let len = hypot(e.x - s.x, e.y - s.y)
        guard len > 0.5 else { return }
        let angle = atan2(e.y - s.y, e.x - s.x)
        let head = min(len * 0.6, w * 3.2 + 10)
        let spread: CGFloat = 0.5
        let base = CGPoint(x: e.x - cos(angle) * head * 0.8, y: e.y - sin(angle) * head * 0.8)
        ctx.setStrokeColor(color)
        ctx.setFillColor(color)
        ctx.setLineWidth(w)
        ctx.move(to: s)
        ctx.addLine(to: base)
        ctx.strokePath()
        ctx.move(to: e)
        ctx.addLine(to: CGPoint(x: e.x - cos(angle - spread) * head, y: e.y - sin(angle - spread) * head))
        ctx.addLine(to: CGPoint(x: e.x - cos(angle + spread) * head, y: e.y - sin(angle + spread) * head))
        ctx.closePath()
        ctx.setLineWidth(max(1, w / 2))
        ctx.drawPath(using: .fillStroke)
    }

    /// Big square pixels: unreadable, and clearly intentional.
    static func pixelate(base: CGImage, rect: CGRect, ctx: CGContext) {
        let r = rect.intersection(CGRect(x: 0, y: 0, width: base.width, height: base.height)).integral
        guard r.width >= 2, r.height >= 2, let piece = base.cropping(to: r) else { return }
        let block = max(6, min(r.width, r.height) / 10)
        let sw = max(1, Int(r.width / block)), sh = max(1, Int(r.height / block))
        guard let small = CGContext(data: nil, width: sw, height: sh, bitsPerComponent: 8, bytesPerRow: 0,
                                    space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
        else { return }
        small.interpolationQuality = .medium
        small.draw(piece, in: CGRect(x: 0, y: 0, width: sw, height: sh))
        guard let tiny = small.makeImage() else { return }
        ctx.saveGState()
        ctx.interpolationQuality = .none
        drawImage(tiny, in: r, ctx: ctx)
        ctx.restoreGState()
    }

    // MARK: text helpers (y-down)

    static func drawText(_ s: NSAttributedString, at p: CGPoint, ctx: CGContext) {
        let ns = NSGraphicsContext(cgContext: ctx, flipped: true)
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = ns
        s.draw(with: CGRect(x: p.x, y: p.y, width: 4000, height: 4000), options: [.usesLineFragmentOrigin], context: nil)
        NSGraphicsContext.restoreGraphicsState()
    }

    static func drawText(_ s: NSAttributedString, centeredIn r: CGRect, ctx: CGContext) {
        let size = s.size()
        drawText(s, at: CGPoint(x: r.midX - size.width / 2, y: r.midY - size.height / 2), ctx: ctx)
    }

    /// The final picture: crop applied, shapes flattened.
    static func flatten(base: CGImage, doc: AnnotationDocument) -> CGImage? {
        let full = CGRect(x: 0, y: 0, width: base.width, height: base.height)
        let crop = (doc.crop ?? full).intersection(full).integral
        guard crop.width >= 1, crop.height >= 1,
              let ctx = CGContext(data: nil, width: Int(crop.width), height: Int(crop.height), bitsPerComponent: 8, bytesPerRow: 0,
                                  space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
        else { return nil }
        // y-down, with the crop's origin at the top-left.
        ctx.translateBy(x: 0, y: crop.height)
        ctx.scaleBy(x: 1, y: -1)
        ctx.translateBy(x: -crop.minX, y: -crop.minY)
        draw(base: base, doc: doc, ctx: ctx)
        return ctx.makeImage()
    }
}
