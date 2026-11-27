import AppKit
import CometsnapCore
import ScreenCaptureKit

/// Scrolling capture, the natural way: start an area selection, keep the mouse button held and
/// scroll. The overlay steps aside, the page scrolls under the area as usual, and every new row
/// that comes into view is glued under the previous ones. Letting go finishes; Esc cancels.
/// Scrolling too fast (or back up) is fine: the page is picked up again once it lines up.
final class ScrollingCapture {
    static let maxHeight = 30000
    static let minSize = CGSize(width: 30, height: 40)
    private unowned let app: AppController
    private var pill: ScrollingPill?
    private(set) var isRunning = false

    init(app: AppController) { self.app = app }

    /// Hotkey / menu: the usual overlay, with a reminder of how it works (hold the button and scroll).
    func start() {
        guard !isRunning else { return }
        app.capture.pickRegion(purpose: .scrolling) { [weak self] result in
            // Let go without scrolling: an ordinary screenshot.
            guard let self, let result, let image = result.image else { return }
            self.app.capture.deliver(image, scale: result.frozen.scale, from: result.rect)
        }
    }

    /// The wheel turned while the button was held over `picked.rect` in the overlay.
    func runHeld(_ picked: OverlayResult, forText: Bool) {
        guard !isRunning else { return }
        Task { @MainActor in await self.run(picked, forText: forText) }
    }

    @MainActor
    private func run(_ picked: OverlayResult, forText: Bool) async {
        let screen = picked.frozen.screen
        let scale = picked.frozen.scale
        let rect = picked.rect.integral
        isRunning = true
        defer {
            pill?.orderOut(nil)
            isRunning = false
        }
        do {
            let content = try await ScreenGrabber.shared.content()
            guard let display = ScreenGrabber.shared.display(for: screen, in: content) else { return }
            let own = ScreenGrabber.shared.ownApplication(in: content)
            let source = screen.displayRect(fromGlobal: rect)
            func grab() async throws -> CGImage {
                try await ScreenGrabber.shot(display: display, excluding: own, scale: scale, rect: source)
            }

            if pill == nil { pill = ScrollingPill() }
            pill?.show(above: rect, on: screen)

            let image = try await grab()
            let w = image.width, h = image.height
            let stitcher = ScrollStitcher(width: w, frameHeight: h)
            var frame = Self.pixels(image, width: w, height: h)
            _ = stitcher.add(frame)
            // The overlay took the first turn of the wheel: pass it on (only allowed with Accessibility).
            if let delta = picked.scrollDelta { Self.repost(delta) }

            var lost = false
            var cancelled = false
            while true {
                try await Task.sleep(nanoseconds: 30_000_000)
                if CGEventSource.keyState(.combinedSessionState, key: 0x35 /* Esc */) {
                    cancelled = true
                    break
                }
                let released = NSEvent.pressedMouseButtons & 1 == 0
                let next: [UInt8]
                if released {
                    // Let go: wait for the last turn of the wheel to finish scrolling.
                    next = try await settled(grab, previous: frame, width: w, height: h).1
                } else {
                    next = Self.pixels(try await grab(), width: w, height: h)
                }
                if next != frame {
                    frame = next
                    switch stitcher.add(next) {
                    case .appended:
                        lost = false
                        pill?.show(height: Int(CGFloat(stitcher.height) / scale), lost: false)
                    case .lost where !lost:
                        lost = true
                        pill?.show(height: 0, lost: true)
                    default: break
                    }
                }
                if released || stitcher.height >= Self.maxHeight { break }
            }
            pill?.orderOut(nil)
            if cancelled { return }

            var (pixels, height) = stitcher.compose()
            height = min(height, Self.maxHeight)
            pixels = Array(pixels.prefix(w * 4 * height))
            guard let tall = Self.image(pixels, width: w, height: height) else { return }
            Log.info("scrolling capture \(w)x\(height) header \(stitcher.header) footer \(stitcher.footer)")
            if forText { app.copyText(tall, scale: scale, from: rect) }
            else { app.capture.deliver(tall, scale: scale, from: rect) }
        } catch {
            app.report(error)
        }
    }

    /// Grabs until two grabs in a row are the same (smooth scrolling has finished), at most ~0.6 s.
    private func settled(_ grab: () async throws -> CGImage, previous: [UInt8], width: Int, height: Int) async throws -> (CGImage, [UInt8]) {
        var image = try await grab()
        var current = Self.pixels(image, width: width, height: height)
        var waited = 0
        while waited < 600 {
            try await Task.sleep(nanoseconds: 60_000_000)
            waited += 60
            let nextImage = try await grab()
            let next = Self.pixels(nextImage, width: width, height: height)
            let same = next == current
            image = nextImage
            current = next
            if same { break }
        }
        return (image, current)
    }

    /// Posts the wheel turn the overlay swallowed, when macOS lets Cometsnap post events.
    private static func repost(_ delta: OverlayResult.ScrollDelta) {
        guard CGPreflightPostEventAccess() else { return }
        let event = delta.precise
            ? CGEvent(scrollWheelEvent2Source: nil, units: .pixel, wheelCount: 1, wheel1: Int32(delta.y.rounded()), wheel2: 0, wheel3: 0)
            : CGEvent(scrollWheelEvent2Source: nil, units: .line, wheelCount: 1, wheel1: Int32(delta.y.rounded(.awayFromZero)), wheel2: 0, wheel3: 0)
        event?.post(tap: .cghidEventTap)
    }

    /// RGBA pixels of an image, redrawn at exactly width × height.
    private static func pixels(_ image: CGImage, width: Int, height: Int) -> [UInt8] {
        var buffer = [UInt8](repeating: 0, count: width * height * 4)
        buffer.withUnsafeMutableBytes { raw in
            guard let ctx = CGContext(data: raw.baseAddress, width: width, height: height, bitsPerComponent: 8, bytesPerRow: width * 4,
                                      space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                      bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return }
            ctx.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))
        }
        return buffer
    }

    private static func image(_ pixels: [UInt8], width: Int, height: Int) -> CGImage? {
        guard let provider = CGDataProvider(data: Data(pixels) as CFData) else { return nil }
        return CGImage(width: width, height: height, bitsPerComponent: 8, bitsPerPixel: 32, bytesPerRow: width * 4,
                       space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedLast.rawValue),
                       provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent)
    }
}

/// The little pill shown while a scrolling capture runs (Cometsnap's own windows are left out of the frames).
final class ScrollingPill: NSPanel {
    private let heightLabel = NSTextField(labelWithString: "")
    private let title = NSTextField(labelWithString: "")

    init() {
        super.init(contentRect: CGRect(x: 0, y: 0, width: 330, height: 34), styleMask: [.borderless, .nonactivatingPanel],
                   backing: .buffered, defer: false)
        isFloatingPanel = true
        level = .statusBar
        backgroundColor = .clear
        isOpaque = false
        ignoresMouseEvents = true
        isReleasedWhenClosed = false
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle]

        let root = NSView()
        root.wantsLayer = true
        root.layer?.backgroundColor = Theme.night.withAlphaComponent(0.94).cgColor
        root.layer?.cornerRadius = 17
        root.layer?.borderWidth = 1
        root.layer?.borderColor = Theme.line.cgColor
        title.stringValue = L.t("Scroll, then let go")
        title.font = Theme.ui(13, .semibold)
        title.textColor = .white
        heightLabel.font = Theme.ui(12)
        heightLabel.textColor = Theme.yellow
        let hint = NSTextField(labelWithString: L.t("Esc cancels"))
        hint.font = Theme.ui(11)
        hint.textColor = Theme.muted
        let stack = NSStackView(views: [title, heightLabel, NSView(), hint])
        stack.spacing = 8
        stack.edgeInsets = NSEdgeInsets(top: 0, left: 16, bottom: 0, right: 16)
        stack.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            stack.topAnchor.constraint(equalTo: root.topAnchor),
            stack.bottomAnchor.constraint(equalTo: root.bottomAnchor),
        ])
        contentView = root
    }

    func show(above rect: CGRect, on screen: NSScreen) {
        show(height: 0, lost: false)
        let vf = screen.visibleFrame
        let x = min(max(rect.midX - frame.width / 2, vf.minX), vf.maxX - frame.width)
        var y = rect.maxY + 8
        if y + frame.height > vf.maxY { y = rect.minY - 8 - frame.height }
        if y < vf.minY { y = min(rect.maxY, vf.maxY) - 8 - frame.height }
        setFrameOrigin(CGPoint(x: x, y: y))
        orderFrontRegardless()
    }

    /// The height so far, or a warning when the page went by too fast to follow.
    func show(height points: Int, lost: Bool) {
        heightLabel.stringValue = lost || points == 0 ? "" : L.f("{0} px", points)
        title.stringValue = lost ? L.t("Too fast: scroll back a little") : L.t("Scroll, then let go")
        title.textColor = lost ? Theme.yellow : .white
    }
}
