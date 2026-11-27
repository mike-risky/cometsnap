import AppKit
import CometsnapCore
import ScreenCaptureKit

/// Scrolling capture: pick a window or an area and Cometsnap scrolls it, grabbing a frame after
/// every step and gluing them into one tall image. It stops by itself at the end of the page;
/// Esc or moving the mouse stops it earlier. Scrolling for you needs the Accessibility permission
/// (macOS asks for it once).
final class ScrollingCapture {
    static let maxHeight = 30000
    private unowned let app: AppController
    private var pill: ScrollingPill?
    private(set) var isRunning = false
    private var stopRequested = false

    init(app: AppController) { self.app = app }

    func start() {
        if isRunning { stopRequested = true; return }
        guard Self.canScroll() else { return }
        app.capture.pickRegion(purpose: .scrolling) { [weak self] result in
            guard let self, let result else { return }
            Task { @MainActor in await self.run(result) }
        }
    }

    /// Posting scroll events needs Accessibility. Asks once, then explains where to turn it on.
    private static func canScroll() -> Bool {
        if CGPreflightPostEventAccess() { return true }
        if CGRequestPostEventAccess() { return true }
        NSApp.activate()
        let a = NSAlert()
        a.icon = Theme.logo
        a.messageText = L.t("Allow Cometsnap to scroll for you")
        a.informativeText = L.t("Scrolling capture turns the scroll wheel for you. Turn on Cometsnap in System Settings › Privacy & Security › Accessibility, then try again.")
        a.addButton(withTitle: L.t("Open System Settings"))
        a.addButton(withTitle: L.t("Later"))
        if a.runModal() == .alertFirstButtonReturn,
           let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
            NSWorkspace.shared.open(url)
        }
        return false
    }

    @MainActor
    private func run(_ picked: OverlayResult) async {
        let screen = picked.frozen.screen
        let scale = picked.frozen.scale
        // Whole pixels, so every frame has exactly the same size.
        let rect = picked.rect.integral
        guard rect.height >= 40, rect.width >= 30 else {
            alert(L.t("Too small to scroll"), L.t("Pick a window or an area of at least 60 pixels high."))
            return
        }
        isRunning = true
        stopRequested = false
        let userMouse = NSEvent.mouseLocation
        defer {
            pill?.orderOut(nil)
            isRunning = false
            CGWarpMouseCursorPosition(Self.cgPoint(userMouse))
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
            try await Task.sleep(nanoseconds: 150_000_000)

            var image = try await grab()
            let w = image.width, h = image.height
            let stitcher = ScrollStitcher(width: w, frameHeight: h)
            var frame = Self.pixels(image, width: w, height: h)
            _ = stitcher.add(frame)

            let park = CGPoint(x: rect.midX, y: rect.midY)
            CGWarpMouseCursorPosition(Self.cgPoint(park))
            try await Task.sleep(nanoseconds: 60_000_000)

            var stepPoints = max(20, Int(rect.height / 2))
            var unchanged = 0
            for _ in 0..<400 {
                if shouldStop(park) { break }
                Self.scroll(byPoints: stepPoints)
                (image, frame) = try await settled(grab, previous: frame, width: w, height: h)
                if shouldStop(park) { break }
                switch stitcher.add(frame) {
                case .unchanged:
                    unchanged += 1
                    if unchanged >= 2 { break }
                    try await Task.sleep(nanoseconds: 250_000_000)
                    continue
                case .lost:
                    Log.info("scrolling capture: frame does not continue the page, stopping")
                    unchanged = 99
                default:
                    unchanged = 0
                    let target = max(20, stitcher.scrollingHeight / 2)
                    let shift = max(1, stitcher.lastShift)
                    stepPoints = max(10, min(Int(rect.height), stepPoints * target / shift))
                    pill?.setHeight(Int(CGFloat(stitcher.height) / scale))
                }
                if unchanged >= 2 || stitcher.height >= Self.maxHeight { break }
            }

            var (pixels, height) = stitcher.compose()
            height = min(height, Self.maxHeight)
            pixels = Array(pixels.prefix(w * 4 * height))
            guard let tall = Self.image(pixels, width: w, height: height) else { return }
            pill?.orderOut(nil)
            Log.info("scrolling capture \(w)x\(height) header \(stitcher.header) footer \(stitcher.footer)")
            if height <= h {
                alert(L.t("Nothing scrolled"), L.t("Cometsnap kept what was visible. Pick the part of the window that scrolls, and keep the mouse still."))
            }
            app.capture.deliver(tall, scale: scale, from: rect)
        } catch {
            app.report(error)
        }
    }

    private func shouldStop(_ park: CGPoint) -> Bool {
        if stopRequested || CGEventSource.keyState(.combinedSessionState, key: 0x35 /* Esc */) { return true }
        let m = NSEvent.mouseLocation
        return abs(m.x - park.x) > 8 || abs(m.y - park.y) > 8
    }

    /// Grabs until two grabs in a row are the same (smooth scrolling has finished).
    private func settled(_ grab: () async throws -> CGImage, previous: [UInt8], width: Int, height: Int) async throws -> (CGImage, [UInt8]) {
        try await Task.sleep(nanoseconds: 140_000_000)
        var image = try await grab()
        var current = Self.pixels(image, width: width, height: height)
        var waited = 140
        while waited < 1500 {
            try await Task.sleep(nanoseconds: 70_000_000)
            waited += 70
            let nextImage = try await grab()
            let next = Self.pixels(nextImage, width: width, height: height)
            let same = next == current
            image = nextImage
            current = next
            if same && (waited >= 400 || next != previous) { break }
        }
        return (image, current)
    }

    private static func scroll(byPoints points: Int) {
        let event = CGEvent(scrollWheelEvent2Source: nil, units: .pixel, wheelCount: 1, wheel1: Int32(-points), wheel2: 0, wheel3: 0)
        event?.post(tap: .cghidEventTap)
    }

    /// AppKit global (y up from the bottom of the main screen) to CG global (y down from its top).
    private static func cgPoint(_ p: CGPoint) -> CGPoint {
        CGPoint(x: p.x, y: (NSScreen.screens.first?.frame.height ?? 0) - p.y)
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

    private func alert(_ title: String, _ text: String) {
        NSApp.activate()
        let a = NSAlert()
        a.icon = Theme.logo
        a.messageText = title
        a.informativeText = text
        a.runModal()
    }
}

/// The little pill shown while a scrolling capture runs (Cometsnap's own windows are left out of the frames).
final class ScrollingPill: NSPanel {
    private let heightLabel = NSTextField(labelWithString: "")

    init() {
        super.init(contentRect: CGRect(x: 0, y: 0, width: 260, height: 34), styleMask: [.borderless, .nonactivatingPanel],
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
        let title = NSTextField(labelWithString: L.t("Scrolling…"))
        title.font = Theme.ui(13, .semibold)
        title.textColor = .white
        heightLabel.font = Theme.ui(12)
        heightLabel.textColor = Theme.yellow
        let hint = NSTextField(labelWithString: L.t("Esc to stop"))
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
        heightLabel.stringValue = ""
        let vf = screen.visibleFrame
        let x = min(max(rect.midX - frame.width / 2, vf.minX), vf.maxX - frame.width)
        var y = rect.maxY + 8
        if y + frame.height > vf.maxY { y = rect.minY - 8 - frame.height }
        if y < vf.minY { y = min(rect.maxY, vf.maxY) - 8 - frame.height }
        setFrameOrigin(CGPoint(x: x, y: y))
        orderFrontRegardless()
    }

    func setHeight(_ points: Int) { heightLabel.stringValue = L.f("{0} px", points) }
}
