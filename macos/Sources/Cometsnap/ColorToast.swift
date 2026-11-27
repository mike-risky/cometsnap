import AppKit
import CometsnapCore

/// "COPIED! #FFAA00" with a swatch, bottom-right, after the color picker. Click-through, fades by itself.
final class ColorToast: NSPanel {
    static let shared = ColorToast()

    private let swatch = NSView()
    private let titleLabel = NSTextField(labelWithString: "")
    private let valueLabel = NSTextField(labelWithString: "")
    private let root = NSView()
    private var hideTimer: Timer?

    private init() {
        super.init(contentRect: CGRect(x: 0, y: 0, width: 240, height: 64), styleMask: [.borderless, .nonactivatingPanel],
                   backing: .buffered, defer: false)
        isFloatingPanel = true
        level = .statusBar
        backgroundColor = .clear
        isOpaque = false
        hasShadow = true
        ignoresMouseEvents = true
        isReleasedWhenClosed = false
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle]

        root.wantsLayer = true
        root.layer?.backgroundColor = Theme.night.withAlphaComponent(0.96).cgColor
        root.layer?.cornerRadius = 14
        root.layer?.borderWidth = 1.5
        swatch.wantsLayer = true
        swatch.layer?.cornerRadius = 8
        swatch.layer?.borderWidth = 1.5
        swatch.layer?.borderColor = NSColor.white.withAlphaComponent(0.5).cgColor
        titleLabel.font = Theme.heading(14)
        valueLabel.font = NSFont.monospacedSystemFont(ofSize: 15, weight: .semibold)
        valueLabel.textColor = .white
        for v in [swatch, titleLabel, valueLabel] as [NSView] {
            v.translatesAutoresizingMaskIntoConstraints = false
            root.addSubview(v)
        }
        NSLayoutConstraint.activate([
            swatch.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 12),
            swatch.centerYAnchor.constraint(equalTo: root.centerYAnchor),
            swatch.widthAnchor.constraint(equalToConstant: 40),
            swatch.heightAnchor.constraint(equalToConstant: 40),
            titleLabel.leadingAnchor.constraint(equalTo: swatch.trailingAnchor, constant: 12),
            titleLabel.topAnchor.constraint(equalTo: root.topAnchor, constant: 12),
            valueLabel.leadingAnchor.constraint(equalTo: titleLabel.leadingAnchor),
            valueLabel.topAnchor.constraint(equalTo: titleLabel.bottomAnchor, constant: 2),
            valueLabel.trailingAnchor.constraint(lessThanOrEqualTo: root.trailingAnchor, constant: -14),
        ])
        contentView = root
    }

    override var canBecomeKey: Bool { false }

    func show(color: NSColor, text: String) {
        swatch.layer?.backgroundColor = color.cgColor
        root.layer?.borderColor = CaptureLook.accent.cgColor
        titleLabel.textColor = CaptureLook.tag
        titleLabel.stringValue = L.t("COPIED!")
        valueLabel.stringValue = text
        let width = max(200, ceil(valueLabel.intrinsicContentSize.width) + 90)
        let vf = NSScreen.underMouse.visibleFrame
        let f = CGRect(x: vf.maxX - width - 14, y: vf.minY + 14, width: width, height: 64)
        setFrame(f.offsetBy(dx: 0, dy: -10), display: false)
        alphaValue = 0
        orderFrontRegardless()
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = 0.18
            self.animator().alphaValue = 1
            self.animator().setFrame(f, display: true)
        }
        hideTimer?.invalidate()
        hideTimer = Timer.scheduledTimer(withTimeInterval: 2.2, repeats: false) { [weak self] _ in
            guard let self else { return }
            NSAnimationContext.runAnimationGroup({ ctx in
                ctx.duration = 0.22
                self.animator().alphaValue = 0
            }, completionHandler: { [weak self] in
                if (self?.alphaValue ?? 1) < 0.05 { self?.orderOut(nil) }
            })
        }
    }
}
