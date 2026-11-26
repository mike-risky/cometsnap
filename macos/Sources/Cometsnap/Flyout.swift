import AppKit
import AVFoundation
import CometsnapCore
import ImageIO

/// The menu bar flyout: a starry header, big capture buttons, search and the latest captures,
/// 3 per row. Click a thumbnail to copy it, double-click to edit it, drag it into any app.
/// Created once and kept hidden, so it pops instantly.
final class FlyoutPanel: NSPanel, NSSearchFieldDelegate {
    static let width: CGFloat = 486
    static let height: CGFloat = 620
    private unowned let app: AppController
    private let header: StarHeaderView
    private let search = NSSearchField()
    private let grid = FlippedView()
    private let scroll = NSScrollView()
    private let empty = NSTextField(labelWithString: "")
    private let status = NSTextField(labelWithString: "")
    private var tiles: [TileView] = []
    private var clickMonitor: Any?
    private var keyMonitor: Any?
    private var konami = KonamiCode()
    private(set) var dirty = true

    init(app: AppController) {
        self.app = app
        header = StarHeaderView(frame: CGRect(x: 0, y: 0, width: FlyoutPanel.width, height: 74))
        super.init(contentRect: CGRect(x: 0, y: 0, width: FlyoutPanel.width, height: FlyoutPanel.height),
                   styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        isFloatingPanel = true
        level = .statusBar
        backgroundColor = .clear
        isOpaque = false
        hasShadow = true
        hidesOnDeactivate = false
        isReleasedWhenClosed = false
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient, .ignoresCycle]
        appearance = NSAppearance(named: .darkAqua)
        build()
    }

    override var canBecomeKey: Bool { true }

    private func build() {
        let W = FlyoutPanel.width, H = FlyoutPanel.height
        let root = FlippedView(frame: CGRect(x: 0, y: 0, width: W, height: H))
        root.wantsLayer = true
        root.layer?.cornerRadius = 14
        root.layer?.masksToBounds = true
        root.layer?.backgroundColor = Theme.night.cgColor
        root.layer?.borderColor = Theme.line.cgColor
        root.layer?.borderWidth = 1
        contentView = root

        header.frame = CGRect(x: 0, y: 0, width: W, height: 74)
        root.addSubview(header)

        // Big capture buttons.
        let pad: CGFloat = 14, gap: CGFloat = 8
        let bw = (W - pad * 2 - gap * 3) / 4
        let y0: CGFloat = 84
        let keys = app.hotkeyDisplay
        let region = SpaceButton(title: L.t("Region / Window"), symbol: "viewfinder", fill: Theme.terracotta, vertical: true, fontSize: 11,
                                 tip: L.f("Capture a region or a window ({0})", keys.capture))
        region.onClick = { [weak self] in self?.app.capture.startRegion() }
        let full = SpaceButton(title: L.t("Fullscreen"), symbol: "rectangle.inset.filled", vertical: true, fontSize: 11,
                               tip: L.t("Capture the screen under the mouse"))
        full.onClick = { [weak self] in self?.app.capture.captureFullscreen() }
        let delayed = SpaceButton(title: L.t("Delayed"), symbol: "timer", vertical: true, fontSize: 11, tip: L.t("Capture after 3, 5 or 10 seconds: time to open a menu or hover something first"))
        delayed.onClick = { [weak self, weak delayed] in
            guard let self, let delayed else { return }
            self.popMenu(self.delayMenu(), from: delayed)
        }
        let record = SpaceButton(title: L.t("Record"), symbol: "record.circle", vertical: true, fontSize: 11,
                                 tip: L.f("Record a video ({0}) or a GIF ({1})", keys.video, keys.gif))
        record.onClick = { [weak self, weak record] in
            guard let self, let record else { return }
            self.popMenu(self.recordMenu(), from: record)
        }
        for (i, b) in [region, full, delayed, record].enumerated() {
            b.frame = CGRect(x: pad + CGFloat(i) * (bw + gap), y: y0, width: bw, height: 66)
            root.addSubview(b)
        }

        search.frame = CGRect(x: pad, y: y0 + 78, width: W - pad * 2, height: 26)
        search.placeholderString = L.t("Search by name or date (e.g. 2026-11-26)")
        search.delegate = self
        search.focusRingType = .none
        root.addSubview(search)

        let recent = NSTextField(labelWithString: L.t("RECENT"))
        recent.font = Theme.heading(12)
        recent.textColor = Theme.muted
        recent.frame = CGRect(x: pad + 2, y: y0 + 114, width: 200, height: 18)
        root.addSubview(recent)

        let gridTop = y0 + 136
        let footerH: CGFloat = 40
        scroll.frame = CGRect(x: 0, y: gridTop, width: W, height: H - gridTop - footerH)
        scroll.drawsBackground = false
        scroll.hasVerticalScroller = true
        scroll.autohidesScrollers = true
        scroll.scrollerStyle = .overlay
        scroll.documentView = grid
        grid.frame = CGRect(x: 0, y: 0, width: W, height: 10)
        root.addSubview(scroll)

        empty.stringValue = L.t("No captures yet.") + "\n" + L.f("Press {0} and grab something!", keys.capture) + " ☄️"
        empty.alignment = .center
        empty.textColor = Theme.muted
        empty.font = Theme.ui(14)
        empty.maximumNumberOfLines = 3
        empty.frame = CGRect(x: 20, y: gridTop + 90, width: W - 40, height: 50)
        root.addSubview(empty)

        let line = NSView(frame: CGRect(x: 0, y: H - footerH, width: W, height: 1))
        line.wantsLayer = true
        line.layer?.backgroundColor = Theme.line.cgColor
        root.addSubview(line)

        let folder = LinkLabel(L.t("Open folder")) { [weak self] in self?.hide(); self?.app.openCapturesFolder() }
        let settings = LinkLabel(L.t("Settings")) { [weak self] in self?.hide(); self?.app.showSettings() }
        let quit = LinkLabel(L.t("Quit")) { NSApp.terminate(nil) }
        folder.sizeToFit(); settings.sizeToFit(); quit.sizeToFit()
        let fy = H - footerH + 12
        folder.setFrameOrigin(CGPoint(x: pad, y: fy))
        status.font = Theme.ui(11)
        status.textColor = Theme.muted
        status.frame = CGRect(x: folder.frame.maxX + 14, y: fy, width: 200, height: 16)
        quit.setFrameOrigin(CGPoint(x: W - pad - quit.frame.width, y: fy))
        settings.setFrameOrigin(CGPoint(x: quit.frame.minX - 16 - settings.frame.width, y: fy))
        for v in [folder, status, settings, quit] as [NSView] { root.addSubview(v) }
    }

    private func delayMenu() -> NSMenu {
        let m = NSMenu()
        let explain = NSMenuItem(title: L.t("Wait a few seconds before capturing, so you can open a menu or a tooltip first."), action: nil, keyEquivalent: "")
        explain.isEnabled = false
        m.addItem(explain)
        m.addItem(.separator())
        for s in [3, 5, 10] {
            let i = ClosureMenuItem(title: L.f("{0} seconds", s)) { [weak self] in self?.app.capture.captureDelayed(s) }
            m.addItem(i)
        }
        return m
    }

    private func recordMenu() -> NSMenu {
        let m = NSMenu()
        if app.recorder.isRecording {
            m.addItem(ClosureMenuItem(title: L.t("Stop recording")) { [weak self] in self?.app.recorder.stop() })
            return m
        }
        let k = app.hotkeyDisplay
        m.addItem(ClosureMenuItem(title: L.t("Video of a region") + "  (\(k.video))") { [weak self] in self?.app.recorder.toggleRegion(format: .mp4) })
        m.addItem(ClosureMenuItem(title: L.t("Video of the whole screen")) { [weak self] in self?.app.recorder.startFullscreen(format: .mp4) })
        m.addItem(ClosureMenuItem(title: L.t("GIF of a region") + "  (\(k.gif))") { [weak self] in self?.app.recorder.toggleRegion(format: .gif) })
        return m
    }

    private func popMenu(_ m: NSMenu, from v: NSView) {
        m.popUp(positioning: nil, at: CGPoint(x: 0, y: -4), in: v)
    }

    // MARK: show / hide

    func toggle(near anchor: CGRect?) {
        if isVisible { hide() } else { show(near: anchor) }
    }

    func show(near anchor: CGRect?) {
        let screen = anchor.flatMap { NSScreen.containing(CGPoint(x: $0.midX, y: $0.midY)) } ?? NSScreen.underMouse
        let vf = screen.visibleFrame
        var x = (anchor?.midX ?? vf.maxX) - FlyoutPanel.width / 2
        x = min(max(vf.minX + 8, x), vf.maxX - FlyoutPanel.width - 8)
        let y = vf.maxY - FlyoutPanel.height - 6
        setFrame(CGRect(x: x, y: y, width: FlyoutPanel.width, height: FlyoutPanel.height), display: false)
        if dirty { populate() }
        alphaValue = 0
        makeKeyAndOrderFront(nil)
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = 0.14
            self.animator().alphaValue = 1
        }
        header.start()
        clickMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] _ in
            self?.hide()
        }
        // ↑ ↑ ↓ ↓ ← → ← → B A while the panel is open.
        konami.reset()
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self, event.window === self else { return event }
            if self.konami.feed(FlyoutPanel.konamiKey(event)) {
                self.hide()
                self.app.meteorShowerEasterEgg()
            }
            return event
        }
    }

    private static func konamiKey(_ e: NSEvent) -> KonamiKey {
        switch e.keyCode {
        case 126: return .up
        case 125: return .down
        case 123: return .left
        case 124: return .right
        default:
            switch e.charactersIgnoringModifiers?.lowercased() {
            case "b": return .b
            case "a": return .a
            default: return .other
            }
        }
    }

    func hide() {
        guard isVisible else { return }
        if let m = clickMonitor { NSEvent.removeMonitor(m); clickMonitor = nil }
        if let m = keyMonitor { NSEvent.removeMonitor(m); keyMonitor = nil }
        header.stop()
        orderOut(nil)
    }

    override func resignKey() {
        super.resignKey()
        DispatchQueue.main.async { [weak self] in
            guard let self, self.isVisible, !self.isKeyWindow else { return }
            self.hide()
        }
    }

    override func cancelOperation(_ sender: Any?) { hide() }

    func controlTextDidChange(_ obj: Notification) { populate() }

    func markDirty() {
        dirty = true
        if isVisible { populate() }
    }

    // MARK: grid

    func populate() {
        dirty = false
        let all = app.recentItems
        let items = all.filter { $0.matches(search.stringValue) }.prefix(30)
        tiles.forEach { $0.removeFromSuperview() }
        tiles.removeAll()
        let pad: CGFloat = 14, gap: CGFloat = 9
        let tw = (FlyoutPanel.width - pad * 2 - gap * 2) / 3
        let th = tw * 0.66
        for (i, item) in items.enumerated() {
            let row = i / 3, col = i % 3
            let t = TileView(item: item, app: app)
            t.frame = CGRect(x: pad + CGFloat(col) * (tw + gap), y: 4 + CGFloat(row) * (th + gap), width: tw, height: th)
            grid.addSubview(t)
            tiles.append(t)
        }
        let rows = (items.count + 2) / 3
        grid.frame = CGRect(x: 0, y: 0, width: FlyoutPanel.width, height: max(scroll.contentSize.height, 8 + CGFloat(rows) * (th + gap)))
        empty.isHidden = !all.isEmpty
        let count = all.count
        status.stringValue = count == 0 ? "" : (count >= 60 ? L.t("60+ captures") : count == 1 ? L.t("1 capture") : L.f("{0} captures", count))
    }
}

final class FlippedView: NSView {
    override var isFlipped: Bool { true }
}

final class ClosureMenuItem: NSMenuItem {
    private let handler: () -> Void

    init(title: String, handler: @escaping () -> Void) {
        self.handler = handler
        super.init(title: title, action: #selector(run), keyEquivalent: "")
        target = self
    }

    required init(coder: NSCoder) { fatalError() }

    @objc private func run() { handler() }
}

// MARK: - Tile

/// One thumbnail. Click copies, double-click edits (or plays a recording), drag hands the file
/// to any app; hover shows quick actions.
final class TileView: NSView, NSDraggingSource {
    let item: RecentItem
    private unowned let app: AppController
    private let imageLayer = CALayer()
    private var hovering = false { didSet { updateHover() } }
    private var downPoint: CGPoint?
    private let actions = NSStackView()
    private let badge = NSTextField(labelWithString: "")
    private let flash = NSTextField(labelWithString: L.t("COPIED!"))

    init(item: RecentItem, app: AppController) {
        self.item = item
        self.app = app
        super.init(frame: .zero)
        wantsLayer = true
        layer?.cornerRadius = 8
        layer?.masksToBounds = true
        layer?.backgroundColor = Theme.surface.cgColor
        layer?.borderWidth = 1
        layer?.borderColor = Theme.line.cgColor
        imageLayer.contentsGravity = .resizeAspectFill
        imageLayer.masksToBounds = true
        layer?.addSublayer(imageLayer)
        toolTip = item.name + "\n" + (item.kind == .image ? L.t("Click: copy · Double-click: edit · Drag: drop into any app") : L.t("Click: copy · Double-click: trim · Drag: drop into any app"))

        if item.kind != .image {
            badge.stringValue = item.kind == .gif ? " GIF " : " ▶ MP4 "
            badge.font = Theme.ui(10, .bold)
            badge.textColor = .white
            badge.wantsLayer = true
            badge.drawsBackground = true
            badge.backgroundColor = Theme.recordRed
            badge.sizeToFit()
            addSubview(badge)
        }

        typealias Spec = (String, String, () -> Void)
        var specs: [Spec] = []
        if item.kind == .image {
            specs.append(("pencil", L.t("Edit"), { [weak self] in self?.primary() }))
        } else {
            specs.append(("scissors", L.t("Trim or cut"), { [weak self] in self?.primary() }))
            specs.append(("play.fill", L.t("Play"), { [weak self] in
                guard let self else { return }
                NSWorkspace.shared.open(self.item.url)
                self.app.flyout.hide()
            }))
        }
        specs.append(("doc.on.doc", L.t("Copy"), { [weak self] in self?.copyItem() }))
        specs.append(("folder", L.t("Show in Finder"), { [weak self] in if let self { NSWorkspace.shared.activateFileViewerSelecting([self.item.url]) } }))
        specs.append(("trash", L.t("Move to Trash"), { [weak self] in self?.trash() }))
        for (sym, tip, act) in specs {
            let b = SpaceButton(title: "", symbol: sym, fill: Theme.night.withAlphaComponent(0.85), tip: tip)
            b.layer?.cornerRadius = 6
            b.onClick = act
            b.translatesAutoresizingMaskIntoConstraints = false
            b.widthAnchor.constraint(equalToConstant: 24).isActive = true
            b.heightAnchor.constraint(equalToConstant: 22).isActive = true
            actions.addArrangedSubview(b)
        }
        actions.spacing = 3
        actions.isHidden = true
        addSubview(actions)

        flash.font = Theme.heading(13)
        flash.textColor = Theme.yellow
        flash.alignment = .center
        flash.wantsLayer = true
        flash.drawsBackground = true
        flash.backgroundColor = Theme.night.withAlphaComponent(0.8)
        flash.isHidden = true
        addSubview(flash)

        Thumbnails.shared.load(item) { [weak self] img in self?.imageLayer.contents = img }
    }

    required init?(coder: NSCoder) { fatalError() }

    override func layout() {
        super.layout()
        imageLayer.frame = bounds
        let size = actions.fittingSize
        actions.frame = CGRect(x: bounds.width - size.width - 4, y: bounds.height - size.height - 4, width: size.width, height: size.height)
        badge.setFrameOrigin(CGPoint(x: 5, y: 5))
        flash.frame = CGRect(x: 0, y: bounds.midY - 11, width: bounds.width, height: 22)
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .activeAlways, .inVisibleRect], owner: self))
    }

    override func mouseEntered(with event: NSEvent) { hovering = true }
    override func mouseExited(with event: NSEvent) { hovering = false }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    private func updateHover() {
        layer?.borderColor = (hovering ? Theme.blue : Theme.line).cgColor
        layer?.borderWidth = hovering ? 2 : 1
        actions.isHidden = !hovering
    }

    override func mouseDown(with event: NSEvent) {
        downPoint = event.locationInWindow
        if event.clickCount == 2 { primary() }
    }

    override func mouseDragged(with event: NSEvent) {
        guard let d = downPoint else { return }
        let p = event.locationInWindow
        guard hypot(p.x - d.x, p.y - d.y) > 4 else { return }
        downPoint = nil
        // The file and, for pictures, the PNG itself: drops into Finder, Mail, Slack, Pages, browsers…
        let preview = imageLayer.contents.map { NSImage(cgImage: $0 as! CGImage, size: bounds.size) }
        alphaValue = 0.55
        DragOut.start(from: self, event: event, url: item.url, image: nil, preview: preview, frame: bounds) { [weak self] _ in
            self?.alphaValue = 1
        }
    }

    override func mouseUp(with event: NSEvent) {
        if downPoint != nil && event.clickCount == 1 { copyItem() }
        downPoint = nil
    }

    func draggingSession(_ session: NSDraggingSession, sourceOperationMaskFor context: NSDraggingContext) -> NSDragOperation {
        context == .outsideApplication ? [.copy] : []
    }

    private func primary() {
        if item.kind == .image { app.openEditor(url: item.url) } else { app.openTrimmer(item.url) }
        app.flyout.hide()
    }

    private func copyItem() {
        if item.kind == .image, let img = PNG.load(item.url) {
            Clipboard.copy(img, scale: NSScreen.underMouse.backingScaleFactor)
        } else {
            Clipboard.copyFile(item.url)
        }
        flash.isHidden = false
        flash.alphaValue = 1
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.7) { [weak self] in
            NSAnimationContext.runAnimationGroup({ ctx in
                ctx.duration = 0.25
                self?.flash.animator().alphaValue = 0
            }, completionHandler: { self?.flash.isHidden = true })
        }
    }

    private func trash() {
        NSWorkspace.shared.recycle([item.url]) { [weak self] _, _ in
            if let self { EditSidecar.delete(for: self.item.url) }
            self?.app.capturesChanged()
        }
    }
}

// MARK: - Thumbnails

/// Small decoded previews, cached in memory. The flyout never decodes full images.
final class Thumbnails {
    static let shared = Thumbnails()
    private let cache = NSCache<NSString, CGImage>()
    private let queue = DispatchQueue(label: "cometsnap.thumbs", qos: .userInitiated, attributes: .concurrent)

    func load(_ item: RecentItem, done: @escaping (CGImage?) -> Void) {
        let key = "\(item.url.path)|\(item.modified.timeIntervalSince1970)" as NSString
        if let hit = cache.object(forKey: key) { done(hit); return }
        if item.kind == .video {
            Task {
                let gen = AVAssetImageGenerator(asset: AVURLAsset(url: item.url))
                gen.appliesPreferredTrackTransform = true
                gen.maximumSize = CGSize(width: 360, height: 360)
                let img = try? await gen.image(at: CMTime(seconds: 0.2, preferredTimescale: 600)).image
                await MainActor.run {
                    if let img { self.cache.setObject(img, forKey: key) }
                    done(img)
                }
            }
            return
        }
        queue.async {
            let img = Thumbnails.decode(item.url, maxPixels: 360)
            DispatchQueue.main.async {
                if let img { self.cache.setObject(img, forKey: key) }
                done(img)
            }
        }
    }

    static func decode(_ url: URL, maxPixels: Int) -> CGImage? {
        guard let src = CGImageSourceCreateWithURL(url as CFURL, nil) else { return nil }
        let opts: [String: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways as String: true,
            kCGImageSourceThumbnailMaxPixelSize as String: maxPixels,
            kCGImageSourceCreateThumbnailWithTransform as String: true,
        ]
        return CGImageSourceCreateThumbnailAtIndex(src, 0, opts as CFDictionary)
    }
}

// MARK: - Starry header

/// Night sky with twinkling stars, a floating comet and one shooting star each time the
/// flyout opens. Animations only run while the flyout is visible.
final class StarHeaderView: NSView {
    private let gradient = CAGradientLayer()
    private var stars: [CALayer] = []
    private let comet = CALayer()
    private let wordmark = CATextLayer()
    private let tagline = CATextLayer()
    private let shooting = CAGradientLayer()
    private var snow: SnowLayer?
    private var season: Season?

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        guard let root = layer else { return }
        gradient.colors = [Theme.surface.cgColor, Theme.night.cgColor]
        gradient.startPoint = CGPoint(x: 0, y: 1)
        gradient.endPoint = CGPoint(x: 1, y: 0)
        gradient.frame = bounds
        root.addSublayer(gradient)

        var rng = SystemRandomNumberGenerator()
        for _ in 0..<46 {
            let s = CALayer()
            let d = CGFloat.random(in: 1...2.6, using: &rng)
            s.frame = CGRect(x: CGFloat.random(in: 0...bounds.width, using: &rng), y: CGFloat.random(in: 0...bounds.height, using: &rng), width: d, height: d)
            s.cornerRadius = d / 2
            s.backgroundColor = (Int.random(in: 0..<7, using: &rng) == 0 ? Theme.yellow : NSColor.white).cgColor
            s.opacity = Float.random(in: 0.25...0.9, using: &rng)
            root.addSublayer(s)
            stars.append(s)
        }

        let scale = NSScreen.main?.backingScaleFactor ?? 2
        wordmark.string = Theme.wordmark(28)
        wordmark.contentsScale = scale
        wordmark.frame = CGRect(x: 16, y: bounds.height - 46, width: 300, height: 38)
        root.addSublayer(wordmark)

        tagline.string = NSAttributedString(string: L.t("Snap it. Mark it. Share it."),
                                            attributes: [.font: Theme.ui(11, .medium), .foregroundColor: Theme.muted])
        tagline.contentsScale = scale
        tagline.frame = CGRect(x: 18, y: 8, width: 300, height: 16)
        root.addSublayer(tagline)

        comet.contents = Theme.logo.cgImage(forProposedRect: nil, context: nil, hints: nil)
        comet.contentsGravity = .resizeAspect
        comet.frame = CGRect(x: bounds.width - 70, y: bounds.height / 2 - 26, width: 52, height: 52)
        root.addSublayer(comet)

        shooting.colors = [NSColor.white.withAlphaComponent(0).cgColor, NSColor.white.cgColor]
        shooting.startPoint = CGPoint(x: 0, y: 0.5)
        shooting.endPoint = CGPoint(x: 1, y: 0.5)
        shooting.frame = CGRect(x: 0, y: 0, width: 70, height: 1.6)
        shooting.cornerRadius = 0.8
        shooting.opacity = 0
        root.addSublayer(shooting)
    }

    required init?(coder: NSCoder) { fatalError() }

    /// Pumpkin comet in late October, snow in December (when Seasonal looks is on).
    private func applySeason() {
        let now = SeasonalArt.current
        guard now != season else { return }
        season = now
        comet.contents = SeasonalArt.decorate(Theme.logo, season: now).cgImage(forProposedRect: nil, context: nil, hints: nil)
        snow?.removeFromSuperlayer()
        snow = nil
        if now == .winter, let root = layer {
            let s = SnowLayer()
            s.fit(bounds)
            root.insertSublayer(s, above: gradient)
            snow = s
        }
    }

    func start() {
        applySeason()
        snow?.birthRate = 1
        for s in stars {
            let a = CABasicAnimation(keyPath: "opacity")
            a.fromValue = 0.15
            a.toValue = 1
            a.duration = Double.random(in: 0.9...2.8)
            a.autoreverses = true
            a.repeatCount = .infinity
            a.beginTime = CACurrentMediaTime() + Double.random(in: 0...2)
            s.add(a, forKey: "twinkle")
        }
        let float = CABasicAnimation(keyPath: "position.y")
        float.byValue = 4
        float.duration = 1.8
        float.autoreverses = true
        float.repeatCount = .infinity
        float.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
        comet.add(float, forKey: "float")

        // One shooting star, top right to bottom left (the brand's motion direction).
        let from = CGPoint(x: bounds.width * 0.85, y: bounds.height - 6)
        let to = CGPoint(x: bounds.width * 0.35, y: 4)
        shooting.setAffineTransform(CGAffineTransform(rotationAngle: atan2(from.y - to.y, from.x - to.x)))
        let move = CABasicAnimation(keyPath: "position")
        move.fromValue = NSValue(point: from)
        move.toValue = NSValue(point: to)
        let fade = CAKeyframeAnimation(keyPath: "opacity")
        fade.values = [0, 1, 1, 0]
        fade.keyTimes = [0, 0.15, 0.7, 1]
        let group = CAAnimationGroup()
        group.animations = [move, fade]
        group.duration = 0.75
        group.beginTime = CACurrentMediaTime() + 0.25
        group.timingFunction = CAMediaTimingFunction(name: .easeIn)
        group.fillMode = .backwards
        shooting.add(group, forKey: "shoot")
    }

    func stop() {
        snow?.birthRate = 0
        stars.forEach { $0.removeAllAnimations() }
        comet.removeAllAnimations()
        shooting.removeAllAnimations()
    }
}
