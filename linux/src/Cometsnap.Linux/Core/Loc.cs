using System.Globalization;
using Cometsnap.Core.Localization;

namespace Cometsnap.Linux.Core;

/// <summary>
/// The app's texts in English, French, German and Italian. The English text is the key. The wording
/// is shared with the Windows app (L, Strings.cs); texts only Linux has live in LinuxStrings.cs.
/// </summary>
public static class Loc
{
    /// <summary>The language in use: "en", "fr", "de", "it", "es", "pt", "zh" or "hi".</summary>
    public static string Language => L.Language;

    /// <summary>The culture of the language in use (month names and the like).</summary>
    public static CultureInfo Culture => L.Culture;

    public static (string Code, string Name)[] Languages => L.Languages;

    /// <summary>Picks the language: the saved choice, or else the desktop's language, or else English.</summary>
    public static void Use(string? choice) => L.Use(choice, SystemCulture());

    /// <summary>Translates a text.</summary>
    public static string T(string english)
    {
        if (Language != "en" && LinuxStrings.Table(Language) is { } own && own.TryGetValue(english, out var t) && !string.IsNullOrEmpty(t)) return t;
        return L.T(english);
    }

    /// <summary>Translates a text with {0}, {1}… placeholders, then fills them in.</summary>
    public static string F(string english, params object?[] args) => string.Format(Culture, T(english), args);

    /// <summary>
    /// The desktop's language from LANGUAGE, LC_ALL, LC_MESSAGES or LANG ("fr_CH.UTF-8" → fr-CH).
    /// LANGUAGE can list several ("de:en"): the first one we speak wins.
    /// </summary>
    public static CultureInfo SystemCulture(Func<string, string?>? env = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        var candidates = new List<string>();
        if (env("LANGUAGE") is { Length: > 0 } list) candidates.AddRange(list.Split(':', StringSplitOptions.RemoveEmptyEntries));
        foreach (var name in new[] { "LC_ALL", "LC_MESSAGES", "LANG" })
            if (env(name) is { Length: > 0 } v)
            {
                candidates.Add(v);
                break;
            }
        foreach (var c in candidates)
        {
            var tag = c.Split('.', '@')[0].Replace('_', '-');
            if (tag is "C" or "POSIX" or "") continue;
            try
            {
                var culture = CultureInfo.GetCultureInfo(tag);
                if (L.FromCulture(culture) != "en" || culture.TwoLetterISOLanguageName == "en") return culture;
            }
            catch (CultureNotFoundException)
            {
            }
        }
        return CultureInfo.InvariantCulture;
    }
}
