import AppKit
import CometsnapCore
import UniformTypeIdentifiers

// MARK: - Drag out

/// Drags a capture out of Cometsnap into any app: the file (Finder, Mail, Slack, browsers…)
/// and the PNG itself (Pages, Keynote, Preview, web apps). The thumbnail follows the cursor.
final class DragOut: NSObject, NSDraggingSource {
    private static var active: DragOut?
    private let onEnd: (NSDragOperation) -> Void

    private init(onEnd: @escaping (NSDragOperation) -> Void) { self.onEnd = onEnd }

    /// Starts the drag. `frame` is where the preview starts, in `view` coordinates.
    @discardableResult
    static func start(from view: NSView, event: NSEvent, url: URL?, image: CGImage?, preview: NSImage?, frame: CGRect,
                      onEnd: @escaping (NSDragOperation) -> Void = { _ in }) -> Bool {
        let item = NSPasteboardItem()
        var any = false
        if let url, FileManager.default.fileExists(atPath: url.path) {
            item.setString(url.absoluteString, forType: .fileURL)
            any = true
        }
        let isRecording = url.map { (MediaKind.of($0) ?? .image) != .image } ?? false
        if !isRecording {
            var png: Data?
            if let url, url.pathExtension.lowercased() == "png" { png = try? Data(contentsOf: url) }
            if png == nil, let image { png = PNG.data(image) }
            if let png {
                item.setData(png, forType: .png)
                any = true
            }
        }
        guard any else { return false }
        let di = NSDraggingItem(pasteboardWriter: item)
        let picture = preview ?? image.map { NSImage(cgImage: $0, size: NSSize(width: $0.width, height: $0.height)) }
        di.setDraggingFrame(fitted(picture, in: frame), contents: picture)
        let source = DragOut(onEnd: onEnd)
        active = source
        let session = view.beginDraggingSession(with: [di], event: event, source: source)
        session.animatesToStartingPositionsOnCancelOrFail = true
        return true
    }

    /// Drags several captures at once, as files (Finder, Mail, Slack, Messages take a group).
    /// Each item starts from its own tile, and AppKit stacks them under the pointer with a count.
    @discardableResult
    static func startFiles(from view: NSView, event: NSEvent, items: [(url: URL, preview: NSImage?, frame: CGRect)],
                           onEnd: @escaping (NSDragOperation) -> Void = { _ in }) -> Bool {
        let existing = items.filter { FileManager.default.fileExists(atPath: $0.url.path) }
        guard !existing.isEmpty else { return false }
        let dragItems = existing.map { entry -> NSDraggingItem in
            let di = NSDraggingItem(pasteboardWriter: entry.url as NSURL)
            di.setDraggingFrame(fitted(entry.preview, in: entry.frame), contents: entry.preview)
            return di
        }
        let source = DragOut(onEnd: onEnd)
        active = source
        let session = view.beginDraggingSession(with: dragItems, event: event, source: source)
        session.animatesToStartingPositionsOnCancelOrFail = true
        session.draggingFormation = .stack
        return true
    }

    /// Keeps the preview's aspect ratio inside the given frame.
    private static func fitted(_ img: NSImage?, in frame: CGRect) -> CGRect {
        guard let img, img.size.width > 0, img.size.height > 0 else { return frame }
        let s = min(frame.width / img.size.width, frame.height / img.size.height)
        let w = img.size.width * s, h = img.size.height * s
        return CGRect(x: frame.midX - w / 2, y: frame.midY - h / 2, width: w, height: h)
    }

    func draggingSession(_ session: NSDraggingSession, sourceOperationMaskFor context: NSDraggingContext) -> NSDragOperation {
        context == .outsideApplication ? [.copy] : []
    }

    func draggingSession(_ session: NSDraggingSession, endedAt screenPoint: NSPoint, operation: NSDragOperation) {
        onEnd(operation)
        DragOut.active = nil
    }
}

// MARK: - Seasonal looks

/// A pumpkin comet in late October, a snowy one in December. Drawn over the logo in the
/// logo's own 64 × 64 grid (y down), the same shapes as on Windows.
enum SeasonalArt {
    static var current: Season {
        guard let app = NSApp.delegate as? AppController, app.settings.seasonalLooks else { return .none }
        return Season.at(Date())
    }

    private static let ink = NSColor(hex: 0x07113A)

    /// The image with the season's touch on top (or the image itself out of season).
    static func decorate(_ base: NSImage, season: Season = current) -> NSImage {
        guard season != .none else { return base }
        let size = base.size
        let img = NSImage(size: size, flipped: false) { rect in
            base.draw(in: rect)
            guard let ctx = NSGraphicsContext.current?.cgContext else { return true }
            ctx.saveGState()
            ctx.translateBy(x: rect.minX, y: rect.maxY)
            ctx.scaleBy(x: rect.width / 64, y: -rect.height / 64)
            draw(season, in: ctx)
            ctx.restoreGState()
            return true
        }
        img.isTemplate = false
        return img
    }

    static func draw(_ season: Season, in ctx: CGContext) {
        ctx.setLineJoin(.round)
        ctx.setLineCap(.round)
        switch season {
        case .none:
            break
        case .winter:
            fill(path("M26.6,24 Q26.8,14.6 33.8,11.6 Q42.6,9.2 48.8,14.6 Q52.6,18.4 52.2,24.6 Q50.2,23.4 49,25.4 Q47.6,23 45.4,24.4 Q44.4,27.6 42.4,26.2 Q41.6,23.4 39,23.8 Q36.6,22.2 34.6,24.4 Q33.4,27.2 31.4,25.6 Q30.6,23.4 28.8,23.6 Q27.6,24.8 26.6,24 Z"),
                 color: .white, stroke: ink, width: 2, ctx: ctx)
            for (x, y, r) in [(12.0, 12.0, 3.2), (56.0, 46.0, 2.6), (7.0, 29.0, 2.2), (22.0, 6.0, 2.0)] {
                snowflake(CGPoint(x: x, y: y), r: r, ctx: ctx)
            }
        case .halloween:
            let body = CGPath(ellipseIn: CGRect(x: 39.2 - 14.2, y: 25.2 - 12.6, width: 28.4, height: 25.2), transform: nil)
            fill(body, color: NSColor(hex: 0xF2841F), stroke: ink, width: 2, ctx: ctx)
            let ribs = CGMutablePath()
            ribs.addEllipse(in: CGRect(x: 39.2 - 7.2, y: 25.2 - 12.2, width: 14.4, height: 24.4))
            ribs.addPath(path("M39.2,13 V37.4"))
            ctx.addPath(ribs)
            ctx.setStrokeColor(NSColor(hex: 0xB85512).cgColor)
            ctx.setLineWidth(1.3)
            ctx.strokePath()
            fill(path("M37.6,13.4 Q37.8,8.6 41.8,7.2 L43.4,9.4 Q40.6,10.6 41,13.4 Z"), color: NSColor(hex: 0x3F8A2E), stroke: ink, width: 1.4, ctx: ctx)
            fill(path("M32.2,23.4 L35.4,18.6 L38,23.4 Z M40.6,23.4 L43.2,18.6 L46.4,23.4 Z"), color: Theme.yellow, stroke: nil, width: 0, ctx: ctx)
            fill(path("M31.4,27.4 Q39.2,34.6 47.2,27.4 L45.6,27.8 L44.6,29.6 L42.6,28.6 L39.4,30.6 L36.4,28.6 L34.2,29.8 L33.2,28 Z"),
                 color: Theme.yellow, stroke: nil, width: 0, ctx: ctx)
        }
    }

    private static func fill(_ p: CGPath, color: NSColor, stroke: NSColor?, width: CGFloat, ctx: CGContext) {
        ctx.addPath(p)
        ctx.setFillColor(color.cgColor)
        if let stroke {
            ctx.setStrokeColor(stroke.cgColor)
            ctx.setLineWidth(width)
            ctx.drawPath(using: .fillStroke)
        } else {
            ctx.fillPath()
        }
    }

    private static func snowflake(_ c: CGPoint, r: CGFloat, ctx: CGContext) {
        ctx.setStrokeColor(NSColor(hex: 0xE8F4FF).cgColor)
        ctx.setLineWidth(1.1)
        for i in 0..<3 {
            let a = CGFloat.pi / 3 * CGFloat(i) + .pi / 2
            ctx.move(to: CGPoint(x: c.x + cos(a) * r, y: c.y + sin(a) * r))
            ctx.addLine(to: CGPoint(x: c.x - cos(a) * r, y: c.y - sin(a) * r))
        }
        ctx.strokePath()
    }

    /// A tiny path reader for the absolute commands the art uses: M L H V Q Z.
    static func path(_ d: String) -> CGPath {
        let p = CGMutablePath()
        var tokens: [String] = []
        var cur = ""
        for ch in d {
            if ch.isLetter {
                if !cur.isEmpty { tokens.append(cur); cur = "" }
                tokens.append(String(ch))
            } else if ch == "," || ch == " " {
                if !cur.isEmpty { tokens.append(cur); cur = "" }
            } else if ch == "-" && !cur.isEmpty {
                tokens.append(cur); cur = "-"
            } else {
                cur.append(ch)
            }
        }
        if !cur.isEmpty { tokens.append(cur) }
        var i = 0
        var cmd = "M"
        var last = CGPoint.zero
        func num() -> CGFloat {
            defer { i += 1 }
            return i < tokens.count ? CGFloat(Double(tokens[i]) ?? 0) : 0
        }
        while i < tokens.count {
            if let first = tokens[i].first, first.isLetter { cmd = tokens[i]; i += 1 }
            switch cmd {
            case "M":
                last = CGPoint(x: num(), y: num()); p.move(to: last); cmd = "L"
            case "L":
                last = CGPoint(x: num(), y: num()); p.addLine(to: last)
            case "H":
                last = CGPoint(x: num(), y: last.y); p.addLine(to: last)
            case "V":
                last = CGPoint(x: last.x, y: num()); p.addLine(to: last)
            case "Q":
                let c = CGPoint(x: num(), y: num())
                last = CGPoint(x: num(), y: num())
                p.addQuadCurve(to: last, control: c)
            case "Z", "z":
                p.closeSubpath()
            default:
                i += 1
            }
        }
        return p
    }
}

/// Falling snow for the panel header in December. Click-through, purely decorative.
final class SnowLayer: CAEmitterLayer {
    override init() {
        super.init()
        let cell = CAEmitterCell()
        cell.contents = SnowLayer.flake
        cell.birthRate = 9
        cell.lifetime = 9
        cell.velocity = 14
        cell.velocityRange = 6
        cell.emissionLongitude = -.pi / 2
        cell.emissionRange = .pi / 8
        cell.xAcceleration = -1.5
        cell.scale = 0.16
        cell.scaleRange = 0.1
        cell.alphaRange = 0.4
        cell.alphaSpeed = -0.06
        cell.spin = 0.3
        cell.spinRange = 1
        emitterCells = [cell]
        emitterShape = .line
        renderMode = .oldestFirst
    }

    override init(layer: Any) { super.init(layer: layer) }
    required init?(coder: NSCoder) { fatalError() }

    func fit(_ bounds: CGRect) {
        frame = bounds
        emitterPosition = CGPoint(x: bounds.midX, y: bounds.maxY + 4)
        emitterSize = CGSize(width: bounds.width, height: 1)
    }

    private static let flake: CGImage? = {
        let size = 32
        guard let ctx = CGContext(data: nil, width: size, height: size, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
        else { return nil }
        let colors = [NSColor.white.cgColor, NSColor.white.withAlphaComponent(0).cgColor] as CFArray
        if let g = CGGradient(colorsSpace: CGColorSpaceCreateDeviceRGB(), colors: colors, locations: [0.35, 1]) {
            ctx.drawRadialGradient(g, startCenter: CGPoint(x: 16, y: 16), startRadius: 0, endCenter: CGPoint(x: 16, y: 16),
                                   endRadius: 16, options: [])
        }
        return ctx.makeImage()
    }()
}

// MARK: - Meteor shower

/// A short meteor shower over part of the screen, with an optional caption. It never takes
/// focus or clicks: you can keep working right through it.
enum MeteorShower {
    private static var panels: [NSPanel] = []

    static func play(over area: CGRect, meteors: Int, duration: TimeInterval, title: String? = nil, subtitle: String? = nil,
                     captionAtBottom: Bool = true) {
        let panel = NSPanel(contentRect: area, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.level = .statusBar
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = false
        panel.ignoresMouseEvents = true
        panel.isReleasedWhenClosed = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle, .stationary]
        let view = NSView(frame: CGRect(origin: .zero, size: area.size))
        view.wantsLayer = true
        panel.contentView = view
        guard let root = view.layer else { return }
        let scale = NSScreen.containing(CGPoint(x: area.midX, y: area.midY))?.backingScaleFactor ?? 2
        let w = area.width, h = area.height
        let diagonal = hypot(w, h)
        let now = CACurrentMediaTime()

        for _ in 0..<max(1, meteors) {
            let length = 36 + CGFloat.random(in: 0...1) * min(120, w * 0.25)
            let thickness = CGFloat.random(in: 1.4...2.6)
            let golden = Double.random(in: 0...1) < 0.35
            let head = golden ? Theme.yellow : NSColor.white
            let m = CAGradientLayer()
            m.colors = [NSColor(hex: 0x9FD0FF).withAlphaComponent(0).cgColor, head.cgColor]
            m.startPoint = CGPoint(x: 0, y: 0.5)
            m.endPoint = CGPoint(x: 1, y: 0.5)
            m.bounds = CGRect(x: 0, y: 0, width: length, height: thickness)
            m.cornerRadius = thickness / 2
            m.anchorPoint = CGPoint(x: 1, y: 0.5)
            m.opacity = 0
            // Top right to bottom left, the brand's motion direction (y up here).
            let angle = CGFloat.random(in: 18...40) * .pi / 180
            let start = CGPoint(x: CGFloat.random(in: (w * 0.15)...(w + length)), y: h + 20 - CGFloat.random(in: 0...(h * 0.45)))
            let travel = CGFloat.random(in: 0.35...0.75) * diagonal
            let end = CGPoint(x: start.x - cos(angle) * travel, y: start.y - sin(angle) * travel)
            m.position = start
            m.setAffineTransform(CGAffineTransform(rotationAngle: .pi + angle))
            root.addSublayer(m)

            let fly = Double.random(in: 0.52...0.94)
            let begin = Double.random(in: 0...max(0.001, duration - fly - 0.2))
            let move = CABasicAnimation(keyPath: "position")
            move.fromValue = NSValue(point: start)
            move.toValue = NSValue(point: end)
            let fade = CAKeyframeAnimation(keyPath: "opacity")
            fade.values = [0, 0.95, 0.75, 0]
            fade.keyTimes = [0, 0.2, 0.7, 1]
            let group = CAAnimationGroup()
            group.animations = [move, fade]
            group.duration = fly
            group.beginTime = now + begin
            group.fillMode = .both
            group.isRemovedOnCompletion = false
            group.timingFunction = CAMediaTimingFunction(name: .easeIn)
            m.add(group, forKey: "fly")
        }

        if let title {
            let caption = CALayer()
            let t = CATextLayer()
            t.string = NSAttributedString(string: title, attributes: [.font: Theme.heading(18), .foregroundColor: Theme.yellow])
            t.alignmentMode = .center
            t.contentsScale = scale
            t.frame = CGRect(x: 0, y: 20, width: w, height: 26)
            t.shadowColor = Theme.night.cgColor
            t.shadowOpacity = 0.9
            t.shadowRadius = 4
            t.shadowOffset = .zero
            caption.addSublayer(t)
            if let subtitle {
                let s = CATextLayer()
                s.string = NSAttributedString(string: subtitle, attributes: [.font: Theme.ui(12, .medium), .foregroundColor: Theme.muted])
                s.alignmentMode = .center
                s.contentsScale = scale
                s.frame = CGRect(x: 0, y: 0, width: w, height: 18)
                s.shadowColor = Theme.night.cgColor
                s.shadowOpacity = 0.9
                s.shadowRadius = 3
                s.shadowOffset = .zero
                caption.addSublayer(s)
            }
            caption.frame = CGRect(x: 0, y: captionAtBottom ? 8 : h / 2 - 23, width: w, height: 46)
            caption.opacity = 0
            root.addSublayer(caption)
            let show = CAKeyframeAnimation(keyPath: "opacity")
            show.values = [0, 1, 1, 0]
            show.keyTimes = [0, 0.1, 0.82, 1]
            show.duration = duration
            let pop = CABasicAnimation(keyPath: "transform.scale")
            pop.fromValue = 0.9
            pop.toValue = 1
            pop.duration = 0.32
            pop.timingFunction = CAMediaTimingFunction(controlPoints: 0.34, 1.4, 0.64, 1)
            caption.add(show, forKey: "show")
            caption.add(pop, forKey: "pop")
        }

        panels.append(panel)
        panel.orderFrontRegardless()
        DispatchQueue.main.asyncAfter(deadline: .now() + duration + 0.15) {
            panel.orderOut(nil)
            panels.removeAll { $0 === panel }
        }
    }
}

// MARK: - Comet flight

/// After a capture, the capture itself (as a small card) flies into the menu bar icon,
/// pulled by a comet. A tiny click-through panel moved along a curve; it never takes focus.
final class CometFlight {
    private static var current: CometFlight?
    private static let size: CGFloat = 170
    private static let thumbMax: CGFloat = 84
    private static let duration: TimeInterval = 0.62

    private let panel: NSPanel
    private let comet = CALayer()
    private let thumb = CALayer()
    private var from = CGPoint.zero, to = CGPoint.zero, control = CGPoint.zero
    private var startTime: CFTimeInterval = 0
    private var timer: Timer?

    static func fly(from rect: CGRect, to target: CGRect?, image: CGImage?) {
        guard let target else { return }
        current?.finish()
        let f = CometFlight(image: image)
        current = f
        f.launch(from: CGPoint(x: rect.midX, y: rect.midY), to: CGPoint(x: target.midX, y: target.midY))
    }

    private init(image: CGImage?) {
        let s = CometFlight.size
        panel = NSPanel(contentRect: CGRect(x: -10000, y: -10000, width: s, height: s), styleMask: [.borderless, .nonactivatingPanel],
                        backing: .buffered, defer: false)
        panel.level = .screenSaver
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = false
        panel.ignoresMouseEvents = true
        panel.isReleasedWhenClosed = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle]
        let view = NSView(frame: CGRect(x: 0, y: 0, width: s, height: s))
        view.wantsLayer = true
        panel.contentView = view
        guard let root = view.layer else { return }
        let c = s / 2

        // Comet pointing right (+x); rotated to the direction of travel each frame.
        comet.frame = root.bounds
        let tail = CAGradientLayer()
        // Classic icy blue for the blue theme, else the capture theme's accent.
        let classic = CaptureLook.theme.id == CaptureThemes.defaultID
        let far = classic ? NSColor(hex: 0x4FC3FF) : CaptureLook.accent
        let near = classic ? NSColor(hex: 0x78C8FF) : CaptureLook.accent
        tail.colors = [far.withAlphaComponent(0).cgColor, near.withAlphaComponent(0.8).cgColor]
        tail.startPoint = CGPoint(x: 0, y: 0.5)
        tail.endPoint = CGPoint(x: 1, y: 0.5)
        tail.frame = CGRect(x: c - 80, y: c - 9, width: 80, height: 18)
        let mask = CAShapeLayer()
        let tp = CGMutablePath()
        tp.move(to: CGPoint(x: 80, y: 0))
        tp.addLine(to: CGPoint(x: 0, y: 7))
        tp.addLine(to: CGPoint(x: 0, y: 11))
        tp.addLine(to: CGPoint(x: 80, y: 18))
        tp.closeSubpath()
        mask.path = tp
        tail.mask = mask
        comet.addSublayer(tail)
        let glow = CAGradientLayer()
        glow.type = .radial
        glow.colors = [near.withAlphaComponent(0.67).cgColor, far.withAlphaComponent(0).cgColor]
        glow.startPoint = CGPoint(x: 0.5, y: 0.5)
        glow.endPoint = CGPoint(x: 1, y: 1)
        glow.frame = CGRect(x: c - 17, y: c - 17, width: 34, height: 34)
        comet.addSublayer(glow)
        let head = CALayer()
        head.frame = CGRect(x: c - 7, y: c - 7, width: 14, height: 14)
        head.cornerRadius = 7
        head.backgroundColor = NSColor.white.cgColor
        comet.addSublayer(head)
        root.addSublayer(comet)

        // The capture rides on the comet's head.
        if let image {
            let k = CometFlight.thumbMax / CGFloat(max(1, max(image.width, image.height)))
            let tw = max(12, CGFloat(image.width) * k), th = max(12, CGFloat(image.height) * k)
            thumb.frame = CGRect(x: c - tw / 2, y: c - th / 2, width: tw, height: th)
            thumb.contents = image
            thumb.contentsGravity = .resizeAspectFill
            thumb.masksToBounds = true
            thumb.cornerRadius = 4
            thumb.borderWidth = 2
            thumb.borderColor = NSColor.white.cgColor
            thumb.backgroundColor = Theme.night.cgColor
            root.addSublayer(thumb)
        }
    }

    private func launch(from: CGPoint, to: CGPoint) {
        self.from = from
        self.to = to
        // A gentle arc: across first, then up into the menu bar.
        control = CGPoint(x: from.x * 0.3 + to.x * 0.7, y: from.y + (to.y - from.y) * 0.15)
        startTime = CACurrentMediaTime()
        move(t: 0)
        panel.orderFrontRegardless()
        timer = Timer.scheduledTimer(withTimeInterval: 1.0 / 60, repeats: true) { [weak self] _ in self?.tick() }
        RunLoop.main.add(timer!, forMode: .common)
    }

    private func point(_ t: CGFloat) -> CGPoint {
        let u = 1 - t
        return CGPoint(x: u * u * from.x + 2 * u * t * control.x + t * t * to.x,
                       y: u * u * from.y + 2 * u * t * control.y + t * t * to.y)
    }

    private func tick() {
        let raw = (CACurrentMediaTime() - startTime) / CometFlight.duration
        if raw >= 1 { finish(); return }
        move(t: CGFloat(raw))
    }

    private func move(t raw: CGFloat) {
        let t = raw < 0.5 ? 2 * raw * raw : 1 - pow(-2 * raw + 2, 2) / 2 // ease in-out
        let p = point(t)
        let ahead = point(min(1, t + 0.02))
        let s = CometFlight.size
        panel.setFrameOrigin(CGPoint(x: p.x - s / 2, y: p.y - s / 2))
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        let angle = atan2(ahead.y - p.y, ahead.x - p.x)
        let shrink = 1 - 0.55 * t
        comet.setAffineTransform(CGAffineTransform(rotationAngle: angle).scaledBy(x: shrink, y: shrink))
        let ts = 1 - 0.8 * t
        thumb.setAffineTransform(CGAffineTransform(scaleX: ts, y: ts))
        panel.alphaValue = raw > 0.8 ? (1 - raw) / 0.2 : 1
        CATransaction.commit()
    }

    private func finish() {
        timer?.invalidate()
        timer = nil
        panel.orderOut(nil)
        if CometFlight.current === self { CometFlight.current = nil }
    }
}
