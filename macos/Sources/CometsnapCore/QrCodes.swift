import Foundation

/// What a scanned code holds, so the result can offer the right button.
public enum CodeKind: Equatable { case text, link, wifi, email, phone }

/// A Wi-Fi network from a "WIFI:S:name;T:WPA;P:secret;;" code.
public struct WifiNetwork: Equatable {
    public let name: String
    public let password: String?
    public let security: String
    public let hidden: Bool
}

/// One QR code (or barcode) read from the screen. Same rules as Windows (src/Cometsnap.Core/Text/QrCodes.cs).
public struct ScannedCode: Equatable {
    public let text: String
    /// "QR code", "EAN-13"...
    public let format: String

    public init(text: String, format: String) {
        self.text = text
        self.format = format
    }

    public var kind: CodeKind { QrCodes.kind(of: text) }
    /// The web address behind "Open link" (http/https only), or nil.
    public var link: URL? { QrCodes.openableLink(text) }
    public var wifi: WifiNetwork? { QrCodes.parseWifi(text) }
    /// What goes on the clipboard: the Wi-Fi password for a Wi-Fi code, else the whole text.
    public var clipboardText: String {
        if let p = wifi?.password, !p.isEmpty { return p }
        return text
    }
}

public enum QrCodes {
    public static func kind(of text: String) -> CodeKind {
        if openableLink(text) != nil { return .link }
        if parseWifi(text) != nil { return .wifi }
        let t = text.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        if t.hasPrefix("mailto:") || t.hasPrefix("matmsg:") { return .email }
        if t.hasPrefix("tel:") { return .phone }
        return .text
    }

    /// Only plain http/https web addresses can be opened, never other schemes (file:, javascript:,
    /// app links...). Nothing is ever opened without a click.
    public static func openableLink(_ text: String) -> URL? {
        let t = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !t.isEmpty, t.count <= 4096, t.rangeOfCharacter(from: .whitespacesAndNewlines) == nil,
              let comps = URLComponents(string: t), let scheme = comps.scheme?.lowercased(),
              scheme == "http" || scheme == "https", let host = comps.host, !host.isEmpty,
              let url = comps.url else { return nil }
        return url
    }

    /// The web site's name, shown next to "Open link" so you see where it goes.
    public static func linkHost(_ text: String) -> String? { openableLink(text)?.host?.lowercased() }

    /// Reads a Wi-Fi code ("WIFI:T:WPA;S:name;P:secret;H:false;;"), or nil.
    public static func parseWifi(_ text: String) -> WifiNetwork? {
        let t = Array(text.trimmingCharacters(in: .whitespacesAndNewlines))
        guard t.count >= 5, String(t[0..<5]).uppercased() == "WIFI:" else { return nil }
        var fields: [String: String] = [:]
        var key = "", value = "", inValue = false
        var i = 5
        while i < t.count {
            let c = t[i]
            if c == "\\", i + 1 < t.count {
                i += 1
                if inValue { value.append(t[i]) } else { key.append(t[i]) }
            } else if !inValue, c == ":" {
                inValue = true
            } else if c == ";" {
                let k = key.trimmingCharacters(in: .whitespaces).uppercased()
                if !k.isEmpty { fields[k] = value }
                key = ""; value = ""; inValue = false
            } else if inValue {
                value.append(c)
            } else {
                key.append(c)
            }
            i += 1
        }
        let last = key.trimmingCharacters(in: .whitespaces).uppercased()
        if !last.isEmpty, inValue { fields[last] = value }
        guard let name = fields["S"], !name.isEmpty else { return nil }
        let password = fields["P"].flatMap { $0.isEmpty ? nil : $0 }
        let security = fields["T"].flatMap { $0.isEmpty ? nil : $0 } ?? "nopass"
        return WifiNetwork(name: name, password: password, security: security, hidden: fields["H"]?.lowercased() == "true")
    }
}
