import AppKit
import CometsnapCore

/// The comet in the menu bar. Click opens the flyout, right click (or Control-click) the full
/// menu. While recording, a red dot pulses on it and a click stops the recording.
final class StatusItemController: NSObject, NSMenuDelegate {
    private unowned let app: AppController
    let item: NSStatusItem
    private var pulseTimer: Timer?
    private var pulseOn = true
    private var countdownTimer: Timer?

    init(app: AppController) {
        self.app = app
        item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        super.init()
        if let b = item.button {
            b.image = Theme.menuBarIcon
            b.imagePosition = .imageLeading
            b.toolTip = "Cometsnap"
            b.target = self
            b.action = #selector(clicked(_:))
            b.sendAction(on: [.leftMouseUp, .rightMouseUp])
        }
    }

    /// Frame of the menu bar button in global coordinates.
    var buttonFrame: CGRect? {
        guard let b = item.button, let w = b.window else { return nil }
        return w.convertToScreen(b.convert(b.bounds, to: nil))
    }

    @objc private func clicked(_ sender: Any?) {
        let e = NSApp.currentEvent
        let right = e?.type == .rightMouseUp || e?.modifierFlags.contains(.control) == true
        if app.recorder.isRecording && !right {
            app.recorder.stop()
            return
        }
        if right { showMenu() } else { app.toggleFlyout() }
    }

    func showMenu() {
        app.hidePanels()
        let menu = buildMenu()
        menu.delegate = self
        item.menu = menu
        item.button?.performClick(nil)
    }

    func menuDidClose(_ menu: NSMenu) {
        // Detach again so a left click keeps opening the flyout.
        item.menu = nil
    }

    private func buildMenu() -> NSMenu {
        let m = NSMenu()
        let keys = app.hotkeyDisplay
        let capture = NSMenu()
        capture.addItem(action("Region / Window\t\(keys.capture)", #selector(region)))
        capture.addItem(action("Fullscreen", #selector(fullscreen)))
        capture.addItem(.separator())
        for s in [3, 5, 10] {
            let i = action("Delayed \(s) s", #selector(delayed(_:)))
            i.tag = s
            capture.addItem(i)
        }
        m.addItem(sub("Capture", capture, symbol: "camera.viewfinder"))

        let record = NSMenu()
        if app.recorder.isRecording {
            record.addItem(action("Stop recording", #selector(stopRecording)))
        } else {
            record.addItem(action("Video of a region\t\(keys.video)", #selector(recordRegion)))
            record.addItem(action("Video of the whole screen", #selector(recordScreen)))
            record.addItem(action("GIF of a region\t\(keys.gif)", #selector(recordGif)))
        }
        m.addItem(sub("Record", record, symbol: "record.circle"))

        let recent = NSMenu()
        let items = app.recentItems.prefix(10)
        if items.isEmpty { recent.addItem(withTitle: "No captures yet", action: nil, keyEquivalent: "") }
        for r in items {
            let i = action(r.name, #selector(openRecent(_:)))
            i.representedObject = r.url
            recent.addItem(i)
        }
        m.addItem(sub("Recent", recent, symbol: "clock"))
        m.addItem(.separator())
        m.addItem(action("Open captures folder", #selector(openFolder), symbol: "folder"))
        m.addItem(action("Settings…", #selector(settings), key: ",", symbol: "gearshape"))
        m.addItem(action("About Cometsnap", #selector(about), symbol: "info.circle"))
        m.addItem(.separator())
        m.addItem(action("Quit Cometsnap", #selector(quit), key: "q"))
        return m
    }

    private func action(_ title: String, _ sel: Selector, key: String = "", symbol: String? = nil) -> NSMenuItem {
        let i = NSMenuItem(title: title, action: sel, keyEquivalent: key)
        i.target = self
        if let symbol { i.image = NSImage(systemSymbolName: symbol, accessibilityDescription: nil) }
        return i
    }

    private func sub(_ title: String, _ menu: NSMenu, symbol: String) -> NSMenuItem {
        let i = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        i.image = NSImage(systemSymbolName: symbol, accessibilityDescription: nil)
        i.submenu = menu
        return i
    }

    @objc private func region() { app.capture.startRegion() }
    @objc private func fullscreen() { app.capture.captureFullscreen() }
    @objc private func delayed(_ sender: NSMenuItem) { app.capture.captureDelayed(sender.tag) }
    @objc private func recordRegion() { app.recorder.toggleRegion(format: .mp4) }
    @objc private func recordScreen() { app.recorder.startFullscreen(format: .mp4) }
    @objc private func recordGif() { app.recorder.toggleRegion(format: .gif) }
    @objc private func stopRecording() { app.recorder.stop() }
    @objc private func openRecent(_ sender: NSMenuItem) { if let u = sender.representedObject as? URL { app.open(u) } }
    @objc private func openFolder() { app.openCapturesFolder() }
    @objc private func settings() { app.showSettings() }
    @objc private func about() { app.showAbout() }
    @objc private func quit() { NSApp.terminate(nil) }

    // MARK: states

    func setRecording(_ on: Bool) {
        pulseTimer?.invalidate()
        pulseTimer = nil
        guard let b = item.button else { return }
        if on {
            pulseOn = true
            b.image = Theme.recordingIcon(dotVisible: true)
            b.toolTip = "Recording… click to stop"
            pulseTimer = Timer.scheduledTimer(withTimeInterval: 0.6, repeats: true) { [weak self] _ in
                guard let self else { return }
                self.pulseOn.toggle()
                self.item.button?.image = Theme.recordingIcon(dotVisible: self.pulseOn)
            }
        } else {
            b.image = Theme.menuBarIcon
            b.toolTip = "Cometsnap"
        }
    }

    /// Picks up a new season (or the Seasonal looks switch).
    func refreshIcon() {
        guard pulseTimer == nil else { return }
        item.button?.image = Theme.menuBarIcon
    }

    /// Shows 3, 2, 1 next to the icon, then runs `done`.
    func countdown(_ seconds: Int, done: @escaping () -> Void) {
        countdownTimer?.invalidate()
        var left = seconds
        item.button?.title = " \(left)"
        countdownTimer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] t in
            left -= 1
            if left <= 0 {
                t.invalidate()
                self?.item.button?.title = ""
                done()
            } else {
                self?.item.button?.title = " \(left)"
            }
        }
    }
}
