import AppKit
import CometsnapCore

/// The Style studio: builds the share-ready look piece by piece (corners, border, drop shadow,
/// laser/neon glow, backdrop) with a big live preview. Every change applies and saves at once.
final class StyleStudioWindow: NSWindow {
    private unowned let app: AppController
    private var style: SnapStyle
    private let previewView = NSImageView()
    private let controls = NSStackView()
    private var version = 0
    private var pending: DispatchWorkItem?
    private var onLastCapture = false
    private let sourceChooser = NSSegmentedControl()
    /// Called after each (debounced) change, so Settings can refresh its small preview.
    var onChange: (() -> Void)?

    init(app: AppController) {
        self.app = app
        style = app.settings.snapStyle
        super.init(contentRect: CGRect(x: 0, y: 0, width: 1040, height: 720), styleMask: [.titled, .closable, .resizable, .miniaturizable],
                   backing: .buffered, defer: false)
        title = L.t("Style studio")
        appearance = NSAppearance(named: .darkAqua)
        backgroundColor = Theme.night
        isReleasedWhenClosed = false
        minSize = NSSize(width: 760, height: 520)
        center()
        build()
        render()
    }

    // MARK: layout

    private func build() {
        let heading = NSTextField(labelWithString: L.t("Style studio").uppercased())
        heading.font = Theme.display(24)
        heading.textColor = Theme.yellow
        let intro = Self.hint(L.t("Mix corners, border, shadow, glow and backdrop. Sizes are in pixels of the capture. Every change is saved right away."), width: 520)

        previewView.imageScaling = .scaleProportionallyDown
        previewView.translatesAutoresizingMaskIntoConstraints = false
        let frame = CheckerView()
        frame.translatesAutoresizingMaskIntoConstraints = false
        frame.addSubview(previewView)
        NSLayoutConstraint.activate([
            previewView.leadingAnchor.constraint(equalTo: frame.leadingAnchor, constant: 20),
            previewView.trailingAnchor.constraint(equalTo: frame.trailingAnchor, constant: -20),
            previewView.topAnchor.constraint(equalTo: frame.topAnchor, constant: 20),
            previewView.bottomAnchor.constraint(equalTo: frame.bottomAnchor, constant: -20),
        ])

        let previewOn = NSTextField(labelWithString: L.t("Preview on"))
        previewOn.font = Theme.ui(12)
        previewOn.textColor = Theme.muted
        sourceChooser.segmentCount = app.lastRawCapture == nil ? 1 : 2
        sourceChooser.setLabel(L.t("Sample"), forSegment: 0)
        if sourceChooser.segmentCount > 1 { sourceChooser.setLabel(L.t("Your last capture"), forSegment: 1) }
        sourceChooser.selectedSegment = 0
        sourceChooser.target = self
        sourceChooser.action = #selector(sourceChanged)
        let under = NSStackView(views: [previewOn, sourceChooser])
        under.spacing = 10

        let left = NSStackView(views: [heading, intro, frame, under])
        left.orientation = .vertical
        left.alignment = .leading
        left.spacing = 10
        left.edgeInsets = NSEdgeInsets(top: 22, left: 22, bottom: 22, right: 22)
        left.translatesAutoresizingMaskIntoConstraints = false
        frame.widthAnchor.constraint(equalTo: left.widthAnchor, constant: -44).isActive = true
        frame.setContentHuggingPriority(.defaultLow, for: .vertical)
        frame.setContentCompressionResistancePriority(.defaultLow, for: .vertical)

        controls.orientation = .vertical
        controls.alignment = .leading
        controls.spacing = 8
        controls.edgeInsets = NSEdgeInsets(top: 18, left: 20, bottom: 24, right: 20)
        controls.translatesAutoresizingMaskIntoConstraints = false
        let scroll = NSScrollView()
        scroll.drawsBackground = true
        scroll.backgroundColor = Theme.surface
        scroll.hasVerticalScroller = true
        scroll.translatesAutoresizingMaskIntoConstraints = false
        let doc = FlippedView()
        doc.translatesAutoresizingMaskIntoConstraints = false
        doc.addSubview(controls)
        scroll.documentView = doc
        NSLayoutConstraint.activate([
            controls.leadingAnchor.constraint(equalTo: doc.leadingAnchor),
            controls.trailingAnchor.constraint(equalTo: doc.trailingAnchor),
            controls.topAnchor.constraint(equalTo: doc.topAnchor),
            controls.bottomAnchor.constraint(equalTo: doc.bottomAnchor),
            doc.widthAnchor.constraint(equalTo: scroll.contentView.widthAnchor),
        ])

        let root = NSView()
        root.addSubview(left)
        root.addSubview(scroll)
        NSLayoutConstraint.activate([
            left.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            left.topAnchor.constraint(equalTo: root.topAnchor),
            left.bottomAnchor.constraint(equalTo: root.bottomAnchor),
            left.trailingAnchor.constraint(equalTo: scroll.leadingAnchor),
            scroll.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            scroll.topAnchor.constraint(equalTo: root.topAnchor),
            scroll.bottomAnchor.constraint(equalTo: root.bottomAnchor),
            scroll.widthAnchor.constraint(equalToConstant: 420),
        ])
        contentView = root
        buildControls()
    }

    /// English names of the presets, written out so the translation check sees them.
    static func presetName(_ id: String) -> String {
        switch id {
        case "clean": return L.t("Clean")
        case "showcase": return L.t("Showcase")
        case "frosted": return L.t("Frosted")
        case "neon": return L.t("Neon")
        case "laser": return L.t("Laser")
        case "comet": return L.t("Comet")
        case "polaroid": return L.t("Polaroid")
        default: return id
        }
    }

    private static func backdropName(_ b: StyleBackdrop) -> String {
        switch b {
        case .none: return L.t("None")
        case .color: return L.t("Color")
        case .gradient: return L.t("Gradient")
        case .blur: return L.t("Blurred capture")
        case .space: return L.t("Deep space")
        }
    }

    private func buildControls() {
        controls.arrangedSubviews.forEach { $0.removeFromSuperview() }
        targets.removeAll()

        add(section(L.t("Start from")))
        add(Self.hint(L.t("A starting point. Then change anything below.")))
        let presets = NSPopUpButton(frame: .zero, pullsDown: true)
        presets.addItem(withTitle: L.t("Start from") + "…")
        SnapStylePresets.all.forEach { presets.addItem(withTitle: Self.presetName($0.id)) }
        presets.target = self
        presets.action = #selector(presetChosen(_:))
        add(presets)

        add(section(L.t("Corners")))
        add(slider(L.t("Radius"), 0, 80, style.cornerRadius) { $0.cornerRadius = $1 })
        add(Self.hint(L.t("0 = square corners.")))

        let border = group(L.t("Border"), style.border) { $0.border = $1 }
        border.addArrangedSubview(slider(L.t("Width"), 1, 40, style.borderWidth) { $0.borderWidth = $1 })
        border.addArrangedSubview(colorRow(L.t("Color"), style.borderColor) { $0.borderColor = $1 })

        let shadow = group(L.t("Drop shadow"), style.shadow) { $0.shadow = $1 }
        shadow.addArrangedSubview(slider(L.t("Left / right"), -80, 80, style.shadowX) { $0.shadowX = $1 })
        shadow.addArrangedSubview(slider(L.t("Up / down"), -80, 80, style.shadowY) { $0.shadowY = $1 })
        shadow.addArrangedSubview(slider(L.t("Blur"), 0, 120, style.shadowBlur) { $0.shadowBlur = $1 })
        shadow.addArrangedSubview(slider(L.t("Opacity"), 0, 100, style.shadowOpacity, unit: "%") { $0.shadowOpacity = $1 })
        shadow.addArrangedSubview(colorRow(L.t("Color"), style.shadowColor) { $0.shadowColor = $1 })

        let glow = group(L.t("Glow (laser or neon)"), style.glow) { $0.glow = $1 }
        glow.addArrangedSubview(Self.hint(L.t("Small = a sharp laser line. Big = a soft neon halo.")))
        glow.addArrangedSubview(slider(L.t("Size"), 1, 120, style.glowSize) { $0.glowSize = $1 })
        glow.addArrangedSubview(slider(L.t("Strength"), 0, 100, style.glowStrength, unit: "%") { $0.glowStrength = $1 })
        glow.addArrangedSubview(colorRow(L.t("Color"), style.glowColor) { $0.glowColor = $1 })

        add(section(L.t("Backdrop")))
        let kinds = StyleBackdrop.allCases
        let chooser = NSSegmentedControl(labels: kinds.map(Self.backdropName), trackingMode: .selectOne, target: self, action: #selector(backdropChanged(_:)))
        chooser.selectedSegment = kinds.firstIndex(of: style.backdrop) ?? 0
        add(chooser)
        let seeThrough = Self.hint(L.t("See-through: just the capture with its shadow and glow (PNG keeps it)."))
        let padding = slider(L.t("Space around"), 0, 240, style.padding) { $0.padding = $1 }
        let first = colorRow(L.t("First color"), style.backdropColor) { $0.backdropColor = $1 }
        let second = colorRow(L.t("Second color"), style.backdropColor2) { $0.backdropColor2 = $1 }
        [seeThrough, padding, first, second].forEach(add)
        backdropViews = (seeThrough, padding, first, second)
        updateBackdropViews()
    }

    private var backdropViews: (NSView, NSView, NSView, NSView)?

    private func updateBackdropViews() {
        guard let views = backdropViews else { return }
        let (seeThrough, padding, first, second) = views
        let b = style.backdrop
        seeThrough.isHidden = b != .none
        padding.isHidden = b == .none
        first.isHidden = !(b == .color || b == .gradient || b == .space)
        second.isHidden = !(b == .gradient || b == .space)
    }

    private func add(_ v: NSView) { controls.addArrangedSubview(v) }

    private func section(_ text: String) -> NSView {
        let t = NSTextField(labelWithString: text.uppercased())
        t.font = Theme.heading(13)
        t.textColor = Theme.muted
        let wrap = NSStackView(views: [t])
        wrap.edgeInsets = NSEdgeInsets(top: 12, left: 0, bottom: 0, right: 0)
        return wrap
    }

    static func hint(_ text: String, width: CGFloat = 370) -> NSTextField {
        let h = NSTextField(wrappingLabelWithString: text)
        h.font = Theme.ui(11)
        h.textColor = Theme.muted
        h.preferredMaxLayoutWidth = width
        return h
    }

    /// A section whose title is an on/off checkbox; the returned stack holds its settings, dimmed when off.
    private func group(_ title: String, _ on: Bool, _ set: @escaping (inout SnapStyle, Bool) -> Void) -> NSStackView {
        let body = NSStackView()
        body.orientation = .vertical
        body.alignment = .leading
        body.spacing = 8
        let box = ActionCheckbox(title: title.uppercased()) { [weak self, weak body] value in
            guard let self else { return }
            set(&self.style, value)
            if let body { Self.enable(body, value) }
            self.changed()
        }
        box.font = Theme.heading(13)
        box.state = on ? .on : .off
        let wrap = NSStackView(views: [box])
        wrap.edgeInsets = NSEdgeInsets(top: 12, left: 0, bottom: 0, right: 0)
        add(wrap)
        add(body)
        DispatchQueue.main.async { Self.enable(body, on) }
        return body
    }

    private static func enable(_ view: NSView, _ on: Bool) {
        view.alphaValue = on ? 1 : 0.45
        for v in view.subviews {
            if let c = v as? NSControl { c.isEnabled = on }
            enable(v, on)
            v.alphaValue = 1
        }
    }

    /// A labelled slider with a number box: drag, or type any value in range.
    private func slider(_ title: String, _ lo: Int, _ hi: Int, _ value: Int, unit: String = "px",
                        _ set: @escaping (inout SnapStyle, Int) -> Void) -> NSView {
        let label = NSTextField(labelWithString: title)
        label.font = Theme.ui(12)
        label.textColor = .white
        label.lineBreakMode = .byTruncatingTail
        label.widthAnchor.constraint(equalToConstant: 104).isActive = true
        let s = NSSlider(value: Double(value), minValue: Double(lo), maxValue: Double(hi), target: nil, action: nil)
        s.isContinuous = true
        s.widthAnchor.constraint(equalToConstant: 170).isActive = true
        let field = NSTextField(string: "\(value)")
        field.alignment = .right
        field.font = Theme.ui(12)
        field.widthAnchor.constraint(equalToConstant: 48).isActive = true
        let formatter = NumberFormatter()
        formatter.minimum = NSNumber(value: lo)
        formatter.maximum = NSNumber(value: hi)
        formatter.allowsFloats = false
        field.formatter = formatter
        let u = NSTextField(labelWithString: unit)
        u.font = Theme.ui(11)
        u.textColor = Theme.muted
        let apply: (Int) -> Void = { [weak self] n in
            guard let self else { return }
            set(&self.style, min(hi, max(lo, n)))
            self.changed()
        }
        let sliderTarget = BlockTarget { [weak s, weak field] in
            guard let s, let field else { return }
            let n = Int(s.doubleValue.rounded())
            field.stringValue = "\(n)"
            apply(n)
        }
        let fieldTarget = BlockTarget { [weak s, weak field] in
            guard let s, let field, let n = Int(field.stringValue) else { return }
            let v = min(hi, max(lo, n))
            s.doubleValue = Double(v)
            field.stringValue = "\(v)"
            apply(v)
        }
        s.target = sliderTarget; s.action = #selector(BlockTarget.fire)
        field.target = fieldTarget; field.action = #selector(BlockTarget.fire)
        let row = NSStackView(views: [label, s, field, u])
        row.spacing = 8
        targets += [sliderTarget, fieldTarget]
        return row
    }

    /// Keeps the closure targets of the controls alive (AppKit holds targets weakly).
    private var targets: [BlockTarget] = []

    /// A color: the macOS color well (every color there is), a hex box and quick palette dots.
    private func colorRow(_ title: String, _ value: String, _ set: @escaping (inout SnapStyle, String) -> Void) -> NSView {
        let label = NSTextField(labelWithString: title)
        label.font = Theme.ui(12)
        label.textColor = .white
        label.widthAnchor.constraint(equalToConstant: 104).isActive = true
        let well = NSColorWell()
        well.widthAnchor.constraint(equalToConstant: 44).isActive = true
        well.heightAnchor.constraint(equalToConstant: 24).isActive = true
        well.color = NSColor(hex: StyleColors.parse(value, 0xFFFFFF))
        well.toolTip = L.t("Any color")
        let field = NSTextField(string: StyleColors.normalize(value, "#FFFFFF"))
        field.font = NSFont.monospacedSystemFont(ofSize: 12, weight: .regular)
        field.widthAnchor.constraint(equalToConstant: 80).isActive = true

        let apply: (String) -> Void = { [weak self, weak well, weak field] text in
            guard let self, let rgb = StyleColors.tryParse(text) else { return }
            let hex = StyleColors.hex(rgb)
            field?.stringValue = hex
            well?.color = NSColor(hex: rgb)
            set(&self.style, hex)
            self.changed()
        }
        let wellTarget = BlockTarget { [weak well] in
            guard let c = well?.color.usingColorSpace(.sRGB) else { return }
            func b(_ v: CGFloat) -> UInt32 { UInt32(min(255, max(0, (v * 255).rounded()))) }
            apply(StyleColors.hex(b(c.redComponent) << 16 | b(c.greenComponent) << 8 | b(c.blueComponent)))
        }
        let fieldTarget = BlockTarget { [weak field] in
            guard let field else { return }
            if StyleColors.tryParse(field.stringValue) != nil { apply(field.stringValue) }
        }
        well.target = wellTarget; well.action = #selector(BlockTarget.fire)
        field.target = fieldTarget; field.action = #selector(BlockTarget.fire)

        var dotTargets: [BlockTarget] = []
        let dots: [NSView] = StyleColors.palette.map { hex in
            let t = BlockTarget { apply(hex) }
            dotTargets.append(t)
            let b = NSButton(image: Self.dot(NSColor(hex: StyleColors.parse(hex, 0))), target: t, action: #selector(BlockTarget.fire))
            b.isBordered = false
            b.toolTip = hex
            return b
        }
        let top = NSStackView(views: [label, well, field])
        top.spacing = 8
        let indent = NSView()
        indent.widthAnchor.constraint(equalToConstant: 104).isActive = true
        let palette = NSStackView(views: [indent] + dots)
        palette.spacing = 5
        let stack = NSStackView(views: [top, palette])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 6
        targets += [wellTarget, fieldTarget] + dotTargets
        return stack
    }

    private static func dot(_ color: NSColor) -> NSImage {
        NSImage(size: NSSize(width: 18, height: 18), flipped: false) { r in
            color.setFill()
            let p = NSBezierPath(ovalIn: r.insetBy(dx: 1, dy: 1))
            p.fill()
            NSColor(white: 1, alpha: 0.35).setStroke()
            p.lineWidth = 1
            p.stroke()
            return true
        }
    }

    // MARK: actions

    @objc private func presetChosen(_ sender: NSPopUpButton) {
        let i = sender.indexOfSelectedItem - 1
        guard i >= 0, i < SnapStylePresets.all.count else { return }
        style = SnapStylePresets.get(SnapStylePresets.all[i].id)
        buildControls()
        changed()
    }

    @objc private func backdropChanged(_ sender: NSSegmentedControl) {
        let kinds = StyleBackdrop.allCases
        guard sender.selectedSegment >= 0, sender.selectedSegment < kinds.count else { return }
        style.backdrop = kinds[sender.selectedSegment]
        updateBackdropViews()
        changed()
    }

    @objc private func sourceChanged() {
        onLastCapture = sourceChooser.selectedSegment == 1
        render()
    }

    /// Debounced: re-render and save a moment after the last change.
    private func changed() {
        pending?.cancel()
        let work = DispatchWorkItem { [weak self] in
            guard let self else { return }
            let style = self.style
            self.app.update { $0.snapStyle = style }
            self.render()
            self.onChange?()
        }
        pending = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.045, execute: work)
    }

    override func close() {
        if let p = pending, !p.isCancelled { p.perform(); p.cancel() }
        super.close()
    }

    /// Renders off the main thread; only the newest render is shown.
    private func render() {
        version += 1
        let v = version
        let style = self.style
        let last = onLastCapture ? app.lastRawCapture : nil
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            var image: NSImage?
            if let last {
                let (raw, scale) = last
                // Big captures are shrunk for the preview; the style shrinks with them.
                let shrink = min(1, 1100 / Double(max(raw.width, raw.height)))
                let src = shrink < 1 ? SnapStyler.scaled(raw, by: shrink) ?? raw : raw
                if let cg = SnapStyler.apply(src, style: style, scale: Double(scale) * (shrink < 1 ? shrink : 1)) {
                    image = NSImage(cgImage: cg, size: NSSize(width: cg.width, height: cg.height))
                }
            } else {
                image = SnapStyler.preview(style: style)
            }
            DispatchQueue.main.async {
                guard let self, v == self.version else { return }
                self.previewView.image = image
            }
        }
    }
}

/// A checkerboard behind the preview, so see-through corners and shadows show as such.
private final class CheckerView: NSView {
    override func draw(_ dirtyRect: NSRect) {
        let path = NSBezierPath(roundedRect: bounds, xRadius: 12, yRadius: 12)
        path.addClip()
        Theme.surface.setFill()
        bounds.fill()
        NSColor(white: 1, alpha: 0.08).setFill()
        let cell: CGFloat = 8
        var y: CGFloat = 0
        var row = 0
        while y < bounds.height {
            var x: CGFloat = row % 2 == 0 ? 0 : cell
            while x < bounds.width {
                NSRect(x: x, y: y, width: cell, height: cell).fill()
                x += cell * 2
            }
            y += cell
            row += 1
        }
        Theme.line.setStroke()
        path.lineWidth = 1
        path.stroke()
    }
}

/// Lets a closure be an AppKit target/action.
final class BlockTarget: NSObject {
    private let block: () -> Void
    init(_ block: @escaping () -> Void) { self.block = block }
    @objc func fire() { block() }
}

/// A checkbox that calls a closure with its new state.
private final class ActionCheckbox: NSButton {
    private var handler: ((Bool) -> Void)?

    convenience init(title: String, handler: @escaping (Bool) -> Void) {
        self.init(checkboxWithTitle: title, target: nil, action: nil)
        self.handler = handler
        target = self
        action = #selector(toggled)
    }

    @objc private func toggled() { handler?(state == .on) }
}
