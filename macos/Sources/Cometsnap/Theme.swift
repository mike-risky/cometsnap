import AppKit
import CoreText

/// The "Electric" palette and the Bungee fonts (see assets/BRAND.md).
enum Theme {
    static let night = NSColor(hex: 0x07113A)
    static let surface = NSColor(hex: 0x0F1D57)
    static let line = NSColor(hex: 0x22348A)
    static let blue = NSColor(hex: 0x3D6BFF)
    static let terracotta = NSColor(hex: 0xE2553A)
    static let flame = NSColor(hex: 0xF09625)
    static let yellow = NSColor(hex: 0xFFD60A)
    static let rock = NSColor(hex: 0x5E4C4A)
    static let text = NSColor.white
    static let muted = NSColor(hex: 0xA9B6F0)
    static let recordRed = NSColor(hex: 0xE5484D)
    static let hover = NSColor(hex: 0x1A2A70)

    private static var fontsRegistered = false

    /// Registers the bundled Bungee fonts for this process (once).
    static func registerFonts() {
        guard !fontsRegistered else { return }
        fontsRegistered = true
        guard let dir = Bundle.main.resourceURL?.appendingPathComponent("Fonts"),
              let files = try? FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil) else { return }
        for f in files where f.pathExtension.lowercased() == "ttf" {
            CTFontManagerRegisterFontsForURL(f as CFURL, .process, nil)
        }
    }

    /// Bungee Shade, for big titles only (28 pt and up).
    static func display(_ size: CGFloat) -> NSFont {
        NSFont(name: "BungeeShade-Regular", size: size) ?? NSFont.systemFont(ofSize: size, weight: .black)
    }

    /// Bungee, the solid sibling, for small headings and labels.
    static func heading(_ size: CGFloat) -> NSFont {
        NSFont(name: "Bungee-Regular", size: size) ?? NSFont.systemFont(ofSize: size, weight: .heavy)
    }

    static func ui(_ size: CGFloat, _ weight: NSFont.Weight = .regular) -> NSFont {
        NSFont.systemFont(ofSize: size, weight: weight)
    }

    /// "COMET" in white and "SNAP" in yellow.
    static func wordmark(_ size: CGFloat, shade: Bool = true) -> NSAttributedString {
        let font = shade ? display(size) : heading(size)
        let s = NSMutableAttributedString(string: "COMET", attributes: [.font: font, .foregroundColor: NSColor.white])
        s.append(NSAttributedString(string: "SNAP", attributes: [.font: font, .foregroundColor: yellow]))
        return s
    }

    static var logo: NSImage {
        if let img = Bundle.main.image(forResource: "cometsnap-256") { return img }
        return NSImage(systemSymbolName: "sparkles", accessibilityDescription: "Cometsnap") ?? NSImage()
    }

    /// Menu bar icon, 18 pt, from the hand-tuned 20/40 px PNGs.
    static var menuBarIcon: NSImage {
        let img = NSImage(size: NSSize(width: 18, height: 18))
        for name in ["cometsnap-20", "cometsnap-40"] {
            if let url = Bundle.main.url(forResource: name, withExtension: "png"), let rep = NSImageRep(contentsOf: url) {
                rep.size = NSSize(width: 18, height: 18)
                img.addRepresentation(rep)
            }
        }
        if img.representations.isEmpty {
            return NSImage(systemSymbolName: "camera.viewfinder", accessibilityDescription: "Cometsnap") ?? img
        }
        return img
    }

    /// The menu bar icon with a red dot at the bottom right (while recording).
    static func recordingIcon(dotVisible: Bool) -> NSImage {
        let base = menuBarIcon
        let img = NSImage(size: NSSize(width: 18, height: 18), flipped: false) { rect in
            base.draw(in: rect)
            if dotVisible {
                let dot = NSRect(x: rect.maxX - 8, y: 0, width: 8, height: 8)
                NSColor.white.setFill()
                NSBezierPath(ovalIn: dot.insetBy(dx: -1, dy: -1)).fill()
                recordRed.setFill()
                NSBezierPath(ovalIn: dot).fill()
            }
            return true
        }
        return img
    }
}

extension NSColor {
    convenience init(hex: UInt32, alpha: CGFloat = 1) {
        self.init(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255, green: CGFloat((hex >> 8) & 0xFF) / 255,
                  blue: CGFloat(hex & 0xFF) / 255, alpha: alpha)
    }
}

/// A flat rounded button in the Cometsnap style: icon + label, hover tint, tiny press bounce.
final class SpaceButton: NSView {
    var onClick: (() -> Void)?
    var onRightClick: ((NSEvent) -> Void)?
    private let fill: NSColor?
    private let stack = NSStackView()
    private var hovering = false { didSet { updateColors() } }
    var active = false { didSet { updateColors() } }

    init(title: String, symbol: String?, fill: NSColor? = nil, vertical: Bool = false, fontSize: CGFloat = 13, tip: String? = nil) {
        self.fill = fill
        super.init(frame: .zero)
        wantsLayer = true
        layer?.cornerRadius = 10
        layer?.borderWidth = fill == nil ? 1 : 0
        layer?.borderColor = Theme.line.cgColor
        toolTip = tip
        stack.orientation = vertical ? .vertical : .horizontal
        stack.spacing = vertical ? 5 : 7
        stack.alignment = .centerY
        if vertical { stack.alignment = .centerX }
        if let symbol, let img = NSImage(systemSymbolName: symbol, accessibilityDescription: title) {
            let conf = NSImage.SymbolConfiguration(pointSize: vertical ? 20 : 14, weight: .semibold)
            let iv = NSImageView(image: img.withSymbolConfiguration(conf) ?? img)
            iv.contentTintColor = .white
            stack.addArrangedSubview(iv)
        }
        if !title.isEmpty {
            let label = NSTextField(labelWithString: title)
            label.font = Theme.ui(fontSize, fill == nil ? .medium : .semibold)
            label.textColor = .white
            label.alignment = .center
            stack.addArrangedSubview(label)
        }
        stack.translatesAutoresizingMaskIntoConstraints = false
        addSubview(stack)
        NSLayoutConstraint.activate([
            stack.centerXAnchor.constraint(equalTo: centerXAnchor),
            stack.centerYAnchor.constraint(equalTo: centerYAnchor),
            stack.leadingAnchor.constraint(greaterThanOrEqualTo: leadingAnchor, constant: 10),
            stack.trailingAnchor.constraint(lessThanOrEqualTo: trailingAnchor, constant: -10),
        ])
        updateColors()
    }

    required init?(coder: NSCoder) { fatalError() }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .activeAlways, .inVisibleRect], owner: self))
    }

    override func mouseEntered(with event: NSEvent) { hovering = true }
    override func mouseExited(with event: NSEvent) { hovering = false }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func resetCursorRects() { addCursorRect(bounds, cursor: .pointingHand) }

    override func mouseDown(with event: NSEvent) { bounce() }

    override func mouseUp(with event: NSEvent) {
        let p = convert(event.locationInWindow, from: nil)
        if bounds.contains(p) { onClick?() }
    }

    override func rightMouseDown(with event: NSEvent) {
        if let onRightClick { onRightClick(event) } else { super.rightMouseDown(with: event) }
    }

    private func updateColors() {
        let base: NSColor
        if active { base = Theme.blue }
        else if let fill { base = hovering ? fill.blended(withFraction: 0.12, of: .white) ?? fill : fill }
        else { base = hovering ? Theme.hover : .clear }
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = 0.12
            ctx.allowsImplicitAnimation = true
            self.layer?.backgroundColor = base.cgColor
        }
    }

    private func bounce() {
        guard let layer else { return }
        // A quick dip in opacity: reads as a press and never fights AppKit's layer geometry.
        let anim = CAKeyframeAnimation(keyPath: "opacity")
        anim.values = [1, 0.65, 1]
        anim.keyTimes = [0, 0.35, 1]
        anim.duration = 0.18
        layer.add(anim, forKey: "bounce")
    }
}

/// Plain clickable text link.
final class LinkLabel: NSTextField {
    var onClick: (() -> Void)?

    convenience init(_ title: String, action: @escaping () -> Void) {
        self.init(labelWithString: title)
        font = Theme.ui(12, .medium)
        textColor = Theme.muted
        onClick = action
    }

    override func resetCursorRects() { addCursorRect(bounds, cursor: .pointingHand) }
    override func mouseDown(with event: NSEvent) {}
    override func mouseUp(with event: NSEvent) { onClick?() }
    override func mouseEntered(with event: NSEvent) { textColor = .white }
    override func mouseExited(with event: NSEvent) { textColor = Theme.muted }
    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .activeAlways, .inVisibleRect], owner: self))
    }
}
