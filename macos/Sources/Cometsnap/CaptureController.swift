import AppKit
import CometsnapCore

/// Hotkey → freeze → overlay → copy + save on release. No toolbar in the way: editing is
/// optional, from the small Edit card or the flyout.
final class CaptureController {
    private unowned let app: AppController
    private var overlay: CaptureOverlay?
    private var previousApp: NSRunningApplication?
    private var starting = false

    init(app: AppController) { self.app = app }

    var isBusy: Bool { overlay != nil || starting }

    func startRegion() {
        openOverlay(purpose: .screenshot) { [weak self] result in
            guard let self, let result, let image = result.image else { return }
            self.deliver(image, scale: result.frozen.scale, from: result.rect)
        }
    }

    /// Lets the recorder reuse the overlay to choose an area.
    func pickRegion(_ completion: @escaping (OverlayResult?) -> Void) {
        openOverlay(purpose: .recording, completion)
    }

    func captureFullscreen() {
        guard !isBusy else { return }
        guard ensurePermission() else { return }
        let screen = NSScreen.underMouse
        app.hidePanels()
        starting = true
        Task { @MainActor in
            defer { self.starting = false }
            do {
                let frozen = try await ScreenGrabber.shared.freezeAll(screens: [screen])
                guard let f = frozen.first else { return }
                self.deliver(f.image, scale: f.scale, from: screen.frame)
            } catch {
                self.app.report(error)
            }
        }
    }

    func captureDelayed(_ seconds: Int) {
        app.hidePanels()
        app.statusItem.countdown(seconds) { [weak self] in self?.startRegion() }
    }

    private func openOverlay(purpose: CaptureOverlay.Purpose, _ completion: @escaping (OverlayResult?) -> Void) {
        guard !isBusy else { return }
        guard ensurePermission() else { return }
        let front = NSWorkspace.shared.frontmostApplication
        previousApp = front?.processIdentifier == ProcessInfo.processInfo.processIdentifier ? nil : front
        app.hidePanels()
        let windows = ScreenGrabber.windows()
        starting = true
        Task { @MainActor in
            do {
                let frozen = try await ScreenGrabber.shared.freezeAll()
                self.starting = false
                guard !frozen.isEmpty else { return }
                let o = CaptureOverlay(frozen: frozen, windows: windows, purpose: purpose,
                                       loupeCorner: self.app.settings.loupeCorner) { [weak self] result in
                    guard let self else { return }
                    self.overlay = nil
                    self.restoreFocus()
                    completion(result)
                }
                self.overlay = o
                o.show()
            } catch {
                self.starting = false
                self.app.report(error)
            }
        }
    }

    private func restoreFocus() {
        if let p = previousApp, !p.isTerminated { _ = p.activate(options: []) }
        previousApp = nil
    }

    private func ensurePermission() -> Bool {
        if ScreenGrabber.hasPermission { return true }
        app.showPermissionHelp()
        return false
    }

    /// Copy first (instant), then save on a background queue.
    func deliver(_ image: CGImage, scale: CGFloat, from rect: CGRect) {
        let settings = app.settings
        if settings.copyToClipboard { Clipboard.copy(image, scale: scale) }
        if settings.playSound { Sound.snap() }
        if settings.cometAnimation { CometFlight.fly(from: rect, to: app.statusItem.buttonFrame, image: image) }
        let card = settings.showEditCard ? app.editCard : nil
        card?.show(image: image, scale: scale, copied: settings.copyToClipboard)
        let url = SnapNaming.nextFreeURL(root: app.capturesDirectory, date: Date())
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            do {
                try PNG.write(image, to: url)
                DispatchQueue.main.async {
                    card?.setSaved(url, for: image)
                    self?.app.capturesChanged()
                    self?.app.countSnap()
                    Log.info("saved \(url.lastPathComponent) \(image.width)x\(image.height)")
                }
            } catch {
                DispatchQueue.main.async { self?.app.report(error) }
            }
        }
    }
}

// MARK: - Edit card

/// The small card in the corner after a capture: L.t("COPIED!"), with an Edit button. It never
/// takes focus, so you can paste right away, and it fades out on its own.
final class EditCard: NSPanel {
    var onEdit: ((URL?, CGImage, CGFloat) -> Void)?
    var onOpen: ((URL) -> Void)?

    private let thumb = NSImageView()
    private let titleLabel = NSTextField(labelWithString: L.t("COPIED!"))
    private let detail = NSTextField(labelWithString: "")
    private let editButton: SpaceButton
    private var image: CGImage?
    private var scale: CGFloat = 2
    private var url: URL?
    private var fileOnly = false
    private var hideTimer: Timer?
    private weak var root: HoverView?

    init() {
        editButton = SpaceButton(title: L.t("Edit"), symbol: "pencil", fill: Theme.terracotta, tip: L.t("Open in the editor"))
        super.init(contentRect: CGRect(x: 0, y: 0, width: 340, height: 84), styleMask: [.borderless, .nonactivatingPanel],
                   backing: .buffered, defer: false)
        isFloatingPanel = true
        level = .statusBar
        backgroundColor = .clear
        isOpaque = false
        hasShadow = true
        hidesOnDeactivate = false
        becomesKeyOnlyIfNeeded = true
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle]
        isReleasedWhenClosed = false

        let root = HoverView()
        root.wantsLayer = true
        root.layer?.backgroundColor = Theme.night.withAlphaComponent(0.96).cgColor
        root.layer?.cornerRadius = 14
        root.layer?.borderWidth = 1
        root.layer?.borderColor = Theme.line.cgColor
        root.onHover = { [weak self] inside in self?.hover(inside) }
        // The whole card opens the editor: faster to hit than the button alone.
        root.onClick = { [weak self] in self?.edit() }
        root.toolTip = L.t("Click to edit · Drag into any app")
        // …or drag it straight into any app.
        root.onDrag = { [weak self] event in self?.drag(event) }
        self.root = root

        thumb.imageScaling = .scaleProportionallyUpOrDown
        thumb.wantsLayer = true
        thumb.layer?.cornerRadius = 6
        thumb.layer?.masksToBounds = true
        thumb.layer?.borderWidth = 1
        thumb.layer?.borderColor = Theme.line.cgColor
        thumb.layer?.backgroundColor = NSColor.black.cgColor

        titleLabel.font = Theme.heading(15)
        titleLabel.textColor = Theme.yellow
        detail.font = Theme.ui(11)
        detail.textColor = Theme.muted
        detail.lineBreakMode = .byTruncatingMiddle

        editButton.onClick = { [weak self] in self?.edit() }
        let close = SpaceButton(title: "", symbol: "xmark", tip: L.t("Dismiss"))
        close.layer?.borderWidth = 0
        close.onClick = { [weak self] in self?.fadeOut() }

        for v in [thumb, titleLabel, detail, editButton, close] as [NSView] {
            v.translatesAutoresizingMaskIntoConstraints = false
            root.addSubview(v)
        }
        NSLayoutConstraint.activate([
            thumb.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 12),
            thumb.centerYAnchor.constraint(equalTo: root.centerYAnchor),
            thumb.widthAnchor.constraint(equalToConstant: 88),
            thumb.heightAnchor.constraint(equalToConstant: 56),
            titleLabel.leadingAnchor.constraint(equalTo: thumb.trailingAnchor, constant: 12),
            titleLabel.topAnchor.constraint(equalTo: root.topAnchor, constant: 20),
            detail.leadingAnchor.constraint(equalTo: titleLabel.leadingAnchor),
            detail.topAnchor.constraint(equalTo: titleLabel.bottomAnchor, constant: 3),
            detail.trailingAnchor.constraint(lessThanOrEqualTo: editButton.leadingAnchor, constant: -8),
            editButton.trailingAnchor.constraint(equalTo: close.leadingAnchor, constant: -4),
            editButton.centerYAnchor.constraint(equalTo: root.centerYAnchor),
            editButton.widthAnchor.constraint(equalToConstant: 74),
            editButton.heightAnchor.constraint(equalToConstant: 34),
            close.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -6),
            close.topAnchor.constraint(equalTo: root.topAnchor, constant: 6),
            close.widthAnchor.constraint(equalToConstant: 24),
            close.heightAnchor.constraint(equalToConstant: 24),
        ])
        contentView = root
    }

    override var canBecomeKey: Bool { false }

    func show(image: CGImage, scale: CGFloat, copied: Bool) {
        self.image = image
        self.scale = scale
        self.url = nil
        fileOnly = false
        thumb.image = NSImage(cgImage: image, size: NSSize(width: image.width, height: image.height))
        titleLabel.stringValue = copied ? L.t("COPIED!") : L.t("SNAPPED!")
        detail.stringValue = "\(image.width) × \(image.height) · " + L.t("saving…")
        root?.toolTip = L.t("Click to edit · Drag into any app")
        present()
    }

    /// For recordings: shows the file with an Open button.
    func show(recording url: URL, thumbnail: CGImage?) {
        image = thumbnail
        self.url = url
        fileOnly = true
        thumb.image = thumbnail.map { NSImage(cgImage: $0, size: NSSize(width: $0.width, height: $0.height)) }
            ?? NSImage(systemSymbolName: "film", accessibilityDescription: nil)
        titleLabel.stringValue = url.pathExtension.lowercased() == "gif" ? L.t("GIF SAVED!") : L.t("VIDEO SAVED!")
        detail.stringValue = url.lastPathComponent + " · " + L.t("copied")
        root?.toolTip = L.t("Click to play")
        present()
    }

    func setSaved(_ url: URL, for image: CGImage) {
        guard image === self.image, isVisible else { return }
        self.url = url
        detail.stringValue = "\(image.width) × \(image.height) · \(url.lastPathComponent)"
    }

    private func present() {
        let screen = NSScreen.underMouse
        let vf = screen.visibleFrame
        let f = CGRect(x: vf.maxX - frame.width - 14, y: vf.minY + 14, width: frame.width, height: frame.height)
        setFrame(f.offsetBy(dx: 0, dy: -10), display: false)
        alphaValue = 0
        orderFrontRegardless()
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = 0.18
            ctx.timingFunction = CAMediaTimingFunction(name: .easeOut)
            self.animator().alphaValue = 1
            self.animator().setFrame(f, display: true)
        }
        restartTimer()
    }

    private func restartTimer() {
        hideTimer?.invalidate()
        hideTimer = Timer.scheduledTimer(withTimeInterval: 4.5, repeats: false) { [weak self] _ in self?.fadeOut() }
    }

    private func hover(_ inside: Bool) {
        root?.layer?.borderColor = (inside ? Theme.blue : Theme.line).cgColor
        root?.layer?.borderWidth = inside ? 2 : 1
        if inside {
            hideTimer?.invalidate()
            animator().alphaValue = 1
        } else {
            restartTimer()
        }
    }

    private func edit() {
        hideTimer?.invalidate()
        orderOut(nil)
        if fileOnly, let url { onOpen?(url); return }
        if let image { onEdit?(url, image, scale) }
    }

    private func drag(_ event: NSEvent) {
        guard let root, image != nil || url != nil else { return }
        hideTimer?.invalidate()
        let started = DragOut.start(from: root, event: event, url: url, image: fileOnly ? nil : image, preview: thumb.image,
                                    frame: root.convert(thumb.bounds, from: thumb)) { [weak self] op in
            if op.isEmpty { self?.restartTimer() } else { self?.fadeOut() }
        }
        if !started { restartTimer() }
    }

    func fadeOut() {
        hideTimer?.invalidate()
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = 0.22
            self.animator().alphaValue = 0
        }, completionHandler: { [weak self] in
            if let self, self.alphaValue < 0.01 { self.orderOut(nil) }
        })
    }
}

/// Reports mouse enter / exit (keeps the card on screen while hovered).
final class HoverView: NSView {
    var onHover: ((Bool) -> Void)?
    var onClick: (() -> Void)?
    /// Called once the mouse has moved a few points with the button down.
    var onDrag: ((NSEvent) -> Void)?
    private var downAt: CGPoint?
    private var dragged = false

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    /// Labels and the thumbnail count as the card; only real buttons keep their own clicks.
    override func hitTest(_ point: NSPoint) -> NSView? {
        let hit = super.hitTest(point)
        var v = hit
        while let cur = v, cur !== self {
            if cur is SpaceButton { return hit }
            v = cur.superview
        }
        return hit == nil ? nil : self
    }
    override func resetCursorRects() { if onClick != nil { addCursorRect(bounds, cursor: .pointingHand) } }
    override func mouseDown(with event: NSEvent) {
        downAt = event.locationInWindow
        dragged = false
    }
    override func mouseDragged(with event: NSEvent) {
        guard let d = downAt, let onDrag else { return }
        let p = event.locationInWindow
        guard hypot(p.x - d.x, p.y - d.y) > 4 else { return }
        downAt = nil
        dragged = true
        onDrag(event)
    }
    override func mouseUp(with event: NSEvent) {
        defer { downAt = nil; dragged = false }
        if !dragged, bounds.contains(convert(event.locationInWindow, from: nil)) { onClick?() }
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .activeAlways, .inVisibleRect], owner: self))
    }

    override func mouseEntered(with event: NSEvent) { onHover?(true) }
    override func mouseExited(with event: NSEvent) { onHover?(false) }
}
