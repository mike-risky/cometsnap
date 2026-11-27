import AppKit
import CometsnapCore

/// Opened straight from the disk image (or from Downloads), macOS runs Cometsnap from a hidden
/// read-only copy: the screen permission doesn't stick and "start at login" breaks. This offers,
/// once per launch, to move it into Applications and reopen it from there.
enum MoveToApplications {
    static var isInApplications: Bool {
        let path = Bundle.main.bundlePath
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        return path.hasPrefix("/Applications/") || path.hasPrefix(home + "/Applications/")
    }

    /// True when Cometsnap is moving (the caller should stop launching).
    static func offerIfNeeded() -> Bool {
        guard !isInApplications else { return false }
        NSApp.activate()
        let a = NSAlert()
        a.icon = Theme.logo
        a.messageText = L.t("Move Cometsnap to Applications?")
        a.informativeText = L.t("Cometsnap works best from your Applications folder: macOS remembers its screen permission there, and it can start at login.")
        a.addButton(withTitle: L.t("Move to Applications"))
        a.addButton(withTitle: L.t("Not now"))
        guard a.runModal() == .alertFirstButtonReturn else { return false }
        if move(to: URL(fileURLWithPath: "/Applications")) { return true }
        let mine = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Applications")
        try? FileManager.default.createDirectory(at: mine, withIntermediateDirectories: true)
        if move(to: mine) { return true }
        let e = NSAlert()
        e.messageText = L.t("Could not move Cometsnap to Applications.")
        e.informativeText = L.t("Drag Cometsnap into the Applications folder yourself, then open it from there.")
        e.runModal()
        return false
    }

    private static func move(to folder: URL) -> Bool {
        let fm = FileManager.default
        let source = URL(fileURLWithPath: Bundle.main.bundlePath)
        let target = folder.appendingPathComponent("Cometsnap.app")
        // Another Cometsnap already in Applications (an older version, maybe running) steps aside.
        for other in NSRunningApplication.runningApplications(withBundleIdentifier: Bundle.main.bundleIdentifier ?? "")
            where other.processIdentifier != ProcessInfo.processInfo.processIdentifier {
            other.terminate()
        }
        let staging = folder.appendingPathComponent(".Cometsnap-\(UUID().uuidString).app")
        do {
            try fm.copyItem(at: source, to: staging)
            if fm.fileExists(atPath: target.path) { _ = try fm.replaceItemAt(target, withItemAt: staging) }
            else { try fm.moveItem(at: staging, to: target) }
        } catch {
            try? fm.removeItem(at: staging)
            Log.info("move to \(folder.path) failed: \(error.localizedDescription)")
            return false
        }
        // The copy is ours now: no second "downloaded from the internet" question.
        run("/usr/bin/xattr", ["-dr", "com.apple.quarantine", target.path])
        Log.info("moved to \(target.path)")
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/sh")
        p.arguments = ["-c", "sleep 1; /usr/bin/open \"\(target.path)\""]
        try? p.run()
        // Leaves the disk image so it can be ejected.
        if source.path.hasPrefix("/Volumes/") {
            let volume = "/Volumes/" + source.path.dropFirst("/Volumes/".count).split(separator: "/").first.map(String.init)!
            let d = Process()
            d.executableURL = URL(fileURLWithPath: "/bin/sh")
            d.arguments = ["-c", "sleep 3; /usr/bin/hdiutil detach \"\(volume)\" -quiet"]
            try? d.run()
        }
        NSApp.terminate(nil)
        return true
    }

    private static func run(_ path: String, _ args: [String]) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: path)
        p.arguments = args
        try? p.run()
        p.waitUntilExit()
    }
}
