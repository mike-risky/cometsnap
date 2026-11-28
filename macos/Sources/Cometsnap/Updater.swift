import AppKit
import CometsnapCore
import CryptoKit

/// Once a day, asks GitHub Releases whether a newer Cometsnap is out and shows the update card.
/// Update now downloads the new .dmg (checked against SHA256SUMS-macOS.txt), opens it for the user
/// to drag into Applications, and quits so the new copy can take this one's place.
/// Nothing is downloaded or opened without a click.
final class Updater {
    enum Result { case upToDate, offered, unreachable }

    private unowned let app: AppController
    private let prefsURL: URL
    private(set) var prefs: UpdatePrefs
    private var timer: Timer?
    private var checking = false
    private var offer: (release: ReleaseInfo, notes: [String])?
    private lazy var card = UpdateCard()

    static var current: AppVersion { AppVersion(Bundle.main.shortVersion) ?? AppVersion(0, 0, 0) }

    private static let session: URLSession = {
        let c = URLSessionConfiguration.default
        c.timeoutIntervalForRequest = 30
        c.httpAdditionalHeaders = ["User-Agent": "Cometsnap/\(Updater.current)"]
        return URLSession(configuration: c)
    }()

    init(app: AppController) {
        self.app = app
        prefsURL = app.paths.dataDirectory.appendingPathComponent("updates.json")
        prefs = UpdatePrefs.load(from: prefsURL)
    }

    /// First look two minutes after launch (never in the way of the first capture), then hourly to see if a day has passed.
    func start() {
        DispatchQueue.main.asyncAfter(deadline: .now() + 120) { [weak self] in
            self?.tick()
            self?.timer = Timer.scheduledTimer(withTimeInterval: 3600, repeats: true) { [weak self] _ in self?.tick() }
        }
    }

    private func tick() {
        if prefs.isDue(now: Date(), current: Self.current) { check(manual: false) { _ in } }
    }

    func setAutomatic(_ on: Bool) {
        prefs.checkAutomatically = on
        prefs.save(to: prefsURL)
    }

    /// Asks GitHub; shows the card when there is a newer version. Settings' "Check now" passes manual.
    func check(manual: Bool, done: @escaping (Result) -> Void) {
        guard !checking else { return done(.upToDate) }
        checking = true
        prefs.lastCheck = Date()
        prefs.save(to: prefsURL)
        var request = URLRequest(url: UpdateLinks.latestAPI)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        let skipped = prefs.skippedVersion
        let language = L.language
        Self.session.dataTask(with: request) { [weak self] data, response, error in
            let ok = (response as? HTTPURLResponse)?.statusCode == 200
            guard let self else { return }
            guard ok, let data, error == nil else {
                Log.info("Update check failed: \(error?.localizedDescription ?? "HTTP \((response as? HTTPURLResponse)?.statusCode ?? 0)")")
                return DispatchQueue.main.async { self.checking = false; done(.unreachable) }
            }
            let release = ReleaseInfo.parse(data)
            // Until the release carries the Mac files (CI still uploading), it counts as not out yet.
            guard ReleaseInfo.isOffered(release, current: Self.current, skipped: skipped, manual: manual), let release,
                  release.asset("Cometsnap-macOS.dmg") != nil else {
                Log.info("Update check: \(Self.current) is up to date")
                return DispatchQueue.main.async { self.checking = false; done(.upToDate) }
            }
            self.notes(for: release, language: language) { notes in
                DispatchQueue.main.async {
                    self.checking = false
                    Log.info("Update check: \(release.version) is out (\(notes.count) notes)")
                    self.show(release, notes)
                    done(.offered)
                }
            }
        }.resume()
    }

    /// What the release brings: whats-new.json at its tag, else its CHANGELOG.md section.
    private func notes(for release: ReleaseInfo, language: String, done: @escaping ([String]) -> Void) {
        let cur = Self.current
        var from = cur.isDevBuild ? AppVersion(release.version.major, release.version.minor, max(0, release.version.patch - 1)) : cur
        if from >= release.version { from = AppVersion(0, 0, 0) }
        let start = from
        func get(_ path: String, _ next: @escaping (Data?) -> Void) {
            guard let url = UpdateLinks.raw(release.tag, path) else { return next(nil) }
            Self.session.dataTask(with: url) { data, response, _ in
                next((response as? HTTPURLResponse)?.statusCode == 200 ? data : nil)
            }.resume()
        }
        get("updates/whats-new.json") { data in
            if let data {
                let lines = WhatsNew.fromJSON(data, current: start, target: release.version, language: language)
                if !lines.isEmpty { return done(lines) }
            }
            get("CHANGELOG.md") { data in
                let md = data.flatMap { String(data: $0, encoding: .utf8) } ?? ""
                done(WhatsNew.fromChangelog(md, current: start, target: release.version))
            }
        }
    }

    private func show(_ release: ReleaseInfo, _ notes: [String]) {
        offer = (release, notes)
        card.onUpdate = { [weak self] in self?.updateNow() }
        card.onLater = { [weak self] in self?.card.orderOut(nil) }
        card.onSkip = { [weak self] in
            guard let self, let o = self.offer else { return }
            self.prefs.skippedVersion = o.release.version.description
            self.prefs.save(to: self.prefsURL)
            self.card.orderOut(nil)
        }
        card.onEverything = { _ = NSWorkspace.shared.open(UpdateLinks.changelog(L.language)) }
        card.show(version: release.version, notes: notes, note: L.t("Cometsnap opens the new version: drag it into Applications and choose Replace."))
    }

    private func updateNow() {
        guard let release = offer?.release else { return }
        guard let dmg = release.asset("Cometsnap-macOS.dmg") else {
            if let page = release.pageURL { NSWorkspace.shared.open(page) }
            card.orderOut(nil)
            return
        }
        card.setBusy(true, L.f("Downloading… {0}%", 0))
        let downloads = FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first ?? FileManager.default.temporaryDirectory
        let target = downloads.appendingPathComponent("Cometsnap-\(release.version).dmg")
        let task = Self.session.downloadTask(with: dmg.url) { [weak self] tmp, response, error in
            guard let self else { return }
            func fail(_ why: String) {
                Log.info("Downloading the update failed: \(why)")
                DispatchQueue.main.async { self.card.setBusy(false, L.t("The update could not be downloaded. Try again later.")) }
            }
            guard let tmp, error == nil, (response as? HTTPURLResponse)?.statusCode == 200 else { return fail(error?.localizedDescription ?? "HTTP error") }
            do {
                try? FileManager.default.removeItem(at: target)
                try FileManager.default.moveItem(at: tmp, to: target)
            } catch { return fail(error.localizedDescription) }
            self.verify(target, release) { ok in
                guard ok else {
                    try? FileManager.default.removeItem(at: target)
                    return fail("checksum mismatch")
                }
                DispatchQueue.main.async { self.openAndQuit(target) }
            }
        }
        progressObservation = task.progress.observe(\.fractionCompleted) { [weak self] p, _ in
            let percent = Int((p.fractionCompleted * 100).rounded())
            DispatchQueue.main.async { self?.card.setStatus(L.f("Downloading… {0}%", percent)) }
        }
        task.resume()
    }

    private var progressObservation: NSKeyValueObservation?

    /// Checks the file against SHA256SUMS-macOS.txt when the release has one.
    private func verify(_ file: URL, _ release: ReleaseInfo, done: @escaping (Bool) -> Void) {
        guard let sums = release.asset("SHA256SUMS-macOS.txt") else { return done(true) }
        Self.session.dataTask(with: sums.url) { data, _, _ in
            guard let data, let text = String(data: data, encoding: .utf8),
                  let expected = Checksums.parse(text)[file.lastPathComponent.lowercased()] ?? Checksums.parse(text)["cometsnap-macos.dmg"] else {
                return done(true)
            }
            guard let bytes = try? Data(contentsOf: file, options: .mappedIfSafe) else { return done(false) }
            let hash = SHA256.hash(data: bytes).map { String(format: "%02x", $0) }.joined()
            done(hash == expected)
        }.resume()
    }

    /// Opens the disk image (Finder shows Cometsnap and Applications) and steps aside for the new copy.
    private func openAndQuit(_ dmg: URL) {
        progressObservation = nil
        card.setBusy(true, L.t("Drag Cometsnap into Applications and choose Replace.") + "\n" +
                     L.t("If macOS won't open the new Cometsnap, use the Terminal line from the download page."))
        NSWorkspace.shared.open(dmg)
        Log.info("Opened \(dmg.lastPathComponent); quitting so it can be replaced")
        DispatchQueue.main.asyncAfter(deadline: .now() + 4) { NSApp.terminate(nil) }
    }
}

/// "Cometsnap 1.4.0 is here": a small panel in the corner with what's new, Update now / Later and Skip this version.
final class UpdateCard: NSPanel {
    var onUpdate: (() -> Void)?
    var onLater: (() -> Void)?
    var onSkip: (() -> Void)?
    var onEverything: (() -> Void)?

    private let titleLabel = NSTextField(labelWithString: "")
    private let notesStack = NSStackView()
    private let statusLabel = NSTextField(wrappingLabelWithString: "")
    private let updateButton = NSButton(title: "", target: nil, action: nil)
    private let laterButton = NSButton(title: "", target: nil, action: nil)
    private let skipButton = NSButton(title: "", target: nil, action: nil)
    private var busy = false

    init() {
        super.init(contentRect: CGRect(x: 0, y: 0, width: 380, height: 200), styleMask: [.borderless, .nonactivatingPanel],
                   backing: .buffered, defer: false)
        isFloatingPanel = true
        level = .statusBar
        backgroundColor = .clear
        isOpaque = false
        hasShadow = true
        isReleasedWhenClosed = false
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle]
    }

    override var canBecomeKey: Bool { true }

    private func build() {
        let root = NSView()
        root.wantsLayer = true
        root.layer?.backgroundColor = Theme.night.withAlphaComponent(0.97).cgColor
        root.layer?.cornerRadius = 14
        root.layer?.borderWidth = 1
        root.layer?.borderColor = Theme.line.cgColor

        let logo = NSImageView(image: Theme.logo)
        logo.translatesAutoresizingMaskIntoConstraints = false
        logo.widthAnchor.constraint(equalToConstant: 32).isActive = true
        logo.heightAnchor.constraint(equalToConstant: 32).isActive = true

        titleLabel.font = Theme.heading(15)
        titleLabel.textColor = Theme.yellow
        titleLabel.lineBreakMode = .byWordWrapping
        titleLabel.maximumNumberOfLines = 2
        titleLabel.preferredMaxLayoutWidth = 290
        let close = NSButton(title: "✕", target: self, action: #selector(later))
        close.isBordered = false
        close.contentTintColor = Theme.muted
        close.toolTip = L.t("Later")
        let head = NSStackView(views: [titleLabel, close])
        head.alignment = .top

        notesStack.orientation = .vertical
        notesStack.alignment = .leading
        notesStack.spacing = 4

        let everything = NSButton(title: L.t("See everything new"), target: self, action: #selector(everythingNew))
        everything.isBordered = false
        everything.attributedTitle = NSAttributedString(string: L.t("See everything new"), attributes: [
            .font: Theme.ui(12), .foregroundColor: Theme.muted, .underlineStyle: NSUnderlineStyle.single.rawValue,
        ])

        updateButton.target = self
        updateButton.action = #selector(updateTapped)
        updateButton.bezelStyle = .rounded
        updateButton.keyEquivalent = "\r"
        updateButton.bezelColor = Theme.terracotta
        laterButton.target = self
        laterButton.action = #selector(later)
        laterButton.bezelStyle = .rounded
        skipButton.target = self
        skipButton.action = #selector(skip)
        skipButton.isBordered = false
        let buttons = NSStackView(views: [updateButton, laterButton, skipButton])
        buttons.spacing = 8

        statusLabel.font = Theme.ui(11)
        statusLabel.textColor = Theme.muted
        statusLabel.preferredMaxLayoutWidth = 290

        let body = NSStackView(views: [head, notesStack, everything, buttons, statusLabel])
        body.orientation = .vertical
        body.alignment = .leading
        body.spacing = 8
        let row = NSStackView(views: [logo, body])
        row.alignment = .top
        row.spacing = 12
        row.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(row)
        NSLayoutConstraint.activate([
            row.topAnchor.constraint(equalTo: root.topAnchor, constant: 14),
            row.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 14),
            row.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -14),
            row.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -14),
            root.widthAnchor.constraint(equalToConstant: 380),
        ])
        contentView = root
    }

    func show(version: AppVersion, notes: [String], note: String?) {
        if contentView?.subviews.isEmpty ?? true { build() }
        titleLabel.stringValue = L.f("Cometsnap {0} is here", version.description).uppercased(with: L.locale)
        notesStack.arrangedSubviews.forEach { $0.removeFromSuperview() }
        for line in notes.isEmpty ? [L.t("Fixes and improvements.")] : notes {
            let star = NSTextField(labelWithString: "✦")
            star.textColor = CaptureLook.accent
            star.font = Theme.ui(11)
            let text = NSTextField(wrappingLabelWithString: line)
            text.font = Theme.ui(13)
            text.textColor = .white
            text.preferredMaxLayoutWidth = 270
            let r = NSStackView(views: [star, text])
            r.alignment = .firstBaseline
            r.spacing = 6
            notesStack.addArrangedSubview(r)
        }
        updateButton.title = L.t("Update now")
        laterButton.title = L.t("Later")
        skipButton.attributedTitle = NSAttributedString(string: L.t("Skip this version"), attributes: [.font: Theme.ui(12), .foregroundColor: Theme.muted])
        setBusy(false, note)
        orderFrontRegardless()
    }

    func setBusy(_ on: Bool, _ status: String?) {
        busy = on
        updateButton.isEnabled = !on
        laterButton.isEnabled = !on
        skipButton.isHidden = on
        setStatus(status)
    }

    func setStatus(_ text: String?) {
        statusLabel.stringValue = text ?? ""
        statusLabel.isHidden = (text ?? "").isEmpty
        place()
    }

    private func place() {
        guard let content = contentView else { return }
        content.layoutSubtreeIfNeeded()
        let size = content.fittingSize
        let screen = NSScreen.main?.visibleFrame ?? CGRect(x: 0, y: 0, width: 1440, height: 900)
        setFrame(CGRect(x: screen.maxX - size.width - 12, y: screen.minY + 12, width: size.width, height: size.height), display: true)
    }

    @objc private func updateTapped() { if !busy { onUpdate?() } }
    @objc private func later() { if !busy { onLater?() } }
    @objc private func skip() { if !busy { onSkip?() } }
    @objc private func everythingNew() { onEverything?() }
}
