import Foundation

/// Scan a QR code: tells whether what a code holds is a web link worth an "Open link" button.
/// Links are never opened on their own; only http and https get the button.
public enum CodeLinks {
    public static func linkOf(_ text: String?) -> String? {
        guard var t = text?.trimmingCharacters(in: .whitespacesAndNewlines), !t.isEmpty,
              t.rangeOfCharacter(from: .whitespacesAndNewlines) == nil else { return nil }
        if t.lowercased().hasPrefix("www.") { t = "https://" + t }
        guard let url = URL(string: t), let scheme = url.scheme?.lowercased(), scheme == "http" || scheme == "https",
              let host = url.host, !host.isEmpty else { return nil }
        return url.absoluteString
    }
}
