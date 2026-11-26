import AppKit
import ScreenCaptureKit

/// A frozen copy of one screen, taken the instant the hotkey fires.
struct FrozenScreen: @unchecked Sendable {
    let screen: NSScreen
    let image: CGImage
    /// Backing scale (pixels per point) of the captured image.
    let scale: CGFloat
}

/// A window on screen, in AppKit global coordinates (y up).
struct WindowInfo {
    let frame: CGRect
    let owner: String
    let title: String
}

enum CaptureError: LocalizedError {
    case noPermission
    case failed(String)

    var errorDescription: String? {
        switch self {
        case .noPermission: return "Cometsnap needs the Screen Recording permission."
        case .failed(let s): return s
        }
    }
}

/// Takes screenshots with ScreenCaptureKit and lists windows for hover detection.
final class ScreenGrabber {
    static let shared = ScreenGrabber()

    private var cachedContent: SCShareableContent?
    private var cachedAt = Date.distantPast

    init() {
        NotificationCenter.default.addObserver(forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main) { [weak self] _ in
            self?.cachedContent = nil
        }
    }

    static var hasPermission: Bool { CGPreflightScreenCaptureAccess() }

    /// Shows the system prompt the first time, afterwards it only opens System Settings.
    static func requestPermission() {
        if !CGRequestScreenCaptureAccess() {
            openPrivacySettings()
        }
    }

    static func openPrivacySettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture") {
            NSWorkspace.shared.open(url)
        }
    }

    /// Display list and our own app (to exclude our windows), cached for a few seconds:
    /// fetching it is the slowest part of a capture.
    func content(fresh: Bool = false) async throws -> SCShareableContent {
        if !fresh, let c = cachedContent, Date().timeIntervalSince(cachedAt) < 30 { return c }
        do {
            let c = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
            cachedContent = c
            cachedAt = Date()
            return c
        } catch {
            if !ScreenGrabber.hasPermission { throw CaptureError.noPermission }
            throw CaptureError.failed(error.localizedDescription)
        }
    }

    /// Warms the cache so the first capture is quick.
    func prewarm() {
        guard ScreenGrabber.hasPermission else { return }
        Task { _ = try? await self.content(fresh: true) }
    }

    func display(for screen: NSScreen, in content: SCShareableContent) -> SCDisplay? {
        content.displays.first { $0.displayID == screen.displayID }
    }

    func ownApplication(in content: SCShareableContent) -> SCRunningApplication? {
        let pid = ProcessInfo.processInfo.processIdentifier
        return content.applications.first { $0.processID == pid }
    }

    /// Freezes every screen (Cometsnap's own windows are left out).
    func freezeAll(screens: [NSScreen] = NSScreen.screens) async throws -> [FrozenScreen] {
        var content = try await content()
        if screens.contains(where: { display(for: $0, in: content) == nil }) { content = try await self.content(fresh: true) }
        let own = ownApplication(in: content)
        return try await withThrowingTaskGroup(of: FrozenScreen?.self) { group in
            for screen in screens {
                guard let d = display(for: screen, in: content) else { continue }
                group.addTask {
                    let img = try await Self.shot(display: d, excluding: own, scale: screen.backingScaleFactor, rect: nil)
                    return FrozenScreen(screen: screen, image: img, scale: screen.backingScaleFactor)
                }
            }
            var out: [FrozenScreen] = []
            for try await f in group { if let f { out.append(f) } }
            return out
        }
    }

    static func shot(display: SCDisplay, excluding own: SCRunningApplication?, scale: CGFloat, rect: CGRect?) async throws -> CGImage {
        let filter = SCContentFilter(display: display, excludingApplications: own.map { [$0] } ?? [], exceptingWindows: [])
        let config = SCStreamConfiguration()
        let size = rect?.size ?? CGSize(width: display.width, height: display.height)
        if let rect { config.sourceRect = rect }
        config.width = Int((size.width * scale).rounded())
        config.height = Int((size.height * scale).rounded())
        config.showsCursor = false
        config.captureResolution = .best
        config.colorSpaceName = CGColorSpace.sRGB
        config.scalesToFit = false
        do {
            return try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: config)
        } catch {
            if !hasPermission { throw CaptureError.noPermission }
            throw CaptureError.failed(error.localizedDescription)
        }
    }

    /// Visible normal windows, front to back, in AppKit global coordinates.
    static func windows() -> [WindowInfo] {
        guard let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] else {
            return []
        }
        let pid = ProcessInfo.processInfo.processIdentifier
        let mainHeight = NSScreen.screens.first?.frame.height ?? 0
        var out: [WindowInfo] = []
        for w in list {
            guard let layer = w[kCGWindowLayer as String] as? Int, layer == 0,
                  let owner = w[kCGWindowOwnerPID as String] as? Int32, owner != pid,
                  let b = w[kCGWindowBounds as String] as? [String: Any],
                  let cg = CGRect(dictionaryRepresentation: b as CFDictionary) else { continue }
            if let alpha = w[kCGWindowAlpha as String] as? Double, alpha < 0.05 { continue }
            if cg.width < 40 || cg.height < 24 { continue }
            // CG global space has its origin at the top-left of the main screen, y down.
            let frame = CGRect(x: cg.minX, y: mainHeight - cg.maxY, width: cg.width, height: cg.height)
            out.append(WindowInfo(frame: frame, owner: w[kCGWindowOwnerName as String] as? String ?? "",
                                  title: w[kCGWindowName as String] as? String ?? ""))
        }
        return out
    }
}

extension NSScreen {
    var displayID: CGDirectDisplayID {
        (deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value ?? CGMainDisplayID()
    }

    static func containing(_ p: CGPoint) -> NSScreen? {
        screens.first { NSMouseInRect(p, $0.frame, false) } ?? main
    }

    static var underMouse: NSScreen { containing(NSEvent.mouseLocation) ?? screens[0] }

    /// Converts an AppKit-global rect on this screen to the display's own top-left point space
    /// (what ScreenCaptureKit's sourceRect expects).
    func displayRect(fromGlobal r: CGRect) -> CGRect {
        CGRect(x: r.minX - frame.minX, y: frame.maxY - r.maxY, width: r.width, height: r.height)
    }
}
