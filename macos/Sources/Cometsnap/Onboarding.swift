import AppKit
import CometsnapCore

/// First run: screen permission, "use ⌘⇧4 for Cometsnap" (replace the old tool), start at login.
final class WelcomeWindow: NSWindow {
    private unowned let app: AppController
    private let permissionStatus = NSTextField(labelWithString: "")
    private let permissionButton: SpaceButton
    private let takeOver = NSButton(checkboxWithTitle: "Use ⌘⇧4 for Cometsnap (replaces the built-in macOS shortcut, switch back any time in Settings)", target: nil, action: nil)
    private let login = NSButton(checkboxWithTitle: "Start Cometsnap when I log in", target: nil, action: nil)
    private var poll: Timer?

    init(app: AppController) {
        self.app = app
        permissionButton = SpaceButton(title: "Allow screen access", symbol: "lock.open", fill: Theme.blue)
        super.init(contentRect: CGRect(x: 0, y: 0, width: 560, height: 560), styleMask: [.titled, .closable, .fullSizeContentView],
                   backing: .buffered, defer: false)
        titlebarAppearsTransparent = true
        titleVisibility = .hidden
        appearance = NSAppearance(named: .darkAqua)
        backgroundColor = Theme.night
        isReleasedWhenClosed = false
        build()
        updatePermission()
        poll = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in self?.updatePermission() }
    }

    private func build() {
        let header = StarHeaderView(frame: CGRect(x: 0, y: 0, width: 560, height: 110))
        let welcome = NSTextField(labelWithString: "Welcome aboard! Three quick things and you're ready to snap.")
        welcome.font = Theme.ui(15, .semibold)
        welcome.textColor = .white

        let step1 = stepLabel("1", "Let Cometsnap see your screen", "macOS needs your OK once. After you switch it on, macOS asks to quit and reopen Cometsnap.")
        permissionButton.onClick = { [weak self] in
            if ScreenGrabber.hasPermission { self?.app.relaunch() } else { ScreenGrabber.requestPermission() }
        }
        permissionStatus.font = Theme.ui(12, .medium)

        let step2 = stepLabel("2", "Replace your old screenshot shortcut", "Snap with ⌘⇧4 like before, but with window detection, a loupe and instant copy.")
        takeOver.state = .on
        styleCheckbox(takeOver)

        let step3 = stepLabel("3", "Always ready", "Cometsnap lives in the menu bar and uses almost nothing while idle.")
        login.state = .on
        styleCheckbox(login)

        let keys = NSTextField(wrappingLabelWithString: "Shortcuts: ⌘⇧2 (or ⌘⇧4) capture · ⌘⇧7 record video · ⌘⇧8 record GIF · Print Screen / F13 on PC keyboards. Release the mouse and it's copied and saved.")
        keys.font = Theme.ui(12)
        keys.textColor = Theme.muted

        let go = SpaceButton(title: "Let's go ☄️", symbol: nil, fill: Theme.terracotta, fontSize: 15)
        go.onClick = { [weak self] in self?.finish() }

        let permRow = NSStackView(views: [permissionButton, permissionStatus])
        permRow.spacing = 12
        let stack = NSStackView(views: [welcome, step1, permRow, step2, takeOver, step3, login, keys])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 12
        stack.setCustomSpacing(20, after: welcome)
        stack.setCustomSpacing(22, after: permRow)
        stack.setCustomSpacing(22, after: takeOver)
        stack.setCustomSpacing(22, after: login)

        let root = NSView()
        contentView = root
        for v in [header, stack, go] as [NSView] {
            v.translatesAutoresizingMaskIntoConstraints = false
            root.addSubview(v)
        }
        NSLayoutConstraint.activate([
            header.topAnchor.constraint(equalTo: root.topAnchor),
            header.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            header.widthAnchor.constraint(equalToConstant: 560),
            header.heightAnchor.constraint(equalToConstant: 110),
            stack.topAnchor.constraint(equalTo: header.bottomAnchor, constant: 22),
            stack.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 28),
            stack.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -28),
            keys.widthAnchor.constraint(equalToConstant: 504),
            takeOver.widthAnchor.constraint(lessThanOrEqualToConstant: 504),
            permissionButton.widthAnchor.constraint(equalToConstant: 190),
            permissionButton.heightAnchor.constraint(equalToConstant: 34),
            go.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -28),
            go.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -22),
            go.widthAnchor.constraint(equalToConstant: 150),
            go.heightAnchor.constraint(equalToConstant: 40),
        ])
        header.start()
    }

    private func stepLabel(_ n: String, _ title: String, _ detail: String) -> NSView {
        let num = NSTextField(labelWithString: n)
        num.font = Theme.heading(20)
        num.textColor = Theme.yellow
        let t = NSTextField(labelWithString: title)
        t.font = Theme.ui(14, .semibold)
        t.textColor = .white
        let d = NSTextField(wrappingLabelWithString: detail)
        d.font = Theme.ui(12)
        d.textColor = Theme.muted
        d.preferredMaxLayoutWidth = 460
        let col = NSStackView(views: [t, d])
        col.orientation = .vertical
        col.alignment = .leading
        col.spacing = 2
        let row = NSStackView(views: [num, col])
        row.alignment = .top
        row.spacing = 12
        return row
    }

    private func styleCheckbox(_ b: NSButton) {
        b.attributedTitle = NSAttributedString(string: b.title, attributes: [.foregroundColor: NSColor.white, .font: Theme.ui(13)])
    }

    private func updatePermission() {
        if ScreenGrabber.hasPermission {
            permissionStatus.stringValue = "✓ Screen access is on"
            permissionStatus.textColor = NSColor(hex: 0x2ED573)
            permissionButton.isHidden = true
        } else {
            permissionStatus.stringValue = "Not allowed yet"
            permissionStatus.textColor = Theme.yellow
        }
    }

    private func finish() {
        poll?.invalidate()
        let take = takeOver.state == .on
        app.update { s in
            s.firstRunDone = true
            s.takeOverSystemShortcut = take
        }
        LoginItem.set(login.state == .on)
        app.welcomeDone()
        if !ScreenGrabber.hasPermission { ScreenGrabber.requestPermission() }
    }

    override func close() {
        poll?.invalidate()
        super.close()
    }
}

/// Settings: plain and short.
final class SettingsWindow: NSWindow {
    private unowned let app: AppController
    private let login = NSButton(checkboxWithTitle: "Start at login", target: nil, action: nil)
    private let takeOver = NSButton(checkboxWithTitle: "Use ⌘⇧4 for Cometsnap (turns off the built-in macOS ⌘⇧4)", target: nil, action: nil)
    private let copyBox = NSButton(checkboxWithTitle: "Copy every capture to the clipboard", target: nil, action: nil)
    private let card = NSButton(checkboxWithTitle: "Show the small Edit card after a capture", target: nil, action: nil)
    private let comet = NSButton(checkboxWithTitle: "Comet animation into the menu bar", target: nil, action: nil)
    private let sound = NSButton(checkboxWithTitle: "Play a sound", target: nil, action: nil)
    private let audio = NSButton(checkboxWithTitle: "Record the Mac's sound in videos", target: nil, action: nil)
    private let celebrations = NSButton(checkboxWithTitle: "Little celebrations (a tiny meteor shower at 100, 1,000… snaps, and a monthly recap)", target: nil, action: nil)
    private let seasonal = NSButton(checkboxWithTitle: "Seasonal looks (a pumpkin comet for Halloween, a snowy one in December)", target: nil, action: nil)
    private let loupe = NSPopUpButton()
    private let gifFps = NSPopUpButton()
    private let folder = NSTextField(labelWithString: "")

    init(app: AppController) {
        self.app = app
        super.init(contentRect: CGRect(x: 0, y: 0, width: 560, height: 530), styleMask: [.titled, .closable], backing: .buffered, defer: false)
        title = "Cometsnap Settings"
        appearance = NSAppearance(named: .darkAqua)
        backgroundColor = Theme.night
        isReleasedWhenClosed = false
        center()
        build()
    }

    private func build() {
        for b in [login, takeOver, copyBox, card, comet, sound, audio, celebrations, seasonal] {
            b.target = self
            b.action = #selector(changed)
        }
        LoupeCorner.allCases.forEach { loupe.addItem(withTitle: $0.title) }
        loupe.target = self
        loupe.action = #selector(changed)
        [8, 10, 12, 15, 20, 25].forEach { gifFps.addItem(withTitle: "\($0) fps") }
        gifFps.target = self
        gifFps.action = #selector(changed)
        folder.textColor = Theme.muted
        folder.lineBreakMode = .byTruncatingMiddle
        folder.font = Theme.ui(12)
        let change = NSButton(title: "Change…", target: self, action: #selector(chooseFolder))
        let reveal = NSButton(title: "Open", target: self, action: #selector(openFolder))

        let title = NSTextField(labelWithAttributedString: Theme.wordmark(28))
        func row(_ label: String, _ v: NSView) -> NSView {
            let l = NSTextField(labelWithString: label)
            l.textColor = Theme.muted
            l.font = Theme.ui(12)
            l.widthAnchor.constraint(equalToConstant: 110).isActive = true
            let r = NSStackView(views: [l, v])
            r.spacing = 8
            return r
        }
        let folderRow = NSStackView(views: [folder, change, reveal])
        folderRow.spacing = 8
        folder.widthAnchor.constraint(equalToConstant: 250).isActive = true
        let keys = NSTextField(wrappingLabelWithString: "Shortcuts: ⌘⇧2 capture (also ⌘⇧4 when enabled above, and Print Screen / F13) · ⌘⇧7 video · ⌘⇧8 GIF · press again to stop.\nIn the overlay: click a window or drag · arrows nudge 1 px (⇧ 10 px) · Space moves the selection · F whole screen · W window detection · C copies the color · Esc cancels.")
        keys.font = Theme.ui(11)
        keys.textColor = Theme.muted
        keys.preferredMaxLayoutWidth = 460

        let stack = NSStackView(views: [title, login, takeOver, copyBox, card, comet, sound, audio, celebrations, seasonal,
                                        row("Loupe corner", loupe), row("GIF smoothness", gifFps), row("Captures folder", folderRow), keys])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 9
        stack.setCustomSpacing(16, after: title)
        stack.setCustomSpacing(16, after: seasonal)
        stack.translatesAutoresizingMaskIntoConstraints = false
        let root = NSView()
        root.addSubview(stack)
        contentView = root
        NSLayoutConstraint.activate([
            stack.topAnchor.constraint(equalTo: root.topAnchor, constant: 18),
            stack.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 24),
            stack.trailingAnchor.constraint(lessThanOrEqualTo: root.trailingAnchor, constant: -24),
            keys.widthAnchor.constraint(equalToConstant: 470),
        ])
    }

    func reload() {
        let s = app.settings
        login.state = LoginItem.isEnabled ? .on : .off
        takeOver.state = s.takeOverSystemShortcut ? .on : .off
        copyBox.state = s.copyToClipboard ? .on : .off
        card.state = s.showEditCard ? .on : .off
        comet.state = s.cometAnimation ? .on : .off
        sound.state = s.playSound ? .on : .off
        audio.state = s.recordSystemAudio ? .on : .off
        celebrations.state = s.celebrations ? .on : .off
        seasonal.state = s.seasonalLooks ? .on : .off
        loupe.selectItem(at: LoupeCorner.allCases.firstIndex(of: s.loupeCorner) ?? 2)
        let fpsValues = [8, 10, 12, 15, 20, 25]
        gifFps.selectItem(at: fpsValues.firstIndex(of: s.gifFramesPerSecond) ?? 3)
        folder.stringValue = app.capturesDirectory.path.replacingOccurrences(of: NSHomeDirectory(), with: "~")
    }

    @objc private func changed() {
        let fpsValues = [8, 10, 12, 15, 20, 25]
        if (login.state == .on) != LoginItem.isEnabled { LoginItem.set(login.state == .on) }
        app.update { s in
            s.takeOverSystemShortcut = takeOver.state == .on
            s.copyToClipboard = copyBox.state == .on
            s.showEditCard = card.state == .on
            s.cometAnimation = comet.state == .on
            s.playSound = sound.state == .on
            s.recordSystemAudio = audio.state == .on
            s.celebrations = celebrations.state == .on
            s.seasonalLooks = seasonal.state == .on
            s.loupeCorner = LoupeCorner.allCases[max(0, loupe.indexOfSelectedItem)]
            s.gifFramesPerSecond = fpsValues[max(0, gifFps.indexOfSelectedItem)]
        }
        if sound.state == .on { Sound.snap() }
    }

    @objc private func chooseFolder() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        panel.directoryURL = app.capturesDirectory
        panel.beginSheetModal(for: self) { [weak self] resp in
            guard resp == .OK, let url = panel.url, let self else { return }
            self.app.update { $0.capturesFolder = url.path }
            self.reload()
        }
    }

    @objc private func openFolder() { app.openCapturesFolder() }
}
