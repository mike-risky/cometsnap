import AppKit
import CometsnapCore

/// First run: screen permission, "use ⌘⇧4 for Cometsnap" (replace the old tool), start at login.
final class WelcomeWindow: NSWindow {
    private unowned let app: AppController
    private let permissionStatus = NSTextField(labelWithString: "")
    private let permissionButton: SpaceButton
    private let takeOver = NSButton(checkboxWithTitle: L.t("Use ⌘⇧3, ⌘⇧4 and ⌘⇧5 for Cometsnap (replaces the macOS screenshot shortcuts, switch back any time in Settings)"), target: nil, action: nil)
    private let login = NSButton(checkboxWithTitle: L.t("Start Cometsnap when I log in"), target: nil, action: nil)
    private var poll: Timer?

    init(app: AppController) {
        self.app = app
        permissionButton = SpaceButton(title: L.t("Allow screen access"), symbol: "lock.open", fill: Theme.blue)
        super.init(contentRect: CGRect(x: 0, y: 0, width: 560, height: 700), styleMask: [.titled, .closable, .fullSizeContentView],
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
        let welcome = NSTextField(labelWithString: L.t("Welcome aboard! Three quick things and you're ready to snap."))
        welcome.font = Theme.ui(15, .semibold)
        welcome.textColor = .white

        let step1 = stepLabel("1", L.t("Let Cometsnap see your screen"), L.t("macOS needs your OK once. After you switch it on, macOS asks to quit and reopen Cometsnap."))
        permissionButton.onClick = { [weak self] in
            if ScreenGrabber.hasPermission { self?.app.relaunch() } else { ScreenGrabber.requestPermission() }
        }
        permissionStatus.font = Theme.ui(12, .medium)

        let step2 = stepLabel("2", L.t("Replace your old screenshot shortcut"), L.t("Same keys as before: ⌘⇧4 an area or a window, ⌘⇧3 the whole screen, ⌘⇧5 the Cometsnap panel. Now with window detection, a loupe and instant copy."))
        takeOver.state = .on
        styleCheckbox(takeOver)

        let step3 = stepLabel("3", L.t("Always ready"), L.t("Cometsnap lives in the menu bar and uses almost nothing while idle."))
        login.state = .on
        styleCheckbox(login)

        let keys = briefing()

        let go = SpaceButton(title: L.t("Let's go ☄️"), symbol: nil, fill: Theme.terracotta, fontSize: 15)
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

    /// "Mission briefing": how Cometsnap works, in four lines and the handy shortcuts.
    private func briefing() -> NSView {
        let s = app.settings
        let capture = s.hotkeys[HotkeyAction.capture.rawValue] != nil ? s.hotkey(.capture).display : "⌘⇧4"
        let title = NSTextField(labelWithString: L.t("Mission briefing").uppercased(with: Locale(identifier: L.language)))
        title.font = Theme.heading(13)
        title.textColor = Theme.yellow
        var rows: [NSView] = [title]
        let steps = [
            L.f("Press {0}, then click a window or drag a box.", capture),
            L.t("Let go: it's copied and saved. Just paste it anywhere."),
            L.t("Click the Copied card to add arrows, boxes and text."),
            L.t("Your recent snaps wait in the menu bar."),
        ]
        for (i, step) in steps.enumerated() {
            let n = NSTextField(labelWithString: "\(i + 1)")
            n.font = Theme.heading(13)
            n.textColor = Theme.yellow
            n.widthAnchor.constraint(equalToConstant: 16).isActive = true
            let t = NSTextField(wrappingLabelWithString: step)
            t.font = Theme.ui(12.5)
            t.textColor = .white
            t.preferredMaxLayoutWidth = 440
            let row = NSStackView(views: [n, t])
            row.alignment = .firstBaseline
            row.spacing = 8
            rows.append(row)
        }
        let keys = NSTextField(wrappingLabelWithString: L.f("{0} video · {1} GIF · {2} copy text · {3} pick a color",
                                                             s.hotkey(.video).display, s.hotkey(.gif).display, s.hotkey(.text).display, s.hotkey(.color).display))
        keys.font = Theme.ui(11.5)
        keys.textColor = Theme.muted
        keys.preferredMaxLayoutWidth = 464
        rows.append(keys)
        let col = NSStackView(views: rows)
        col.orientation = .vertical
        col.alignment = .leading
        col.spacing = 5
        col.setCustomSpacing(8, after: title)
        col.edgeInsets = NSEdgeInsets(top: 12, left: 16, bottom: 12, right: 16)
        col.wantsLayer = true
        col.layer?.backgroundColor = Theme.surface.cgColor
        col.layer?.borderColor = Theme.line.cgColor
        col.layer?.borderWidth = 1
        col.layer?.cornerRadius = 12
        return col
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
            permissionStatus.stringValue = L.t("✓ Screen access is on")
            permissionStatus.textColor = NSColor(hex: 0x2ED573)
            permissionButton.isHidden = true
        } else {
            permissionStatus.stringValue = L.t("Not allowed yet")
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
    private let login = NSButton(checkboxWithTitle: L.t("Start at login"), target: nil, action: nil)
    private let takeOver = NSButton(checkboxWithTitle: L.t("Use ⌘⇧3, ⌘⇧4 and ⌘⇧5 for Cometsnap (turns off the macOS screenshot shortcuts)"), target: nil, action: nil)
    private let copyBox = NSButton(checkboxWithTitle: L.t("Copy every capture to the clipboard"), target: nil, action: nil)
    private let card = NSButton(checkboxWithTitle: L.t("Show the small Edit card after a capture"), target: nil, action: nil)
    private let comet = NSButton(checkboxWithTitle: L.t("Comet animation into the menu bar"), target: nil, action: nil)
    private let sound = NSButton(checkboxWithTitle: L.t("Play a sound"), target: nil, action: nil)
    private let audio = NSButton(checkboxWithTitle: L.t("Record the Mac's sound in videos"), target: nil, action: nil)
    private let celebrations = NSButton(checkboxWithTitle: L.t("Little celebrations (a tiny meteor shower at 100, 1,000… snaps, and a monthly recap)"), target: nil, action: nil)
    private let seasonal = NSButton(checkboxWithTitle: L.t("Seasonal looks (a pumpkin comet for Halloween, a snowy one in December)"), target: nil, action: nil)
    private let loupe = NSPopUpButton()
    private let gifFps = NSPopUpButton()
    private let language = NSPopUpButton()
    private let folder = NSTextField(labelWithString: "")
    private let saveBox = NSButton(checkboxWithTitle: L.t("Save every capture"), target: nil, action: nil)
    private let askBox = NSButton(checkboxWithTitle: L.t("Ask where to save each time"), target: nil, action: nil)
    private let editorBox = NSButton(checkboxWithTitle: L.t("Open in the editor right away"), target: nil, action: nil)
    private let nameField = NSTextField(string: "")
    private let nameExample = NSTextField(labelWithString: "")
    private let tags = NSPopUpButton(frame: .zero, pullsDown: true)
    private let format = NSPopUpButton()
    private var hotkeyButtons: [HotkeyAction: NSButton] = [:]
    private var listening: HotkeyAction?
    private var keyMonitor: Any?
    private var themeButtons: [NSButton] = []
    private let preview = ThemePreviewView(frame: CGRect(x: 0, y: 0, width: 460, height: 132))

    init(app: AppController) {
        self.app = app
        super.init(contentRect: CGRect(x: 0, y: 0, width: 560, height: 530), styleMask: [.titled, .closable], backing: .buffered, defer: false)
        title = L.t("Cometsnap Settings")
        appearance = NSAppearance(named: .darkAqua)
        backgroundColor = Theme.night
        isReleasedWhenClosed = false
        center()
        build()
    }

    private func build() {
        for b in [login, takeOver, copyBox, card, comet, sound, audio, celebrations, seasonal, saveBox, askBox, editorBox] {
            b.target = self
            b.action = #selector(changed)
        }
        LoupeCorner.allCases.forEach { loupe.addItem(withTitle: $0.title) }
        loupe.target = self
        loupe.action = #selector(changed)
        [8, 10, 12, 15, 20, 25].forEach { gifFps.addItem(withTitle: "\($0) fps") }
        gifFps.target = self
        gifFps.action = #selector(changed)
        language.addItem(withTitle: L.f("Same as macOS ({0})", L.systemLanguageName))
        L.languages.forEach { language.addItem(withTitle: $0.name) }
        language.target = self
        language.action = #selector(languageChanged)
        folder.textColor = Theme.muted
        folder.lineBreakMode = .byTruncatingMiddle
        folder.font = Theme.ui(12)
        format.addItems(withTitles: ["PNG", "JPG"])
        format.target = self
        format.action = #selector(changed)
        nameField.font = NSFont.monospacedSystemFont(ofSize: 12, weight: .regular)
        nameField.delegate = self
        nameField.widthAnchor.constraint(equalToConstant: 300).isActive = true
        nameExample.font = Theme.ui(11)
        nameExample.textColor = Theme.muted
        tags.addItem(withTitle: "{ }")
        FileNamePattern.tokens.forEach { tags.addItem(withTitle: $0) }
        tags.target = self
        tags.action = #selector(insertTag)
        let change = NSButton(title: L.t("Change…"), target: self, action: #selector(chooseFolder))
        let reveal = NSButton(title: L.t("Open"), target: self, action: #selector(openFolder))

        // Capture colors: one radio per theme, with a dot of its color, and a live preview.
        themeButtons = CaptureThemes.all.map { t in
            let b = NSButton(radioButtonWithTitle: CaptureLook.name(t), target: self, action: #selector(themeChanged(_:)))
            b.image = Self.dot(NSColor(hex: t.accent))
            b.imagePosition = .imageLeading
            b.font = Theme.ui(12)
            return b
        }
        let themeRow = NSStackView(views: themeButtons)
        themeRow.spacing = 10
        preview.translatesAutoresizingMaskIntoConstraints = false
        preview.widthAnchor.constraint(equalToConstant: 460).isActive = true
        preview.heightAnchor.constraint(equalToConstant: 132).isActive = true
        let themeHint = NSTextField(wrappingLabelWithString: L.t("The crosshair, frames, loupe and size tag, in the comet's colors."))
        themeHint.font = Theme.ui(11)
        themeHint.textColor = Theme.muted
        let themeLabel = NSTextField(labelWithString: L.t("Capture colors"))
        themeLabel.font = Theme.ui(12, .semibold)
        themeLabel.textColor = .white
        let themeBox = NSStackView(views: [themeLabel, themeHint, themeRow, preview])
        themeBox.orientation = .vertical
        themeBox.alignment = .leading
        themeBox.spacing = 6

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
        let keys = NSTextField(wrappingLabelWithString: L.t("In the overlay: click a window or drag · arrows nudge 1 px (⇧ 10 px) · Space moves the selection · F whole screen · W window detection · C copies the color · Esc cancels."))
        keys.font = Theme.ui(11)
        keys.textColor = Theme.muted
        keys.preferredMaxLayoutWidth = 460

        func heading(_ text: String) -> NSTextField {
            let h = NSTextField(labelWithString: text)
            h.font = Theme.ui(13, .semibold)
            h.textColor = .white
            return h
        }
        func hint(_ text: String) -> NSTextField {
            let h = NSTextField(wrappingLabelWithString: text)
            h.font = Theme.ui(11)
            h.textColor = Theme.muted
            h.preferredMaxLayoutWidth = 460
            return h
        }
        let nameRow = NSStackView(views: [nameField, tags])
        nameRow.spacing = 8
        let nameBox = NSStackView(views: [nameRow, nameExample, hint(L.t("Click a tag to add it. A / makes a subfolder."))])
        nameBox.orientation = .vertical
        nameBox.alignment = .leading
        nameBox.spacing = 4

        var hotkeyRows: [NSView] = []
        for action in HotkeyAction.allCases {
            let b = NSButton(title: "", target: self, action: #selector(recordHotkey(_:)))
            b.bezelStyle = .rounded
            b.tag = HotkeyAction.allCases.firstIndex(of: action) ?? 0
            b.widthAnchor.constraint(equalToConstant: 170).isActive = true
            hotkeyButtons[action] = b
            let reset = NSButton(title: "↺", target: self, action: #selector(resetHotkey(_:)))
            reset.bezelStyle = .rounded
            reset.tag = b.tag
            reset.toolTip = L.t("Back to default")
            let l = NSTextField(labelWithString: action.title)
            l.textColor = Theme.muted
            l.font = Theme.ui(12)
            l.widthAnchor.constraint(equalToConstant: 230).isActive = true
            let r = NSStackView(views: [l, b, reset])
            r.spacing = 8
            hotkeyRows.append(r)
        }

        var views: [NSView] = [title, login, takeOver, card, comet, sound, audio, celebrations, seasonal]
        views += [row(L.t("Language"), language), row(L.t("Loupe corner"), loupe), row(L.t("GIF smoothness"), gifFps), themeBox]
        views += [heading(L.t("Where captures go")), copyBox, saveBox, askBox, editorBox]
        views += [row(L.t("Captures folder"), folderRow), row(L.t("File name"), nameBox), row(L.t("File format"), format)]
        views.append(hint(L.t("PNG keeps every pixel sharp. JPG files are smaller.")))
        views += [heading(L.t("Keyboard shortcuts")), hint(L.t("Click a shortcut, then press the new keys. Esc cancels."))]
        views += hotkeyRows
        views.append(keys)
        let stack = NSStackView(views: views)
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 9
        stack.setCustomSpacing(16, after: title)
        stack.setCustomSpacing(16, after: seasonal)
        stack.translatesAutoresizingMaskIntoConstraints = false
        // Everything in one scrolling page, so it fits small screens too.
        let doc = FlippedView()
        doc.translatesAutoresizingMaskIntoConstraints = false
        doc.addSubview(stack)
        let scroll = NSScrollView()
        scroll.drawsBackground = false
        scroll.hasVerticalScroller = true
        scroll.autohidesScrollers = true
        scroll.documentView = doc
        contentView = scroll
        NSLayoutConstraint.activate([
            doc.topAnchor.constraint(equalTo: scroll.contentView.topAnchor),
            doc.leadingAnchor.constraint(equalTo: scroll.contentView.leadingAnchor),
            doc.widthAnchor.constraint(equalTo: scroll.contentView.widthAnchor),
            stack.topAnchor.constraint(equalTo: doc.topAnchor, constant: 18),
            stack.leadingAnchor.constraint(equalTo: doc.leadingAnchor, constant: 24),
            stack.trailingAnchor.constraint(lessThanOrEqualTo: doc.trailingAnchor, constant: -24),
            stack.bottomAnchor.constraint(equalTo: doc.bottomAnchor, constant: -18),
            keys.widthAnchor.constraint(equalToConstant: 470),
        ])
        // Fit the longest line (French and German run longer), at most most of the screen tall.
        let fit = stack.fittingSize
        let maxHeight = (NSScreen.main?.visibleFrame.height ?? 900) * 0.85
        setContentSize(NSSize(width: max(560, fit.width + 64), height: min(maxHeight, max(470, fit.height + 36))))
        center()
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
        language.selectItem(at: (L.languages.firstIndex { $0.code == s.language }).map { $0 + 1 } ?? 0)
        for (i, t) in CaptureThemes.all.enumerated() { themeButtons[i].state = t.id == s.captureTheme ? .on : .off }
        preview.theme = CaptureThemes.get(s.captureTheme)
        folder.stringValue = app.capturesDirectory.path.replacingOccurrences(of: NSHomeDirectory(), with: "~")
        saveBox.state = s.saveEveryCapture ? .on : .off
        askBox.state = s.askWhereToSave ? .on : .off
        editorBox.state = s.openEditorAfterCapture ? .on : .off
        format.selectItem(at: SnapNaming.fileExtension(for: s.imageFormat) == "jpg" ? 1 : 0)
        if nameField.currentEditor() == nil { nameField.stringValue = s.fileNamePattern }
        refreshExample()
        for (action, b) in hotkeyButtons where action != listening { b.title = s.hotkey(action).display }
    }

    private func refreshExample() {
        let s = app.settings
        let name = FileNamePattern.expand(nameField.stringValue, date: Date(), counter: app.nextNameCounter) + "." + SnapNaming.fileExtension(for: s.imageFormat)
        nameExample.stringValue = L.f("Example: {0}", name)
    }

    @objc private func insertTag() {
        let i = tags.indexOfSelectedItem
        guard i > 0 else { return }
        let token = FileNamePattern.tokens[i - 1]
        if let editor = nameField.currentEditor() {
            editor.insertText(token)
        } else {
            nameField.stringValue += token
        }
        patternChanged()
    }

    private func patternChanged() {
        let text = nameField.stringValue.trimmingCharacters(in: .whitespaces)
        app.update { $0.fileNamePattern = FileNamePattern.isUsable(text) ? text : FileNamePattern.defaultPattern }
        refreshExample()
    }

    // MARK: shortcuts

    @objc private func recordHotkey(_ sender: NSButton) {
        stopListening()
        let action = HotkeyAction.allCases[sender.tag]
        listening = action
        sender.title = L.t("Press the new keys…")
        app.pauseHotkeys() // so the current shortcut reaches this window too
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: [.keyDown]) { [weak self] event in
            guard let self, let action = self.listening else { return event }
            if event.keyCode == 53 { // Esc
                self.stopListening()
                return nil
            }
            guard let spec = HotkeyRecorder.spec(for: event) else { NSSound.beep(); return nil }
            let taken = HotkeyAction.allCases.contains { other in
                other != action && self.app.settings.hotkey(other).keyCode == spec.keyCode && self.app.settings.hotkey(other).modifiers == spec.modifiers
            }
            if taken { NSSound.beep(); return nil }
            self.app.update { $0.hotkeys[action.rawValue] = spec }
            self.stopListening()
            return nil
        }
    }

    @objc private func resetHotkey(_ sender: NSButton) {
        stopListening()
        let action = HotkeyAction.allCases[sender.tag]
        app.update { $0.hotkeys[action.rawValue] = nil }
        reload()
    }

    private func stopListening() {
        if let m = keyMonitor { NSEvent.removeMonitor(m) }
        keyMonitor = nil
        guard listening != nil else { return }
        listening = nil
        app.registerHotkeys()
        reload()
    }

    override func resignKey() {
        super.resignKey()
        stopListening()
    }

    override func close() {
        stopListening()
        super.close()
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
            s.saveEveryCapture = saveBox.state == .on
            s.askWhereToSave = askBox.state == .on
            s.openEditorAfterCapture = editorBox.state == .on
            s.imageFormat = format.indexOfSelectedItem == 1 ? "jpg" : "png"
        }
        refreshExample()
        if sound.state == .on { Sound.snap() }
    }

    /// Applies right away: the panel, cards and this window are rebuilt in the new language.
    @objc private func languageChanged() {
        let i = language.indexOfSelectedItem
        let code = i <= 0 ? "" : L.languages[i - 1].code
        guard code != app.settings.language else { return }
        app.update { $0.language = code }
        DispatchQueue.main.async { [app] in app.languageChanged() }
    }

    @objc private func themeChanged(_ sender: NSButton) {
        guard let i = themeButtons.firstIndex(of: sender) else { return }
        let id = CaptureThemes.all[i].id
        app.update { $0.captureTheme = id }
        reload()
    }

    private static func dot(_ color: NSColor) -> NSImage {
        NSImage(size: NSSize(width: 12, height: 12), flipped: false) { r in
            color.setFill()
            NSBezierPath(ovalIn: r.insetBy(dx: 0.5, dy: 0.5)).fill()
            return true
        }
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

extension SettingsWindow: NSTextFieldDelegate {
    func controlTextDidChange(_ obj: Notification) {
        guard (obj.object as? NSTextField) === nameField else { return }
        patternChanged()
    }
}

/// A small fake capture screen in the chosen colors: dimmed desktop, selection with glow, size tag and loupe.
final class ThemePreviewView: NSView {
    var theme = CaptureThemes.all[0] { didSet { needsDisplay = true } }

    override func draw(_ dirtyRect: NSRect) {
        let accent = NSColor(hex: theme.accent)
        let b = bounds
        let clip = NSBezierPath(roundedRect: b, xRadius: 10, yRadius: 10)
        clip.addClip()
        NSGradient(starting: Theme.surface, ending: Theme.night)?.draw(in: b, angle: -90)
        func window(_ r: CGRect, bar: NSColor) {
            NSColor(hex: 0x2A3C8F).setFill()
            NSBezierPath(roundedRect: r, xRadius: 4, yRadius: 4).fill()
            bar.setFill()
            NSBezierPath(roundedRect: CGRect(x: r.minX, y: r.maxY - 12, width: r.width, height: 12), xRadius: 4, yRadius: 4).fill()
        }
        window(CGRect(x: 22, y: 18, width: 180, height: 96), bar: Theme.rock)
        Theme.night.withAlphaComponent(0.55).setFill()
        b.fill()
        let sel = CGRect(x: 140, y: 14, width: 210, height: 84)
        window(sel, bar: NSColor(hex: 0x1F2559))
        // glow + frame
        accent.withAlphaComponent(0.33).setStroke()
        let glow = NSBezierPath(rect: sel.insetBy(dx: -4, dy: -4)); glow.lineWidth = 4; glow.stroke()
        accent.setStroke()
        let frame = NSBezierPath(rect: sel.insetBy(dx: -1, dy: -1)); frame.lineWidth = 2; frame.stroke()
        // size tag
        let attrs: [NSAttributedString.Key: Any] = [.font: NSFont.monospacedDigitSystemFont(ofSize: 11, weight: .bold), .foregroundColor: NSColor(hex: theme.tagText)]
        let text = "1280 × 720" as NSString
        let ts = text.size(withAttributes: attrs)
        let tag = CGRect(x: sel.minX, y: sel.maxY + 6, width: ts.width + 12, height: ts.height + 4)
        NSColor(hex: theme.tag).setFill()
        NSBezierPath(roundedRect: tag, xRadius: 4, yRadius: 4).fill()
        text.draw(at: CGPoint(x: tag.minX + 6, y: tag.minY + 2), withAttributes: attrs)
        // loupe
        let loupe = CGRect(x: b.maxX - 100, y: 10, width: 88, height: 108)
        Theme.night.withAlphaComponent(0.95).setFill()
        let lp = NSBezierPath(roundedRect: loupe, xRadius: 8, yRadius: 8); lp.fill()
        (theme.id == CaptureThemes.defaultID ? Theme.line : accent).setStroke()
        lp.lineWidth = 1.5; lp.stroke()
        let cell: CGFloat = 15
        for i in 0..<5 { for j in 0..<5 {
            let r = CGRect(x: loupe.minX + 6.5 + CGFloat(i) * cell, y: loupe.maxY - 6.5 - CGFloat(j + 1) * cell, width: cell, height: cell)
            let v = CGFloat((i * 37 + j * 53) % 40) / 255
            (i == 2 && j == 2 ? accent : NSColor(srgbRed: 0.13 + v, green: 0.19 + v, blue: 0.45 + v, alpha: 1)).setFill()
            r.fill()
        } }
        NSColor.white.setStroke()
        NSBezierPath(rect: CGRect(x: loupe.minX + 6.5 + 2 * cell, y: loupe.maxY - 6.5 - 3 * cell, width: cell, height: cell)).stroke()
        let hex = String(format: "#%06X", theme.accent) as NSString
        hex.draw(at: CGPoint(x: loupe.minX + 14, y: loupe.minY + 8), withAttributes: [.font: NSFont.monospacedSystemFont(ofSize: 11, weight: .medium), .foregroundColor: NSColor.white])
    }
}
