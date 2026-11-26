import AppKit
import CometsnapCore

@main
enum CometsnapMain {
    static func main() {
        let app = NSApplication.shared
        let controller = AppController()
        app.delegate = controller
        app.setActivationPolicy(.accessory)
        app.run()
    }
}

struct HotkeyDisplay {
    let capture: String
    let video: String
    let gif: String
}

/// Owns everything: settings, the menu bar item, the flyout, capture, recording and editors.
final class AppController: NSObject, NSApplicationDelegate {
    let paths = AppPaths.standard
    private(set) var settings = AppSettings()
    private(set) var recentItems: [RecentItem] = []

    private(set) var statusItem: StatusItemController!
    private(set) var flyout: FlyoutPanel!
    private(set) var capture: CaptureController!
    private(set) var recorder: Recorder!
    private(set) lazy var editCard = EditCard()
    private var editors: [EditorWindow] = []
    private var settingsWindow: SettingsWindow?
    private var welcome: WelcomeWindow?

    var capturesDirectory: URL { paths.capturesDirectory(settings) }

    var hotkeyDisplay: HotkeyDisplay {
        HotkeyDisplay(capture: settings.takeOverSystemShortcut ? "⌘⇧4" : "⌘⇧2", video: "⌘⇧7", gif: "⌘⇧8")
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        try? FileManager.default.createDirectory(at: paths.dataDirectory, withIntermediateDirectories: true)
        Log.file = paths.logFile
        settings = AppSettings.load(from: paths.settingsFile)
        Theme.registerFonts()
        NSApp.appearance = NSAppearance(named: .darkAqua)
        buildMainMenu()

        capture = CaptureController(app: self)
        recorder = Recorder(app: self)
        statusItem = StatusItemController(app: self)
        flyout = FlyoutPanel(app: self)
        editCard.onEdit = { [weak self] url, image, scale in self?.openEditor(url: url, image: image, scale: scale) }
        editCard.onOpen = { url in NSWorkspace.shared.open(url) }

        try? FileManager.default.createDirectory(at: capturesDirectory, withIntermediateDirectories: true)
        refreshRecent()
        registerHotkeys()
        ScreenGrabber.shared.prewarm()
        Log.info("Cometsnap \(Bundle.main.shortVersion) started")

        if !settings.snapCountSeeded { seedSnapCount() }
        // A new season can start while Cometsnap runs.
        Timer.scheduledTimer(withTimeInterval: 3600, repeats: true) { [weak self] _ in self?.statusItem.refreshIcon() }

        if !settings.firstRunDone { showWelcome() }
    }

    func applicationWillTerminate(_ notification: Notification) {
        if recorder.isRecording { recorder.stop() }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        toggleFlyout()
        return false
    }

    // MARK: settings

    func update(_ change: (inout AppSettings) -> Void) {
        let old = settings
        change(&settings)
        settings.save(to: paths.settingsFile)
        if old.takeOverSystemShortcut != settings.takeOverSystemShortcut {
            SystemShortcuts.setBuiltInRegionShortcut(enabled: !settings.takeOverSystemShortcut)
            registerHotkeys()
        }
        if old.seasonalLooks != settings.seasonalLooks { statusItem.refreshIcon() }
        if old.capturesFolder != settings.capturesFolder {
            try? FileManager.default.createDirectory(at: capturesDirectory, withIntermediateDirectories: true)
            capturesChanged()
        }
    }

    func registerHotkeys() {
        let hk = HotkeyCenter.shared
        hk.unregisterAll()
        let region: () -> Void = { [weak self] in
            guard let self else { return }
            if self.recorder.isRecording { self.recorder.stop() } else { self.capture.startRegion() }
        }
        hk.register(.captureCmdShift2, action: region)
        hk.register(.captureF13, action: region)
        if settings.takeOverSystemShortcut {
            // Give macOS a moment to release the shortcut after the change.
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
                hk.register(.captureCmdShift4, action: region)
            }
        }
        let video: () -> Void = { [weak self] in self?.recorder.toggleRegion(format: .mp4) }
        let gif: () -> Void = { [weak self] in self?.recorder.toggleRegion(format: .gif) }
        hk.register(.videoCmdShift7, action: video)
        hk.register(.videoShiftF13, action: video)
        hk.register(.gifCmdShift8, action: gif)
        hk.register(.gifCtrlShiftF13, action: gif)
    }

    // MARK: captures

    func refreshRecent() {
        let root = capturesDirectory
        DispatchQueue.global(qos: .userInitiated).async {
            let items = RecentCaptures.scan(root: root)
            DispatchQueue.main.async {
                self.recentItems = items
                self.flyout.markDirty()
            }
        }
    }

    func capturesChanged() { refreshRecent() }

    // MARK: playful touches

    /// Counts a snap; round numbers and the first snap of a month get a tiny celebration.
    func countSnap() {
        var event: SnapEvent?
        update { event = SnapMilestones.record(&$0, now: Date()) }
        guard settings.celebrations, let event else { return }
        var delay: TimeInterval = 0.9 // after the comet has landed
        if let month = event.finishedMonth {
            let n = SnapMilestones.formatted(month.count)
            celebrate(after: delay, meteors: 14, title: "You captured \(n) snap\(month.count == 1 ? "" : "s")",
                      subtitle: "in \(month.title). New month, new snaps!")
            delay += 3.4
        }
        if let n = event.milestone {
            celebrate(after: delay, meteors: 22, title: "✦ " + SnapMilestones.title(n), subtitle: "Thanks for snapping with Cometsnap.")
        }
    }

    /// A small meteor shower above the corner where the Copied card sits. Never during a capture.
    private func celebrate(after delay: TimeInterval, meteors: Int, title: String, subtitle: String, tries: Int = 0) {
        DispatchQueue.main.asyncAfter(deadline: .now() + delay) { [weak self] in
            guard let self else { return }
            if self.capture.isBusy || self.recorder.isRecording {
                if tries < 20 { self.celebrate(after: 1, meteors: meteors, title: title, subtitle: subtitle, tries: tries + 1) }
                return
            }
            let vf = NSScreen.underMouse.visibleFrame
            let area = CGRect(x: vf.maxX - 392, y: vf.minY + 112, width: 380, height: 250)
            MeteorShower.play(over: area, meteors: meteors, duration: 3, title: title, subtitle: subtitle)
        }
    }

    /// ↑ ↑ ↓ ↓ ← → ← → B A in the menu bar panel.
    func meteorShowerEasterEgg() {
        guard !capture.isBusy else { return }
        Log.info("Konami code: meteor shower")
        let vf = NSScreen.underMouse.visibleFrame
        MeteorShower.play(over: vf, meteors: 70, duration: 3.6, title: "☄ METEOR SHOWER ☄",
                          subtitle: "You found the secret. Make a wish!", captionAtBottom: false)
    }

    /// First run with milestones: start from the snaps already in the captures folder, silently.
    private func seedSnapCount() {
        let root = capturesDirectory
        DispatchQueue.global(qos: .utility).async { [weak self] in
            var dates: [Date] = []
            let fm = FileManager.default
            if let e = fm.enumerator(at: root, includingPropertiesForKeys: [.contentModificationDateKey], options: [.skipsHiddenFiles]) {
                for case let u as URL in e where MediaKind.of(u) == .image && u.lastPathComponent.hasPrefix(SnapNaming.snapPrefix) {
                    dates.append((try? u.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? Date())
                }
            }
            DispatchQueue.main.async {
                self?.update { SnapMilestones.seed(&$0, existing: dates, now: Date()) }
            }
        }
    }

    func open(_ url: URL) {
        if MediaKind.of(url) == .image { openEditor(url: url) } else { NSWorkspace.shared.open(url) }
    }

    func openCapturesFolder() {
        try? FileManager.default.createDirectory(at: capturesDirectory, withIntermediateDirectories: true)
        NSWorkspace.shared.open(capturesDirectory)
    }

    func openEditor(url: URL?, image: CGImage? = nil, scale: CGFloat? = nil) {
        if let url, let existing = editors.first(where: { $0.url == url }) {
            NSApp.activate()
            existing.makeKeyAndOrderFront(nil)
            return
        }
        let e = EditorWindow(app: self, url: url, image: image, scale: scale)
        editors.append(e)
        e.present()
    }

    func editorClosed(_ e: EditorWindow) {
        editors.removeAll { $0 === e }
    }

    // MARK: panels

    func toggleFlyout() {
        flyout.toggle(near: statusItem.buttonFrame)
    }

    func hidePanels() {
        flyout?.hide()
    }

    func showSettings() {
        hidePanels()
        if settingsWindow == nil { settingsWindow = SettingsWindow(app: self) }
        settingsWindow?.reload()
        NSApp.activate()
        settingsWindow?.makeKeyAndOrderFront(nil)
    }

    func showWelcome() {
        if welcome == nil { welcome = WelcomeWindow(app: self) }
        NSApp.activate()
        welcome?.center()
        welcome?.makeKeyAndOrderFront(nil)
    }

    func welcomeDone() {
        welcome?.orderOut(nil)
        welcome = nil
    }

    func showAbout() {
        hidePanels()
        NSApp.activate()
        let credits = NSAttributedString(string: "Free, fun and snappy screenshots and recordings.\nMIT licensed · github.com/mike-risky/cometsnap",
                                         attributes: [.font: Theme.ui(11), .foregroundColor: NSColor.secondaryLabelColor])
        NSApp.orderFrontStandardAboutPanel(options: [.credits: credits, .applicationIcon: Theme.logo])
    }

    // MARK: errors and permission

    func report(_ error: Error) {
        if let ce = error as? CaptureError, case .noPermission = ce { showPermissionHelp(); return }
        Log.info("error: \(error.localizedDescription)")
        NSApp.activate()
        let a = NSAlert()
        a.messageText = "Cometsnap hit a snag"
        a.informativeText = error.localizedDescription
        a.runModal()
    }

    func showPermissionHelp() {
        hidePanels()
        NSApp.activate()
        let a = NSAlert()
        a.icon = Theme.logo
        a.messageText = "Allow Cometsnap to see your screen"
        a.informativeText = """
        macOS asks once. In System Settings › Privacy & Security › Screen & System Audio Recording, \
        turn on Cometsnap. macOS then asks to quit and reopen it: click "Quit & Reopen".
        """
        a.addButton(withTitle: "Open System Settings")
        a.addButton(withTitle: "Later")
        if a.runModal() == .alertFirstButtonReturn { ScreenGrabber.requestPermission() }
    }

    func relaunch() {
        let path = Bundle.main.bundlePath
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/sh")
        p.arguments = ["-c", "sleep 1; /usr/bin/open \"\(path)\""]
        try? p.run()
        NSApp.terminate(nil)
    }

    // MARK: menu (not shown for a menu bar app, but gives text fields and the editor their shortcuts)

    private func buildMainMenu() {
        let main = NSMenu()
        let appItem = NSMenuItem()
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "About Cometsnap", action: #selector(aboutAction), keyEquivalent: "").target = self
        appMenu.addItem(withTitle: "Settings…", action: #selector(settingsAction), keyEquivalent: ",").target = self
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "Quit Cometsnap", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appItem.submenu = appMenu
        main.addItem(appItem)

        let editItem = NSMenuItem()
        let edit = NSMenu(title: "Edit")
        edit.addItem(withTitle: "Undo", action: Selector(("undo:")), keyEquivalent: "z")
        let redo = edit.addItem(withTitle: "Redo", action: Selector(("redo:")), keyEquivalent: "z")
        redo.keyEquivalentModifierMask = [.command, .shift]
        edit.addItem(.separator())
        edit.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = edit
        main.addItem(editItem)

        let windowItem = NSMenuItem()
        let window = NSMenu(title: "Window")
        window.addItem(withTitle: "Close", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        windowItem.submenu = window
        main.addItem(windowItem)
        NSApp.mainMenu = main
    }

    @objc private func aboutAction() { showAbout() }
    @objc private func settingsAction() { showSettings() }
}

extension EditorWindow {
    @objc func undo(_ sender: Any?) { canvas.undo() }
    @objc func redo(_ sender: Any?) { canvas.redo() }
    @objc func copy(_ sender: Any?) { performCopyAndClose() }
}

extension Bundle {
    var shortVersion: String { infoDictionary?["CFBundleShortVersionString"] as? String ?? "dev" }
}
