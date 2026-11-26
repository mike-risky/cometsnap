import Foundation

/// The app's texts in English, French, German and Italian. The English text is the key, so the
/// code stays readable and a missing translation simply shows English. Same scheme and wording
/// as the Windows app.
public enum L {
    /// Languages Cometsnap speaks: code and native name.
    public static let languages: [(code: String, name: String)] = [
        ("en", "English"), ("fr", "Français"), ("de", "Deutsch"), ("it", "Italiano"),
    ]

    private static var table: [String: String]?

    /// The language in use: "en", "fr", "de" or "it".
    public private(set) static var language = "en"

    /// Picks the language: the saved choice, or else the Mac's preferred languages, or else English.
    public static func use(_ choice: String?, preferred: [String] = Locale.preferredLanguages) {
        let wanted = choice?.trimmingCharacters(in: .whitespaces).lowercased() ?? ""
        let code = wanted.isEmpty ? fromPreferred(preferred) : wanted
        table = Strings.table(code)
        language = table == nil ? "en" : code
    }

    /// The first of the Mac's preferred languages that Cometsnap speaks ("fr-CH" → "fr").
    public static func fromPreferred(_ list: [String]) -> String {
        for id in list {
            let two = String(id.lowercased().prefix(while: { $0 != "-" && $0 != "_" }))
            if two == "en" || Strings.table(two) != nil { return two }
        }
        return "en"
    }

    /// The native name of the Mac's own language, for "Same as macOS (Français)".
    public static var systemLanguageName: String {
        let code = fromPreferred(Locale.preferredLanguages)
        return languages.first { $0.code == code }?.name ?? "English"
    }

    /// Month names and numbers in the language in use.
    public static var locale: Locale { Locale(identifier: language) }

    /// Translates a text.
    public static func t(_ english: String) -> String {
        if let t = table?[english], !t.isEmpty { return t }
        return english
    }

    /// Translates a text with {0}, {1}… placeholders, then fills them in.
    public static func f(_ english: String, _ args: Any...) -> String {
        var s = t(english)
        for (i, a) in args.enumerated() { s = s.replacingOccurrences(of: "{\(i)}", with: "\(a)") }
        return s
    }
}
