import AppKit
import CometsnapCore
import UniformTypeIdentifiers

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

    /// The overlay, for something else than a screenshot (scrolling capture, copy text).
    func pickRegion(purpose: CaptureOverlay.Purpose, _ completion: @escaping (OverlayResult?) -> Void) {
        openOverlay(purpose: purpose, completion)
    }

    /// Copy text: drag over some text (or click a window) and its text goes on the clipboard.
    func startTextCapture() {
        openOverlay(purpose: .text) { [weak self] result in
            guard let self, let result, let image = result.image else { return }
            self.app.copyText(image, scale: result.frozen.scale, from: result.rect)
        }
    }

    /// Color picker: the loupe opens on the frozen screen, a click copies the color as #RRGGBB.
    func startColorPicker() {
        openOverlay(purpose: .color, onColor: { color, text in
            ColorToast.shared.show(color: color.ns, text: text)
        }) { _ in }
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

    private func openOverlay(purpose: CaptureOverlay.Purpose, onColor: ((PixelProbe.Color, String) -> Void)? = nil,
                             _ completion: @escaping (OverlayResult?) -> Void) {
        guard !isBusy else { return }
        guard ensurePermission() else { return }
        let front = NSWorkspace.shared.frontmostApplication
        previousApp = front?.processIdentifier == ProcessInfo.processInfo.processIdentifier ? nil : front
        app.hidePanels()
        let windows = purpose == .color ? [] : ScreenGrabber.windows()
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
                    if let result, result.scrollDelta != nil {
                        // Wheel with the button held: the page under the area scrolls, Cometsnap glues it.
                        self.previousApp = nil
                        self.app.scrolling.runHeld(result, forText: purpose == .text)
                        return
                    }
                    self.restoreFocus()
                    completion(result)
                }
                o.onColor = onColor
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
    func deliver(_ raw: CGImage, scale: CGFloat, from rect: CGRect) {
        let settings = app.settings
        app.lastRawCapture = (raw, scale)
        // Share-ready style: the copy and the saved file can each be raw or styled.
        var styledCache: CGImage?
        func styled() -> CGImage {
            if let s = styledCache { return s }
            let s = SnapStyler.apply(raw, settings: settings, scale: scale) ?? raw
            styledCache = s
            return s
        }
        let copyImage = settings.styleOnCopy ? styled() : raw
        let image = settings.styleOnExport ? styled() : raw
        if settings.copyToClipboard { Clipboard.copy(copyImage, scale: scale) }
        if settings.playSound { Sound.snap() }
        if settings.cometAnimation { CometFlight.fly(from: rect, to: app.statusItem.buttonFrame, image: image) }
        let card = settings.showEditCard ? app.editCard : nil
        let saving = settings.askWhereToSave || settings.saveEveryCapture
        // ✨ has nothing to add when both the copy and the file are styled already.
        let canStyle = settings.stylePerShot && !(settings.styleOnCopy && settings.styleOnExport)
        card?.show(image: saving ? image : copyImage, scale: scale, copied: settings.copyToClipboard, raw: raw, canStyle: canStyle)
        if settings.askWhereToSave {
            DispatchQueue.main.async { [weak self] in self?.askAndSave(image, scale: scale, card: card) }
            return
        }
        guard settings.saveEveryCapture else {
            app.countSnap()
            if settings.openEditorAfterCapture { app.openEditor(url: nil, image: image, scale: scale) }
            return
        }
        let url = app.nextCaptureURL()
        let edit = settings.openEditorAfterCapture
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            do {
                try PNG.writeImage(image, to: url)
                DispatchQueue.main.async {
                    card?.setSaved(url, for: image)
                    self?.app.capturesChanged()
                    self?.app.countSnap()
                    Log.info("saved \(url.lastPathComponent) \(image.width)x\(image.height)")
                    if edit { self?.app.openEditor(url: url) }
                }
            } catch {
                DispatchQueue.main.async {
                    self?.app.report(error)
                    if edit { self?.app.openEditor(url: nil, image: image, scale: scale) }
                }
            }
        }
    }

    private var lastSaveFolder: URL?

    /// "Ask where to save each time": a Save panel right after the clipboard copy.
    private func askAndSave(_ image: CGImage, scale: CGFloat, card: EditCard?) {
        let s = app.settings
        let ext = SnapNaming.fileExtension(for: s.imageFormat)
        let panel = NSSavePanel()
        panel.title = L.t("Save capture")
        panel.allowedContentTypes = ext == "jpg" ? [.jpeg, .png] : [.png, .jpeg]
        panel.canCreateDirectories = true
        panel.directoryURL = lastSaveFolder ?? app.capturesDirectory
        panel.nameFieldStringValue = SnapNaming.suggestedName(pattern: s.fileNamePattern, date: Date(), counter: app.nextNameCounter) + "." + ext
        NSApp.activate()
        panel.begin { [weak self] resp in
            guard let self else { return }
            guard resp == .OK, let url = panel.url else {
                self.app.countSnap()
                if s.openEditorAfterCapture { self.app.openEditor(url: nil, image: image, scale: scale) }
                return
            }
            do {
                try PNG.writeImage(image, to: url)
                self.lastSaveFolder = url.deletingLastPathComponent()
                _ = self.app.takeNameCounter()
                card?.setSaved(url, for: image)
                self.app.capturesChanged()
                self.app.countSnap()
                if s.openEditorAfterCapture { self.app.openEditor(url: url) }
            } catch {
                self.app.report(error)
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
    /// The T button: read the text in the capture.
    var onText: ((CGImage, CGFloat) -> Void)?
    /// A click on the card of a text capture: show the text.
    var onShowText: ((String) -> Void)?
    /// The ✨ button: style this capture (saved file if known, raw capture, scale).
    var onStyle: ((URL?, CGImage, CGFloat) -> Void)?
    private var recognized: String?
    private let textButton: SpaceButton
    private let styleButton: SpaceButton
    private var raw: CGImage?
    private var canStyle = false

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
        textButton = SpaceButton(title: "", symbol: "text.viewfinder", fill: Theme.surface, tip: L.t("Copy the text in this capture"))
        styleButton = SpaceButton(title: "", symbol: "sparkles", fill: Theme.surface, tip: L.t("Style this capture: rounded corners, shadow, backdrop"))
        super.init(contentRect: CGRect(x: 0, y: 0, width: 380, height: 84), styleMask: [.borderless, .nonactivatingPanel],
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
        titleLabel.textColor = CaptureLook.tag
        detail.font = Theme.ui(11)
        detail.textColor = Theme.muted
        detail.lineBreakMode = .byTruncatingMiddle

        editButton.onClick = { [weak self] in self?.edit() }
        textButton.onClick = { [weak self] in
            guard let self, let image = self.image, !self.fileOnly else { return }
            self.hideTimer?.invalidate()
            self.onText?(image, self.scale)
        }
        styleButton.onClick = { [weak self] in
            guard let self, let raw = self.raw, !self.fileOnly, self.recognized == nil else { return }
            self.hideTimer?.invalidate()
            self.styleButton.isHidden = true
            self.titleLabel.stringValue = L.t("STYLING…")
            self.onStyle?(self.url, raw, self.scale)
        }
        let close = SpaceButton(title: "", symbol: "xmark", tip: L.t("Dismiss"))
        close.layer?.borderWidth = 0
        close.onClick = { [weak self] in self?.fadeOut() }

        for v in [thumb, titleLabel, detail, styleButton, textButton, editButton, close] as [NSView] {
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
            detail.trailingAnchor.constraint(lessThanOrEqualTo: styleButton.leadingAnchor, constant: -8),
            titleLabel.trailingAnchor.constraint(lessThanOrEqualTo: styleButton.leadingAnchor, constant: -8),
            styleButton.trailingAnchor.constraint(equalTo: textButton.leadingAnchor, constant: -6),
            styleButton.centerYAnchor.constraint(equalTo: root.centerYAnchor),
            styleButton.widthAnchor.constraint(equalToConstant: 34),
            styleButton.heightAnchor.constraint(equalToConstant: 34),
            textButton.trailingAnchor.constraint(equalTo: editButton.leadingAnchor, constant: -6),
            textButton.centerYAnchor.constraint(equalTo: root.centerYAnchor),
            textButton.widthAnchor.constraint(equalToConstant: 34),
            textButton.heightAnchor.constraint(equalToConstant: 34),
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

    func show(image: CGImage, scale: CGFloat, copied: Bool, raw: CGImage? = nil, canStyle: Bool = false) {
        self.image = image
        self.scale = scale
        self.url = nil
        self.raw = raw ?? image
        self.canStyle = canStyle
        fileOnly = false
        recognized = nil
        textButton.isHidden = false
        styleButton.isHidden = !canStyle
        editButton.isHidden = false
        thumb.image = NSImage(cgImage: image, size: NSSize(width: image.width, height: image.height))
        titleLabel.stringValue = copied ? L.t("COPIED!") : L.t("SNAPPED!")
        detail.stringValue = "\(image.width) × \(image.height) · " + L.t("saving…")
        root?.toolTip = L.t("Click to edit · Drag into any app")
        editButton.update(title: L.t("Edit"), symbol: "pencil")
        editButton.toolTip = L.t("Open in the editor")
        present()
    }

    /// For recordings: shows the file with an Open button.
    func show(recording url: URL, thumbnail: CGImage?) {
        image = thumbnail
        self.url = url
        fileOnly = true
        recognized = nil
        textButton.isHidden = true
        styleButton.isHidden = true
        editButton.isHidden = false
        thumb.image = thumbnail.map { NSImage(cgImage: $0, size: NSSize(width: $0.width, height: $0.height)) }
            ?? NSImage(systemSymbolName: "film", accessibilityDescription: nil)
        titleLabel.stringValue = url.pathExtension.lowercased() == "gif" ? L.t("GIF SAVED!") : L.t("VIDEO SAVED!")
        detail.stringValue = url.lastPathComponent + " · " + L.t("copied")
        root?.toolTip = L.t("Click to trim · Drag into any app")
        editButton.update(title: L.t("Trim"), symbol: "scissors")
        editButton.toolTip = L.t("Trim the start and end, or cut parts out")
        present()
    }

    /// The text of a capture is being read.
    func showReading(image: CGImage, scale: CGFloat) {
        self.image = image
        self.scale = scale
        url = nil
        fileOnly = false
        recognized = ""
        textButton.isHidden = true
        styleButton.isHidden = true
        editButton.isHidden = true
        thumb.image = NSImage(cgImage: image, size: NSSize(width: image.width, height: image.height))
        titleLabel.stringValue = L.t("READING…")
        detail.stringValue = L.t("Looking for text")
        root?.toolTip = nil
        if !isVisible { present() }
        hideTimer?.invalidate()
    }

    /// Shows what text recognition found (already on the clipboard).
    func showText(_ text: String, lines: Int) {
        recognized = text
        textButton.isHidden = true
        styleButton.isHidden = true
        editButton.isHidden = text.isEmpty
        editButton.update(title: L.t("Show"), symbol: "eye")
        editButton.toolTip = L.t("See the text and copy just a part")
        root?.toolTip = text.isEmpty ? nil : L.t("Click to see the text")
        let firstLine = text.split(separator: "\n").first.map { String($0) } ?? ""
        titleLabel.stringValue = text.isEmpty ? L.t("NO TEXT FOUND") : L.t("TEXT COPIED!")
        detail.stringValue = text.isEmpty ? L.t("Try a bigger area") : lines == 1 ? firstLine : L.f("{0} lines · {1}", lines, firstLine)
        if !isVisible { present() } else { restartTimer() }
    }

    /// The ✨ button finished: the card now holds the styled capture (already on the clipboard).
    func showStyled(raw: CGImage, styled: CGImage, url: URL?, copied: Bool) {
        guard raw === self.raw, isVisible, recognized == nil, !fileOnly else { return }
        image = styled
        self.url = url
        thumb.image = NSImage(cgImage: styled, size: NSSize(width: styled.width, height: styled.height))
        titleLabel.stringValue = copied ? L.t("STYLED & COPIED!") : L.t("STYLED!")
        detail.stringValue = "\(styled.width) × \(styled.height)" + (url.map { " · " + $0.lastPathComponent } ?? "")
        restartTimer()
    }

    func setSaved(_ url: URL, for image: CGImage) {
        guard image === self.image, isVisible, recognized == nil else { return }
        self.url = url
        detail.stringValue = "\(image.width) × \(image.height) · \(url.lastPathComponent)"
    }

    private func present() {
        titleLabel.textColor = CaptureLook.tag
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
        root?.layer?.borderColor = (inside ? CaptureLook.accent : Theme.line).cgColor
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
        if let recognized {
            if !recognized.isEmpty { onShowText?(recognized) }
            return
        }
        if fileOnly, let url { onOpen?(url); return }
        if let image { onEdit?(url, image, scale) }
    }

    private func drag(_ event: NSEvent) {
        guard let root, image != nil || url != nil, recognized == nil else { return }
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
