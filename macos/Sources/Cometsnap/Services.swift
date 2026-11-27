import AppKit
import Carbon.HIToolbox
import CometsnapCore
import ImageIO
import ServiceManagement
import UniformTypeIdentifiers

// MARK: - Log

enum Log {
    private static let queue = DispatchQueue(label: "cometsnap.log")
    static var file: URL?

    static func info(_ message: String) {
        let line = "\(ISO8601DateFormatter().string(from: Date())) \(message)\n"
        #if DEBUG
        print(line, terminator: "")
        #endif
        guard let file else { return }
        queue.async {
            if let h = try? FileHandle(forWritingTo: file) {
                h.seekToEndOfFile()
                h.write(Data(line.utf8))
                try? h.close()
            } else {
                try? Data(line.utf8).write(to: file)
            }
        }
    }
}

// MARK: - Global hotkeys (Carbon: works without Accessibility permission)

struct Hotkey: Equatable {
    let keyCode: UInt32
    let modifiers: UInt32
    let display: String

    static let captureCmdShift2 = Hotkey(keyCode: UInt32(kVK_ANSI_2), modifiers: UInt32(cmdKey | shiftKey), display: "⌘⇧2")
    static let captureCmdShift4 = Hotkey(keyCode: UInt32(kVK_ANSI_4), modifiers: UInt32(cmdKey | shiftKey), display: "⌘⇧4")
    /// Print Screen on a PC keyboard plugged into a Mac arrives as F13.
    static let captureF13 = Hotkey(keyCode: UInt32(kVK_F13), modifiers: 0, display: "F13 / Print Screen")
    static let fullscreenCmdShift3 = Hotkey(keyCode: UInt32(kVK_ANSI_3), modifiers: UInt32(cmdKey | shiftKey), display: "⌘⇧3")
    static let panelCmdShift5 = Hotkey(keyCode: UInt32(kVK_ANSI_5), modifiers: UInt32(cmdKey | shiftKey), display: "⌘⇧5")
    static let fullscreenCmdShift3Alt = Hotkey(keyCode: UInt32(kVK_ANSI_3), modifiers: UInt32(cmdKey | shiftKey | optionKey), display: "⌥⌘⇧3")
    static let videoCmdShift7 = Hotkey(keyCode: UInt32(kVK_ANSI_7), modifiers: UInt32(cmdKey | shiftKey), display: "⌘⇧7")
    static let gifCmdShift8 = Hotkey(keyCode: UInt32(kVK_ANSI_8), modifiers: UInt32(cmdKey | shiftKey), display: "⌘⇧8")
    static let videoShiftF13 = Hotkey(keyCode: UInt32(kVK_F13), modifiers: UInt32(shiftKey), display: "⇧F13")
    static let textCmdShift9 = Hotkey(keyCode: UInt32(kVK_ANSI_9), modifiers: UInt32(cmdKey | shiftKey), display: "⌘⇧9")
    static let scrollingCmdShift0 = Hotkey(keyCode: UInt32(kVK_ANSI_0), modifiers: UInt32(cmdKey | shiftKey), display: "⌘⇧0")
    static let gifCtrlShiftF13 = Hotkey(keyCode: UInt32(kVK_F13), modifiers: UInt32(controlKey | shiftKey), display: "⌃⇧F13")
}

final class HotkeyCenter {
    static let shared = HotkeyCenter()
    private var handlers: [UInt32: () -> Void] = [:]
    private var refs: [UInt32: EventHotKeyRef] = [:]
    private var nextID: UInt32 = 1
    private var installed = false

    private func installHandler() {
        guard !installed else { return }
        installed = true
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, event, _ -> OSStatus in
            var hk = EventHotKeyID()
            let status = GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                                           nil, MemoryLayout<EventHotKeyID>.size, nil, &hk)
            if status == noErr {
                let id = hk.id
                DispatchQueue.main.async { HotkeyCenter.shared.fire(id) }
            }
            return noErr
        }, 1, &spec, nil, nil)
    }

    @discardableResult
    func register(_ key: Hotkey, action: @escaping () -> Void) -> UInt32? {
        installHandler()
        var ref: EventHotKeyRef?
        let id = nextID
        nextID += 1
        let status = RegisterEventHotKey(key.keyCode, key.modifiers, EventHotKeyID(signature: OSType(0x434D_5350), id: id),
                                         GetApplicationEventTarget(), 0, &ref)
        guard status == noErr, let ref else {
            Log.info("hotkey \(key.display) not available (status \(status))")
            return nil
        }
        refs[id] = ref
        handlers[id] = action
        return id
    }

    func unregisterAll() {
        for (_, r) in refs { UnregisterEventHotKey(r) }
        refs.removeAll()
        handlers.removeAll()
    }

    fileprivate func fire(_ id: UInt32) { handlers[id]?() }
}

// MARK: - macOS screenshot shortcut takeover

/// Optionally hands the macOS screenshot shortcuts (⌘⇧3, ⌘⇧4, ⌘⇧5 and their ⌃ copy variants)
/// to Cometsnap, the Mac version of "Replace your old screen capture tool". Fully reversible.
///
/// Two layers: the live switch through the window server (takes effect at once, lasts until
/// logout), and the saved preference in com.apple.symbolichotkeys (what macOS reads at login and
/// what System Settings › Keyboard › Shortcuts shows). Cometsnap applies both at every launch.
enum SystemShortcuts {
    /// IDs in com.apple.symbolichotkeys, with their default key (ASCII, key code, modifiers).
    private static let entries: [(id: Int, ascii: Int, key: Int, modifiers: Int)] = [
        (28, 51, 20, 1_179_648),  // ⌘⇧3 save screen
        (29, 51, 20, 1_441_792),  // ⌃⌘⇧3 copy screen
        (30, 52, 21, 1_179_648),  // ⌘⇧4 save area
        (31, 52, 21, 1_441_792),  // ⌃⌘⇧4 copy area
        (184, 53, 23, 1_179_648), // ⌘⇧5 screenshot and recording options
    ]

    private typealias SetEnabled = @convention(c) (Int32, Bool) -> Int32
    private typealias IsEnabled = @convention(c) (Int32) -> Bool
    private static let handle = dlopen(nil, RTLD_NOW)
    private static let setEnabledFn: SetEnabled? = dlsym(handle, "CGSSetSymbolicHotKeyEnabled").map { unsafeBitCast($0, to: SetEnabled.self) }
    private static let isEnabledFn: IsEnabled? = dlsym(handle, "CGSIsSymbolicHotKeyEnabled").map { unsafeBitCast($0, to: IsEnabled.self) }

    /// True when macOS's own ⌘⇧4 is switched off right now.
    static var builtInRegionShortcutIsOff: Bool { isEnabledFn.map { !$0(30) } ?? false }

    static func setBuiltInShortcuts(enabled: Bool) {
        // Live: the window server stops (or starts) handling these keys at once.
        if let f = setEnabledFn {
            for e in entries { _ = f(Int32(e.id), enabled) }
        }
        // Saved: survives logout, and shows in System Settings.
        for e in entries {
            let dict = "{ enabled = \(enabled ? 1 : 0); value = { parameters = (\(e.ascii), \(e.key), \(e.modifiers)); type = standard; }; }"
            run("/usr/bin/defaults", ["write", "com.apple.symbolichotkeys", "AppleSymbolicHotKeys", "-dict-add", "\(e.id)", dict])
        }
        run("/System/Library/PrivateFrameworks/SystemAdministration.framework/Resources/activateSettings", ["-u"])
        Log.info("macOS screenshot shortcuts \(enabled ? "restored" : "handed to Cometsnap") (live switch \(setEnabledFn == nil ? "missing" : "ok"), now \(builtInRegionShortcutIsOff ? "off" : "on"))")
    }

    @discardableResult
    private static func run(_ path: String, _ args: [String]) -> Int32 {
        guard FileManager.default.isExecutableFile(atPath: path) else { return -1 }
        let p = Process()
        p.executableURL = URL(fileURLWithPath: path)
        p.arguments = args
        do {
            try p.run()
            p.waitUntilExit()
            return p.terminationStatus
        } catch {
            return -1
        }
    }
}

// MARK: - Start at login

enum LoginItem {
    static var isEnabled: Bool { SMAppService.mainApp.status == .enabled }

    static func set(_ on: Bool) {
        do {
            if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
        } catch {
            Log.info("login item \(on ? "register" : "unregister") failed: \(error.localizedDescription)")
        }
    }
}

// MARK: - Clipboard and files

enum Clipboard {
    /// Puts the capture on the clipboard as PNG and TIFF, sized in points so it pastes at the
    /// right size on Retina screens.
    static func copy(_ image: CGImage, scale: CGFloat) {
        let pb = NSPasteboard.general
        pb.clearContents()
        let item = NSPasteboardItem()
        if let png = PNG.data(image) { item.setData(png, forType: .png) }
        let rep = NSBitmapImageRep(cgImage: image)
        rep.size = NSSize(width: CGFloat(image.width) / scale, height: CGFloat(image.height) / scale)
        if let tiff = rep.tiffRepresentation { item.setData(tiff, forType: .tiff) }
        pb.writeObjects([item])
    }

    static func copyFile(_ url: URL) {
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.writeObjects([url as NSURL])
    }

    static func copyText(_ s: String) {
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.setString(s, forType: .string)
    }
}

enum PNG {
    static func data(_ image: CGImage) -> Data? {
        let data = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(data, UTType.png.identifier as CFString, 1, nil) else { return nil }
        CGImageDestinationAddImage(dest, image, nil)
        guard CGImageDestinationFinalize(dest) else { return nil }
        return data as Data
    }

    static func write(_ image: CGImage, to url: URL) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        guard let d = data(image) else { throw CocoaError(.fileWriteUnknown) }
        try d.write(to: url, options: .atomic)
    }

    static func load(_ url: URL) -> CGImage? {
        guard let src = CGImageSourceCreateWithURL(url as CFURL, nil) else { return nil }
        return CGImageSourceCreateImageAtIndex(src, 0, nil)
    }
}

enum Sound {
    static func snap() { NSSound(named: "Pop")?.play() }
}
