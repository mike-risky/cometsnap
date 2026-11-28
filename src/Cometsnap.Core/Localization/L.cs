using System.Globalization;

namespace Cometsnap.Core.Localization;

/// <summary>
/// The app's texts in English, French, German, Italian, Spanish, Simplified Chinese, Hindi and Portuguese. The English text is the key, so the
/// code stays readable and a missing translation simply shows English.
/// </summary>
public static class L
{
    /// <summary>Languages Cometsnap speaks: code and native name.</summary>
    public static readonly (string Code, string Name)[] Languages =
    {
        ("en", "English"), ("fr", "Français"), ("de", "Deutsch"), ("it", "Italiano"),
        ("es", "Español"), ("pt", "Português"), ("zh", "简体中文"), ("hi", "हिन्दी"),
    };

    private static IReadOnlyDictionary<string, string>? _table;

    /// <summary>The language in use: "en", "fr", "de", "it", "es", "pt", "zh" or "hi".</summary>
    public static string Language { get; private set; } = "en";

    /// <summary>Picks the language: the saved choice, or else Windows' display language, or else English.</summary>
    public static void Use(string? choice, CultureInfo? system = null)
    {
        var code = string.IsNullOrWhiteSpace(choice) ? FromCulture(system ?? CultureInfo.CurrentUICulture) : choice.Trim().ToLowerInvariant();
        _table = Table(code);
        Language = _table is null ? "en" : code;
    }

    /// <summary>The culture of the language in use (month names and the like).</summary>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Language == "zh" ? "zh-Hans" : Language);

    /// <summary>"fr-CH" → "fr"; anything we don't speak → "en".</summary>
    public static string FromCulture(CultureInfo culture)
    {
        for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
        {
            var two = c.TwoLetterISOLanguageName;
            if (Table(two) is not null || two == "en") return two;
        }
        return "en";
    }

    /// <summary>Translates a text.</summary>
    public static string T(string english) =>
        _table is not null && _table.TryGetValue(english, out var t) && !string.IsNullOrEmpty(t) ? t : english;

    /// <summary>Translates a text with {0}, {1}… placeholders, then fills them in.</summary>
    public static string F(string english, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(english), args);

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Merged = new();

    /// <summary>The translation table of a language (null for English or an unknown code).</summary>
    public static IReadOnlyDictionary<string, string>? Table(string code)
    {
        lock (Merged)
        {
            if (Merged.TryGetValue(code, out var cached)) return cached;
            var main = MainTable(code);
            if (main is null) return null;
            // The update texts live in UpdateStrings.cs; the main table wins on a duplicate.
            var all = new Dictionary<string, string>(main);
            if (UpdateStrings.Table(code) is { } updates)
                foreach (var (k, v) in updates) all.TryAdd(k, v);
            return Merged[code] = all;
        }
    }

    private static IReadOnlyDictionary<string, string>? MainTable(string code) => code switch
    {
        "fr" => Strings.French,
        "de" => Strings.German,
        "it" => Strings.Italian,
        "es" => Strings.Spanish,
        "pt" => Strings.Portuguese,
        "zh" => Strings.Chinese,
        "hi" => Strings.Hindi,
        _ => null,
    };
}
