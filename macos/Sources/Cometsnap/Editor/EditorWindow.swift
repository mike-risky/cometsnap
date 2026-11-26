import AppKit
import CometsnapCore
import UniformTypeIdentifiers

/// The editor: 13 tools with single-key shortcuts, colors, thickness, undo/redo, and shapes that
/// stay editable (saved in a sidecar). Copy and Save both close it, so it never lingers.
final class EditorWindow: NSWindow, NSWindowDelegate {
    private unowned let app: AppController
    private(set) var url: URL?
    private let base: CGImage
    private let imageScale: CGFloat
    let canvas: CanvasView
    private let scroll = NSScrollView()
    private var toolButtons: [EditorTool: SpaceButton] = [:]
    private var swatches: [SwatchView] = []
    private let widthSlider = NSSlider(value: 4, minValue: 1, maxValue: 12, target: nil, action: nil)
    private let zoomLabel = NSTextField(labelWithString: "100%")
    private var savedDoc: AnnotationDocument
    private let constellation = SpaceButton(title: "Constellation", symbol: "sparkles", tip: "Join the numbered steps with faint star lines")

    init(app: AppController, url: URL?, image: CGImage?, scale: CGFloat?) {
        self.app = app
        self.url = url
        var doc = AnnotationDocument()
        var baseImage = image
        if let url {
            // Re-open with shapes still editable: original pixels + saved shapes.
            let original = EditSidecar.original(for: url)
            if let data = try? Data(contentsOf: EditSidecar.annotations(for: url)), let d = AnnotationDocument.decode(data),
               let o = PNG.load(original) {
                doc = d
                baseImage = o
            } else if baseImage == nil {
                baseImage = PNG.load(url)
            }
        }
        if doc.annotations.isEmpty { doc.connectSteps = app.settings.constellationSteps }
        base = baseImage ?? CGImage.placeholder
        imageScale = scale ?? (NSScreen.main?.backingScaleFactor ?? 2)
        savedDoc = doc
        canvas = CanvasView(base: base, doc: doc, imageScale: imageScale)

        let vf = NSScreen.underMouse.visibleFrame
        let imgW = CGFloat(base.width) / imageScale, imgH = CGFloat(base.height) / imageScale
        let w = min(vf.width * 0.9, max(760, imgW + 120)), h = min(vf.height * 0.9, max(520, imgH + 150))
        let frame = CGRect(x: vf.midX - w / 2, y: vf.midY - h / 2, width: w, height: h)
        super.init(contentRect: frame, styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView],
                   backing: .buffered, defer: false)
        title = url?.lastPathComponent ?? "Cometsnap"
        titlebarAppearsTransparent = true
        appearance = NSAppearance(named: .darkAqua)
        backgroundColor = Theme.night
        isReleasedWhenClosed = false
        minSize = NSSize(width: 640, height: 420)
        delegate = self
        build()
        canvas.onChange = { [weak self] in self?.documentChanged() }
        canvas.onSelection = { [weak self] a in self?.selectionChanged(a) }
        select(.arrow)
        setColor(RGBA.palette[0])
        updateConstellation()
    }

    private func build() {
        let root = NSView()
        root.wantsLayer = true
        root.layer?.backgroundColor = Theme.night.cgColor
        contentView = root

        // Top bar: undo/redo, zoom, then Save as, Save, Copy on the right.
        let undo = SpaceButton(title: "", symbol: "arrow.uturn.backward", tip: "Undo (⌘Z)")
        undo.onClick = { [weak self] in self?.canvas.undo() }
        let redo = SpaceButton(title: "", symbol: "arrow.uturn.forward", tip: "Redo (⇧⌘Z)")
        redo.onClick = { [weak self] in self?.canvas.redo() }
        let fit = SpaceButton(title: "Fit", symbol: "arrow.up.left.and.arrow.down.right", tip: "Fit to window (⌘0)")
        fit.onClick = { [weak self] in self?.zoomToFit() }
        let actual = SpaceButton(title: "1:1", symbol: nil, tip: "Actual size (⌘1)")
        actual.onClick = { [weak self] in self?.setZoom(1) }
        zoomLabel.textColor = Theme.muted
        zoomLabel.font = NSFont.monospacedDigitSystemFont(ofSize: 11, weight: .medium)
        let saveAs = SpaceButton(title: "Save as…", symbol: "square.and.arrow.down.on.square", tip: "Save a copy somewhere else (⇧⌘S)")
        saveAs.onClick = { [weak self] in self?.saveAs() }
        let save = SpaceButton(title: "Save", symbol: "square.and.arrow.down", fill: Theme.surface, tip: "Save and close (⌘S)")
        save.layer?.borderWidth = 1
        save.onClick = { [weak self] in self?.save(close: true) }
        let copy = SpaceButton(title: "Copy", symbol: "doc.on.doc", fill: Theme.terracotta, tip: "Copy, save and close (⌘C or ⌘↩)")
        copy.onClick = { [weak self] in self?.performCopyAndClose() }

        constellation.onClick = { [weak self] in self?.toggleConstellation() }
        constellation.widthAnchor.constraint(equalToConstant: 128).isActive = true
        constellation.heightAnchor.constraint(equalToConstant: 32).isActive = true
        let left = NSStackView(views: [undo, redo, fit, actual, zoomLabel, constellation])
        left.spacing = 6
        let right = NSStackView(views: [saveAs, save, copy])
        right.spacing = 8
        for b in [undo, redo] { b.widthAnchor.constraint(equalToConstant: 36).isActive = true }
        for b in [undo, redo, fit, actual, saveAs, save, copy] { b.heightAnchor.constraint(equalToConstant: 32).isActive = true }
        fit.widthAnchor.constraint(equalToConstant: 64).isActive = true
        actual.widthAnchor.constraint(equalToConstant: 44).isActive = true
        saveAs.widthAnchor.constraint(equalToConstant: 108).isActive = true
        save.widthAnchor.constraint(equalToConstant: 78).isActive = true
        copy.widthAnchor.constraint(equalToConstant: 84).isActive = true

        // Tool palette (left).
        let tools = NSStackView()
        tools.orientation = .vertical
        tools.spacing = 3
        for t in EditorTool.allCases {
            let b = SpaceButton(title: t.label, symbol: t.symbol, vertical: true, fontSize: 9, tip: t.tip)
            b.layer?.borderWidth = 0
            b.onClick = { [weak self] in self?.select(t) }
            b.widthAnchor.constraint(equalToConstant: 58).isActive = true
            b.heightAnchor.constraint(equalToConstant: 44).isActive = true
            tools.addArrangedSubview(b)
            toolButtons[t] = b
        }
        let toolsScroll = NSScrollView()
        toolsScroll.drawsBackground = false
        toolsScroll.hasVerticalScroller = false
        let toolsDoc = FlippedView()
        toolsDoc.addSubview(tools)
        tools.translatesAutoresizingMaskIntoConstraints = false
        toolsScroll.documentView = toolsDoc
        toolsDoc.translatesAutoresizingMaskIntoConstraints = false

        // Bottom bar: colors and thickness.
        let colors = NSStackView()
        colors.spacing = 6
        for (i, c) in RGBA.palette.enumerated() {
            let s = SwatchView(color: c, tip: "\(RGBA.paletteNames[i]) (\(i + 1))")
            s.onClick = { [weak self] in self?.setColor(c) }
            s.widthAnchor.constraint(equalToConstant: 22).isActive = true
            s.heightAnchor.constraint(equalToConstant: 22).isActive = true
            colors.addArrangedSubview(s)
            swatches.append(s)
        }
        let custom = SpaceButton(title: "", symbol: "eyedropper", tip: "Pick any color")
        custom.layer?.borderWidth = 0
        custom.onClick = { [weak self] in self?.pickCustomColor() }
        custom.widthAnchor.constraint(equalToConstant: 28).isActive = true
        custom.heightAnchor.constraint(equalToConstant: 26).isActive = true
        colors.addArrangedSubview(custom)
        let thickness = NSTextField(labelWithString: "Thickness")
        thickness.textColor = Theme.muted
        thickness.font = Theme.ui(11)
        widthSlider.target = self
        widthSlider.action = #selector(widthChanged)
        widthSlider.widthAnchor.constraint(equalToConstant: 140).isActive = true
        let hint = NSTextField(labelWithString: "Keys: V A L R E T K H P N B S C · 1–8 colors · ⌫ delete")
        hint.textColor = Theme.muted
        hint.font = Theme.ui(11)
        hint.lineBreakMode = .byTruncatingTail
        hint.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        let bottom = NSStackView(views: [colors, thickness, widthSlider, hint])
        bottom.spacing = 14

        // Canvas.
        scroll.drawsBackground = true
        scroll.backgroundColor = NSColor(hex: 0x050C2B)
        scroll.hasVerticalScroller = true
        scroll.hasHorizontalScroller = true
        scroll.autohidesScrollers = true
        scroll.allowsMagnification = true
        scroll.minMagnification = 0.05
        scroll.maxMagnification = 8
        let clip = CenteringClipView()
        clip.drawsBackground = false
        scroll.contentView = clip
        scroll.documentView = canvas
        NotificationCenter.default.addObserver(forName: NSScrollView.didEndLiveMagnifyNotification, object: scroll, queue: .main) { [weak self] _ in
            self?.updateZoomLabel()
        }

        for v in [left, right, toolsScroll, bottom, scroll] as [NSView] {
            v.translatesAutoresizingMaskIntoConstraints = false
            root.addSubview(v)
        }
        NSLayoutConstraint.activate([
            left.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 84),
            left.topAnchor.constraint(equalTo: root.topAnchor, constant: 6),
            right.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -12),
            right.centerYAnchor.constraint(equalTo: left.centerYAnchor),

            toolsScroll.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 8),
            toolsScroll.topAnchor.constraint(equalTo: left.bottomAnchor, constant: 10),
            toolsScroll.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -8),
            toolsScroll.widthAnchor.constraint(equalToConstant: 60),
            toolsDoc.widthAnchor.constraint(equalToConstant: 60),
            tools.topAnchor.constraint(equalTo: toolsDoc.topAnchor),
            tools.leadingAnchor.constraint(equalTo: toolsDoc.leadingAnchor, constant: 1),
            toolsDoc.bottomAnchor.constraint(equalTo: tools.bottomAnchor),

            scroll.leadingAnchor.constraint(equalTo: toolsScroll.trailingAnchor, constant: 8),
            scroll.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -10),
            scroll.topAnchor.constraint(equalTo: left.bottomAnchor, constant: 10),
            scroll.bottomAnchor.constraint(equalTo: bottom.topAnchor, constant: -10),

            bottom.leadingAnchor.constraint(equalTo: scroll.leadingAnchor),
            bottom.trailingAnchor.constraint(lessThanOrEqualTo: root.trailingAnchor, constant: -12),
            bottom.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -10),
            bottom.heightAnchor.constraint(equalToConstant: 28),
        ])
    }

    func present() {
        NSApp.activate()
        makeKeyAndOrderFront(nil)
        DispatchQueue.main.async { [weak self] in self?.zoomToFit() }
        makeFirstResponder(canvas)
    }

    // MARK: tools and style

    func select(_ t: EditorTool) {
        canvas.commitText()
        canvas.tool = t
        for (tool, b) in toolButtons { b.active = tool == t }
    }

    func setColor(_ c: RGBA) {
        canvas.color = c
        for s in swatches { s.selected = s.color == c }
        canvas.applyStyleToSelection()
    }

    @objc private func widthChanged() {
        canvas.strokeWidth = widthSlider.doubleValue
        canvas.applyStyleToSelection()
    }

    private func selectionChanged(_ a: Annotation?) {
        guard let a else { return }
        canvas.color = a.color
        for s in swatches { s.selected = s.color == a.color }
        widthSlider.doubleValue = a.width
        canvas.strokeWidth = a.width
    }

    private func pickCustomColor() {
        let panel = NSColorPanel.shared
        panel.setTarget(self)
        panel.setAction(#selector(colorPanelChanged(_:)))
        panel.showsAlpha = false
        panel.orderFront(nil)
    }

    @objc private func colorPanelChanged(_ sender: NSColorPanel) {
        guard let c = sender.color.usingColorSpace(.sRGB) else { return }
        setColor(RGBA(r: Double(c.redComponent), g: Double(c.greenComponent), b: Double(c.blueComponent)))
    }

    // MARK: zoom

    func zoomToFit() {
        let avail = scroll.contentSize
        let size = canvas.frame.size
        guard size.width > 0, size.height > 0 else { return }
        let m = min(1, min((avail.width - 24) / size.width, (avail.height - 24) / size.height))
        setZoom(max(0.05, m))
    }

    func setZoom(_ m: CGFloat) {
        scroll.magnification = m
        updateZoomLabel()
    }

    private func updateZoomLabel() {
        zoomLabel.stringValue = "\(Int((scroll.magnification * 100).rounded()))%"
    }

    // MARK: keys

    override func keyDown(with event: NSEvent) {
        if canvas.isEditingText { super.keyDown(with: event); return }
        let mods = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        let ch = event.charactersIgnoringModifiers?.lowercased() ?? ""
        if mods.contains(.command) {
            switch ch {
            case "z": mods.contains(.shift) ? canvas.redo() : canvas.undo()
            case "y": canvas.redo()
            case "c": performCopyAndClose()
            case "s": mods.contains(.shift) ? saveAs() : save(close: true)
            case "0": zoomToFit()
            case "1": setZoom(1)
            case "=", "+": setZoom(scroll.magnification * 1.25)
            case "-": setZoom(scroll.magnification / 1.25)
            case "w": performClose(nil)
            default:
                if event.keyCode == 36 { performCopyAndClose() } else { super.keyDown(with: event) }
            }
            return
        }
        switch event.keyCode {
        case 51, 117: canvas.deleteSelection(); return     // ⌫ / ⌦
        case 53: canvas.escape(); return                    // Esc
        default: break
        }
        if let c = ch.first, let t = EditorTool.forKey(c) { select(t); return }
        if let n = Int(ch), n >= 1, n <= RGBA.palette.count { setColor(RGBA.palette[n - 1]); return }
        super.keyDown(with: event)
    }

    // MARK: copy / save

    private var flattened: CGImage? { AnnotationRenderer.flatten(base: base, doc: canvas.doc) }

    func performCopyAndClose() {
        canvas.commitText()
        guard let img = flattened else { return }
        Clipboard.copy(img, scale: imageScale)
        save(close: true)
    }

    /// Writes the flattened PNG to the capture file, and the original + shapes beside it.
    func save(close: Bool) {
        canvas.commitText()
        guard let img = flattened else { return }
        let target = url ?? SnapNaming.nextFreeURL(root: app.capturesDirectory, date: Date())
        do {
            let original = EditSidecar.original(for: target)
            if !FileManager.default.fileExists(atPath: original.path) { try PNG.write(base, to: original) }
            if let data = canvas.doc.encoded() { try data.write(to: EditSidecar.annotations(for: target), options: .atomic) }
            try PNG.write(img, to: target)
            url = target
            savedDoc = canvas.doc
            app.capturesChanged()
            if close { self.close() }
        } catch {
            app.report(error)
        }
    }

    private func saveAs() {
        canvas.commitText()
        guard let img = flattened else { return }
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.png]
        panel.nameFieldStringValue = url?.lastPathComponent ?? "snap.png"
        panel.beginSheetModal(for: self) { [weak self] resp in
            guard resp == .OK, let dest = panel.url else { return }
            do { try PNG.write(img, to: dest) } catch { self?.app.report(error) }
        }
    }

    private func documentChanged() {
        isDocumentEdited = canvas.doc != savedDoc
        updateConstellation()
    }

    /// Constellation mode: shown once there are two steps to connect.
    private func updateConstellation() {
        constellation.isHidden = canvas.doc.orderedSteps.count < 2
        constellation.active = canvas.doc.connectSteps
    }

    private func toggleConstellation() {
        let on = !canvas.doc.connectSteps
        canvas.setConnectSteps(on)
        app.update { $0.constellationSteps = on }
        updateConstellation()
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        canvas.commitText()
        guard canvas.doc != savedDoc else { return true }
        let alert = NSAlert()
        alert.messageText = "Keep your changes?"
        alert.informativeText = "Your shapes stay editable: you can reopen this capture later and still move them."
        alert.addButton(withTitle: "Save")
        alert.addButton(withTitle: "Discard")
        alert.addButton(withTitle: "Cancel")
        alert.beginSheetModal(for: self) { [weak self] resp in
            guard let self else { return }
            switch resp {
            case .alertFirstButtonReturn: self.save(close: true)
            case .alertSecondButtonReturn: self.savedDoc = self.canvas.doc; self.close()
            default: break
            }
        }
        return false
    }

    func windowWillClose(_ notification: Notification) {
        if NSColorPanel.sharedColorPanelExists { NSColorPanel.shared.orderOut(nil) }
        app.editorClosed(self)
    }
}

/// Keeps a small canvas centred in the scroll view.
final class CenteringClipView: NSClipView {
    override func constrainBoundsRect(_ proposedBounds: NSRect) -> NSRect {
        var r = super.constrainBoundsRect(proposedBounds)
        guard let doc = documentView else { return r }
        let f = doc.frame
        if r.width > f.width { r.origin.x = (f.width - r.width) / 2 }
        if r.height > f.height { r.origin.y = (f.height - r.height) / 2 }
        return r
    }
}

final class SwatchView: NSView {
    let color: RGBA
    var onClick: (() -> Void)?
    var selected = false { didSet { needsDisplay = true } }

    init(color: RGBA, tip: String) {
        self.color = color
        super.init(frame: .zero)
        toolTip = tip
    }

    required init?(coder: NSCoder) { fatalError() }

    override func draw(_ dirtyRect: NSRect) {
        let r = bounds.insetBy(dx: 3, dy: 3)
        NSColor(cgColor: color.cgColor)?.setFill()
        NSBezierPath(ovalIn: r).fill()
        NSColor.white.withAlphaComponent(selected ? 1 : 0.25).setStroke()
        let ring = NSBezierPath(ovalIn: selected ? bounds.insetBy(dx: 1, dy: 1) : r)
        ring.lineWidth = selected ? 2 : 1
        ring.stroke()
    }

    override func mouseUp(with event: NSEvent) { onClick?() }
    override func resetCursorRects() { addCursorRect(bounds, cursor: .pointingHand) }
}

extension CGImage {
    static var placeholder: CGImage {
        let ctx = CGContext(data: nil, width: 4, height: 4, bitsPerComponent: 8, bytesPerRow: 0,
                            space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        ctx.setFillColor(NSColor.black.cgColor)
        ctx.fill(CGRect(x: 0, y: 0, width: 4, height: 4))
        return ctx.makeImage()!
    }
}
