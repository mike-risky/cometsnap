import AppKit
import CometsnapCore

/// What the overlay hands back: the frozen screen and the chosen rectangle in AppKit global
/// points (always inside that screen).
struct OverlayResult {
    let frozen: FrozenScreen
    let rect: CGRect

    /// The chosen pixels, cut out of the frozen screen.
    var image: CGImage? {
        let s = frozen.scale
        let f = frozen.screen.frame
        let local = CGRect(x: rect.minX - f.minX, y: f.maxY - rect.maxY, width: rect.width, height: rect.height)
        let px = CGRect(x: local.minX * s, y: local.minY * s, width: local.width * s, height: local.height * s).integral
        return frozen.image.cropping(to: px.intersection(CGRect(x: 0, y: 0, width: frozen.image.width, height: frozen.image.height)))
    }
}

/// The capture overlay: every screen is frozen and dimmed; hover highlights the window under
/// the cursor (click grabs it), drag selects any area with edges snapping to windows, and a
/// loupe in a corner shows the real pixels and dodges the cursor.
final class CaptureOverlay {
    enum Purpose { case screenshot, recording, text, scrolling }

    let purpose: Purpose
    let loupeCorner: LoupeCorner
    private(set) var frozen: [FrozenScreen]
    private var windows: [OverlayWindow] = []
    let windowList: [WindowInfo]
    private var completion: ((OverlayResult?) -> Void)?

    // Shared state, AppKit global points.
    var mouse: CGPoint = NSEvent.mouseLocation
    var dragStart: CGPoint?
    var dragCurrent: CGPoint?
    var isDragging = false
    var detectWindows = true
    var spaceHeld = false
    private var spaceAnchor: CGPoint?

    init(frozen: [FrozenScreen], windows: [WindowInfo], purpose: Purpose, loupeCorner: LoupeCorner,
         completion: @escaping (OverlayResult?) -> Void) {
        self.frozen = frozen
        self.windowList = windows
        self.purpose = purpose
        self.loupeCorner = loupeCorner
        self.completion = completion
    }

    func show() {
        for f in frozen {
            let w = OverlayWindow(frozen: f, overlay: self)
            windows.append(w)
        }
        for w in windows { w.orderFrontRegardless() }
        NSApp.activate()
        let target = windows.first { NSMouseInRect(NSEvent.mouseLocation, $0.frame, false) } ?? windows.first
        target?.makeKey()
        NSCursor.crosshair.set()
        refresh()
    }

    // MARK: state

    /// The selection being dragged, clamped to the screen where the drag began.
    var selection: CGRect? {
        guard isDragging, let a = dragStart, let b = dragCurrent else { return nil }
        var r = CGRect(corner: a, corner: b)
        if let s = screen(containing: a) { r = r.intersection(s.frame) }
        return r.isNull ? nil : r
    }

    /// The window (or whole screen) under the cursor when not dragging.
    var hovered: CGRect? {
        guard !isDragging else { return nil }
        guard let s = screen(containing: mouse) else { return nil }
        if detectWindows, let w = windowList.first(where: { $0.frame.contains(mouse) }) {
            let r = w.frame.intersection(s.frame)
            if !r.isNull && r.width > 4 && r.height > 4 { return r }
        }
        return s.frame
    }

    var highlight: CGRect? { selection ?? hovered }

    func screen(containing p: CGPoint) -> NSScreen? {
        frozen.first { NSMouseInRect(p, $0.screen.frame, false) }?.screen
    }

    func frozenScreen(containing p: CGPoint) -> FrozenScreen? {
        frozen.first { NSMouseInRect(p, $0.screen.frame, false) }
    }

    func snapCandidates() -> [CGRect] {
        windowList.map(\.frame) + frozen.map(\.screen.frame)
    }

    func refresh() {
        for w in windows { w.overlayView.update() }
    }

    // MARK: input (called by the views)

    func mouseMoved(to p: CGPoint) {
        mouse = p
        refresh()
    }

    func mouseDown(at p: CGPoint, snapping: Bool) {
        let start = snapping ? EdgeSnapper.snap(p, toEdgesOf: snapCandidates()) : p
        dragStart = start
        dragCurrent = start
        isDragging = false
        mouse = p
    }

    func mouseDragged(to p: CGPoint, snapping: Bool) {
        guard let start = dragStart else { return }
        mouse = p
        if spaceHeld, let anchor = spaceAnchor, let cur = dragCurrent {
            // Space held: move the whole selection.
            let d = CGSize(width: p.x - anchor.x, height: p.y - anchor.y)
            dragStart = CGPoint(x: start.x + d.width, y: start.y + d.height)
            dragCurrent = CGPoint(x: cur.x + d.width, y: cur.y + d.height)
            spaceAnchor = p
            refresh()
            return
        }
        let target = snapping ? EdgeSnapper.snap(p, toEdgesOf: snapCandidates()) : p
        if !isDragging && hypot(p.x - start.x, p.y - start.y) >= 3 { isDragging = true }
        dragCurrent = target
        refresh()
    }

    func mouseUp(at p: CGPoint) {
        mouse = p
        if isDragging, let sel = selection, sel.width >= 2, sel.height >= 2 {
            finish(sel, near: dragStart ?? p)
        } else if let h = hovered {
            finish(h, near: p)
        }
        dragStart = nil
        dragCurrent = nil
        isDragging = false
    }

    func setSpace(_ held: Bool) {
        spaceHeld = held
        spaceAnchor = held ? mouse : nil
    }

    func confirm() {
        if let r = highlight { finish(r, near: isDragging ? (dragStart ?? mouse) : mouse) }
    }

    func escape() {
        if isDragging || dragStart != nil {
            dragStart = nil
            dragCurrent = nil
            isDragging = false
            refresh()
        } else {
            close(with: nil)
        }
    }

    func fullscreen() {
        if let s = screen(containing: mouse) { finish(s.frame, near: mouse) }
    }

    func toggleDetection() {
        detectWindows.toggle()
        refresh()
    }

    /// Moves the real cursor by whole pixels (arrow keys).
    func nudge(dx: CGFloat, dy: CGFloat) {
        let scale = frozenScreen(containing: mouse)?.scale ?? 1
        let p = CGPoint(x: mouse.x + dx / scale, y: mouse.y + dy / scale)
        let mainHeight = NSScreen.screens.first?.frame.height ?? 0
        CGWarpMouseCursorPosition(CGPoint(x: p.x, y: mainHeight - p.y))
        CGAssociateMouseAndMouseCursorPosition(1)
        if dragStart != nil { mouseDragged(to: p, snapping: false) } else { mouseMoved(to: p) }
    }

    func copyColorUnderCursor() {
        guard let f = frozenScreen(containing: mouse), let c = PixelProbe.color(in: f, at: mouse) else { return }
        Clipboard.copyText(c.hex)
        for w in windows { w.overlayView.flash(L.f("Copied {0}", c.hex)) }
    }

    private func finish(_ rect: CGRect, near p: CGPoint) {
        guard let f = frozenScreen(containing: CGPoint(x: rect.midX, y: rect.midY)) ?? frozenScreen(containing: p) else {
            close(with: nil)
            return
        }
        let clipped = rect.intersection(f.screen.frame)
        close(with: clipped.isNull ? nil : OverlayResult(frozen: f, rect: clipped))
    }

    private func close(with result: OverlayResult?) {
        let done = completion
        completion = nil
        for w in windows { w.orderOut(nil) }
        windows.removeAll()
        NSCursor.arrow.set()
        done?(result)
    }
}

// MARK: - Pixel probe

enum PixelProbe {
    struct Color { let r: UInt8, g: UInt8, b: UInt8
        var hex: String { String(format: "#%02X%02X%02X", r, g, b) }
        var ns: NSColor { NSColor(srgbRed: CGFloat(r) / 255, green: CGFloat(g) / 255, blue: CGFloat(b) / 255, alpha: 1) }
    }

    /// Pixel position (top-left origin) of a global point on a frozen screen.
    static func pixel(in f: FrozenScreen, at p: CGPoint) -> (x: Int, y: Int) {
        let fr = f.screen.frame
        let x = Int(floor((p.x - fr.minX) * f.scale))
        let y = Int(floor((fr.maxY - p.y) * f.scale))
        return (min(max(0, x), f.image.width - 1), min(max(0, y), f.image.height - 1))
    }

    static func color(in f: FrozenScreen, at p: CGPoint) -> Color? {
        let px = pixel(in: f, at: p)
        guard let one = f.image.cropping(to: CGRect(x: px.x, y: px.y, width: 1, height: 1)) else { return nil }
        var data = [UInt8](repeating: 0, count: 4)
        let ok: Bool = data.withUnsafeMutableBytes { buf in
            guard let ctx = CGContext(data: buf.baseAddress, width: 1, height: 1, bitsPerComponent: 8, bytesPerRow: 4,
                                      space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                      bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return false }
            ctx.draw(one, in: CGRect(x: 0, y: 0, width: 1, height: 1))
            return true
        }
        return ok ? Color(r: data[0], g: data[1], b: data[2]) : nil
    }
}

// MARK: - Window and view

final class OverlayWindow: NSWindow {
    let overlayView: OverlayView

    init(frozen: FrozenScreen, overlay: CaptureOverlay) {
        overlayView = OverlayView(frozen: frozen, overlay: overlay)
        super.init(contentRect: frozen.screen.frame, styleMask: [.borderless], backing: .buffered, defer: false)
        setFrame(frozen.screen.frame, display: false)
        level = .screenSaver
        isOpaque = true
        backgroundColor = .black
        hasShadow = false
        animationBehavior = .none
        isReleasedWhenClosed = false
        acceptsMouseMovedEvents = true
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        contentView = overlayView
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

final class OverlayView: NSView {
    private let frozen: FrozenScreen
    private unowned let overlay: CaptureOverlay
    private let imageLayer = CALayer()
    private let dimLayer = CAShapeLayer()
    private let borderLayer = CAShapeLayer()
    private let sizeTag = TagView()
    private let hint = TagView()
    private let loupe: LoupeView
    private var loupeCornerNow: LoupeCorner
    private var flashTimer: Timer?

    init(frozen: FrozenScreen, overlay: CaptureOverlay) {
        self.frozen = frozen
        self.overlay = overlay
        self.loupe = LoupeView(frozen: frozen)
        self.loupeCornerNow = overlay.loupeCorner
        super.init(frame: CGRect(origin: .zero, size: frozen.screen.frame.size))
        wantsLayer = true
        layer?.backgroundColor = NSColor.black.cgColor

        imageLayer.frame = bounds
        imageLayer.contents = frozen.image
        imageLayer.contentsGravity = .resize
        imageLayer.magnificationFilter = .nearest
        layer?.addSublayer(imageLayer)

        dimLayer.fillRule = .evenOdd
        dimLayer.fillColor = NSColor.black.withAlphaComponent(0.42).cgColor
        dimLayer.frame = bounds
        layer?.addSublayer(dimLayer)

        borderLayer.fillColor = nil
        borderLayer.strokeColor = Theme.blue.cgColor
        borderLayer.lineWidth = 2
        borderLayer.shadowColor = Theme.blue.cgColor
        borderLayer.shadowRadius = 8
        borderLayer.shadowOpacity = 0.9
        borderLayer.shadowOffset = .zero
        borderLayer.frame = bounds
        layer?.addSublayer(borderLayer)

        addSubview(sizeTag)
        addSubview(loupe)
        hint.style = .hint
        switch overlay.purpose {
        case .recording: hint.text = L.t("Pick what to record: click a window or drag an area · F whole screen · Esc cancel")
        case .text: hint.text = L.t("Drag over the text to copy, or click a window · Esc cancel")
        case .scrolling: hint.text = L.t("Click the window to scroll, or drag over the part that scrolls · Esc cancel")
        case .screenshot: hint.text = L.t("Click a window or drag an area · F whole screen · W window detection · Esc cancel")
        }
        addSubview(hint)
        hint.sizeToFitText()
        hint.setFrameOrigin(CGPoint(x: (bounds.width - hint.frame.width) / 2, y: bounds.height - hint.frame.height - 40))
        DispatchQueue.main.asyncAfter(deadline: .now() + 2.6) { [weak self] in
            NSAnimationContext.runAnimationGroup { ctx in
                ctx.duration = 0.4
                self?.hint.animator().alphaValue = 0
            }
        }
    }

    required init?(coder: NSCoder) { fatalError() }

    override var acceptsFirstResponder: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(rect: bounds, options: [.mouseMoved, .activeAlways, .inVisibleRect, .cursorUpdate], owner: self))
    }

    override func cursorUpdate(with event: NSEvent) { NSCursor.crosshair.set() }
    override func resetCursorRects() { addCursorRect(bounds, cursor: .crosshair) }

    private func global(_ e: NSEvent) -> CGPoint {
        guard let w = window else { return NSEvent.mouseLocation }
        return w.convertPoint(toScreen: e.locationInWindow)
    }

    private func local(_ r: CGRect) -> CGRect {
        let o = frozen.screen.frame.origin
        return r.offsetBy(dx: -o.x, dy: -o.y)
    }

    override func mouseMoved(with event: NSEvent) {
        if let w = window, !w.isKeyWindow { w.makeKey() }
        NSCursor.crosshair.set()
        overlay.mouseMoved(to: global(event))
    }

    override func mouseDown(with event: NSEvent) {
        overlay.mouseDown(at: global(event), snapping: !event.modifierFlags.contains(.option))
    }

    override func mouseDragged(with event: NSEvent) {
        overlay.mouseDragged(to: global(event), snapping: !event.modifierFlags.contains(.option))
    }

    override func mouseUp(with event: NSEvent) {
        if event.clickCount >= 2 { return }
        overlay.mouseUp(at: global(event))
    }

    override func rightMouseDown(with event: NSEvent) { overlay.escape() }

    override func keyDown(with event: NSEvent) {
        let step: CGFloat = event.modifierFlags.contains(.shift) ? 10 : 1
        switch Int(event.keyCode) {
        case 53: overlay.escape()                       // Esc
        case 36, 76: overlay.confirm()                  // Return / Enter
        case 123: overlay.nudge(dx: -step, dy: 0)       // ←
        case 124: overlay.nudge(dx: step, dy: 0)        // →
        case 125: overlay.nudge(dx: 0, dy: -step)       // ↓
        case 126: overlay.nudge(dx: 0, dy: step)        // ↑
        case 49: if !event.isARepeat { overlay.setSpace(true) }   // Space
        default:
            switch event.charactersIgnoringModifiers?.lowercased() {
            case "f": overlay.fullscreen()
            case "w": overlay.toggleDetection()
            case "c": overlay.copyColorUnderCursor()
            default: break
            }
        }
    }

    override func keyUp(with event: NSEvent) {
        if event.keyCode == 49 { overlay.setSpace(false) }
    }

    func flash(_ text: String) {
        hint.text = text
        hint.sizeToFitText()
        hint.setFrameOrigin(CGPoint(x: (bounds.width - hint.frame.width) / 2, y: bounds.height - hint.frame.height - 40))
        hint.alphaValue = 1
        flashTimer?.invalidate()
        flashTimer = Timer.scheduledTimer(withTimeInterval: 1.2, repeats: false) { [weak self] _ in
            self?.hint.alphaValue = 0
        }
    }

    /// Re-applies the shared overlay state to this screen.
    func update() {
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        let full = CGPath(rect: bounds, transform: nil)
        if let h = overlay.highlight, h.intersects(frozen.screen.frame) {
            let r = local(h.intersection(frozen.screen.frame))
            let path = CGMutablePath()
            path.addPath(full)
            path.addRect(r)
            dimLayer.path = path
            borderLayer.path = CGPath(rect: r.insetBy(dx: 1, dy: 1), transform: nil)
            borderLayer.isHidden = false
            let s = frozen.scale
            sizeTag.text = "\(Int((h.width * s).rounded())) × \(Int((h.height * s).rounded()))"
            sizeTag.sizeToFitText()
            var o = CGPoint(x: r.minX, y: r.maxY + 6)
            if o.y + sizeTag.frame.height > bounds.height - 4 { o.y = r.maxY - sizeTag.frame.height - 6; o.x = r.minX + 6 }
            o.x = min(max(4, o.x), bounds.width - sizeTag.frame.width - 4)
            sizeTag.setFrameOrigin(o)
            sizeTag.isHidden = false
        } else {
            dimLayer.path = full
            borderLayer.isHidden = true
            sizeTag.isHidden = true
        }
        CATransaction.commit()
        updateLoupe()
    }

    private func updateLoupe() {
        let onThisScreen = NSMouseInRect(overlay.mouse, frozen.screen.frame, false)
        loupe.isHidden = !onThisScreen
        guard onThisScreen else { return }
        let size = loupe.frame.size
        let cursor = CGPoint(x: overlay.mouse.x - frozen.screen.frame.minX, y: overlay.mouse.y - frozen.screen.frame.minY)
        let rect: CGRect
        if overlay.loupeCorner == .followCursor {
            rect = LoupePlacement.followRect(in: bounds, size: size, cursor: cursor)
        } else {
            loupeCornerNow = LoupePlacement.resolveCorner(preferred: overlay.loupeCorner, current: loupeCornerNow,
                                                          screen: bounds, size: size, cursor: cursor)
            rect = LoupePlacement.rect(for: loupeCornerNow, in: bounds, size: size)
        }
        if loupe.frame.origin != rect.origin {
            if overlay.loupeCorner == .followCursor {
                loupe.setFrameOrigin(rect.origin)
            } else {
                NSAnimationContext.runAnimationGroup { ctx in
                    ctx.duration = 0.12
                    loupe.animator().setFrameOrigin(rect.origin)
                }
            }
        }
        let sel = overlay.highlight
        loupe.update(cursor: overlay.mouse, selectionPixels: sel.map { CGSize(width: $0.width * frozen.scale, height: $0.height * frozen.scale) })
    }
}

/// Small rounded label: the yellow size tag, or a dark hint pill.
final class TagView: NSView {
    enum Style { case size, hint }
    var style: Style = .size
    var text = "" { didSet { needsDisplay = true } }

    private var attributes: [NSAttributedString.Key: Any] {
        switch style {
        case .size: return [.font: NSFont.monospacedDigitSystemFont(ofSize: 12, weight: .bold), .foregroundColor: Theme.night]
        case .hint: return [.font: Theme.ui(13, .medium), .foregroundColor: NSColor.white]
        }
    }

    func sizeToFitText() {
        let s = (text as NSString).size(withAttributes: attributes)
        setFrameSize(NSSize(width: ceil(s.width) + (style == .hint ? 28 : 14), height: ceil(s.height) + (style == .hint ? 14 : 6)))
    }

    override func draw(_ dirtyRect: NSRect) {
        let path = NSBezierPath(roundedRect: bounds, xRadius: style == .hint ? bounds.height / 2 : 5, yRadius: style == .hint ? bounds.height / 2 : 5)
        (style == .hint ? Theme.night.withAlphaComponent(0.92) : Theme.yellow).setFill()
        path.fill()
        if style == .hint {
            Theme.line.setStroke()
            path.lineWidth = 1
            path.stroke()
        }
        let s = (text as NSString).size(withAttributes: attributes)
        (text as NSString).draw(at: CGPoint(x: (bounds.width - s.width) / 2, y: (bounds.height - s.height) / 2), withAttributes: attributes)
    }
}

/// The magnifier: 15×15 real pixels with a grid and crosshair, X/Y, selection size and the
/// hex color under the cursor.
final class LoupeView: NSView {
    static let cells = 15
    static let cell: CGFloat = 9
    private let frozen: FrozenScreen
    private var crop: CGImage?
    private var pixel = (x: 0, y: 0)
    private var color: PixelProbe.Color?
    private var selection: CGSize?

    init(frozen: FrozenScreen) {
        self.frozen = frozen
        let side = CGFloat(LoupeView.cells) * LoupeView.cell
        super.init(frame: CGRect(x: 0, y: 0, width: side + 12, height: side + 12 + 54))
        wantsLayer = true
        layer?.cornerRadius = 12
        layer?.backgroundColor = Theme.night.withAlphaComponent(0.95).cgColor
        layer?.borderColor = Theme.line.cgColor
        layer?.borderWidth = 1
        layer?.shadowOpacity = 0.5
        layer?.shadowRadius = 10
    }

    required init?(coder: NSCoder) { fatalError() }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    func update(cursor: CGPoint, selectionPixels: CGSize?) {
        pixel = PixelProbe.pixel(in: frozen, at: cursor)
        let half = LoupeView.cells / 2
        crop = frozen.image.cropping(to: CGRect(x: pixel.x - half, y: pixel.y - half, width: LoupeView.cells, height: LoupeView.cells))
        color = PixelProbe.color(in: frozen, at: cursor)
        selection = selectionPixels
        needsDisplay = true
    }

    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        let side = CGFloat(LoupeView.cells) * LoupeView.cell
        let zoom = CGRect(x: 6, y: bounds.height - 6 - side, width: side, height: side)
        ctx.saveGState()
        ctx.addPath(CGPath(roundedRect: zoom, cornerWidth: 6, cornerHeight: 6, transform: nil))
        ctx.clip()
        NSColor.black.setFill()
        ctx.fill(zoom)
        if let crop {
            // The crop can be smaller near screen edges: keep the centre pixel in the middle.
            let half = LoupeView.cells / 2
            let offX = CGFloat(max(0, half - pixel.x)) * LoupeView.cell
            let offTop = CGFloat(max(0, half - pixel.y)) * LoupeView.cell
            let w = CGFloat(crop.width) * LoupeView.cell, h = CGFloat(crop.height) * LoupeView.cell
            ctx.interpolationQuality = .none
            ctx.draw(crop, in: CGRect(x: zoom.minX + offX, y: zoom.maxY - offTop - h, width: w, height: h))
        }
        // Pixel grid.
        ctx.setStrokeColor(NSColor.white.withAlphaComponent(0.12).cgColor)
        ctx.setLineWidth(0.5)
        for i in 1..<LoupeView.cells {
            let d = CGFloat(i) * LoupeView.cell
            ctx.move(to: CGPoint(x: zoom.minX + d, y: zoom.minY)); ctx.addLine(to: CGPoint(x: zoom.minX + d, y: zoom.maxY))
            ctx.move(to: CGPoint(x: zoom.minX, y: zoom.minY + d)); ctx.addLine(to: CGPoint(x: zoom.maxX, y: zoom.minY + d))
        }
        ctx.strokePath()
        // Centre pixel.
        let c = CGFloat(LoupeView.cells / 2) * LoupeView.cell
        let centre = CGRect(x: zoom.minX + c, y: zoom.minY + c, width: LoupeView.cell, height: LoupeView.cell)
        ctx.setStrokeColor(NSColor.black.cgColor)
        ctx.setLineWidth(2.5)
        ctx.stroke(centre.insetBy(dx: -1, dy: -1))
        ctx.setStrokeColor(NSColor.white.cgColor)
        ctx.setLineWidth(1.2)
        ctx.stroke(centre.insetBy(dx: -1, dy: -1))
        ctx.restoreGState()

        let mono = NSFont.monospacedDigitSystemFont(ofSize: 11, weight: .medium)
        let white: [NSAttributedString.Key: Any] = [.font: mono, .foregroundColor: NSColor.white]
        let muted: [NSAttributedString.Key: Any] = [.font: mono, .foregroundColor: Theme.muted]
        var y = zoom.minY - 18
        ("X \(pixel.x)  Y \(pixel.y)" as NSString).draw(at: CGPoint(x: 10, y: y), withAttributes: white)
        if let selection {
            let s = "\(Int(selection.width.rounded())) × \(Int(selection.height.rounded()))" as NSString
            let w = s.size(withAttributes: white).width
            s.draw(at: CGPoint(x: bounds.width - 10 - w, y: y), withAttributes: [.font: mono, .foregroundColor: Theme.yellow])
        }
        y -= 18
        if let color {
            let sw = CGRect(x: 10, y: y + 1, width: 12, height: 12)
            color.ns.setFill()
            NSBezierPath(roundedRect: sw, xRadius: 3, yRadius: 3).fill()
            NSColor.white.withAlphaComponent(0.5).setStroke()
            NSBezierPath(roundedRect: sw, xRadius: 3, yRadius: 3).stroke()
            (color.hex as NSString).draw(at: CGPoint(x: 28, y: y), withAttributes: white)
            let hintText = L.t("C copy") as NSString
            let w = hintText.size(withAttributes: muted).width
            hintText.draw(at: CGPoint(x: bounds.width - 10 - w, y: y), withAttributes: muted)
        }
    }
}
