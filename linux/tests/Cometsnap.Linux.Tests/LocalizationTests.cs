using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Cometsnap.Core.Localization;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Editing;
using static Cometsnap.Linux.Core.Loc;

namespace Cometsnap.Linux.Tests;

public class LocalizationTests
{
    [Theory]
    [InlineData("fr_CH.UTF-8", null, "fr")]
    [InlineData("de_DE.UTF-8", null, "de")]
    [InlineData("it_IT.utf8", null, "it")]
    [InlineData("en_US.UTF-8", null, "en")]
    [InlineData("es_ES.UTF-8", null, "es")]
    [InlineData("pt_BR.UTF-8", null, "pt")]
    [InlineData("ja_JP.UTF-8", null, "en")]
    [InlineData("C.UTF-8", null, "en")]
    [InlineData("en_US.UTF-8", "fr_FR:en", "fr")]
    [InlineData("de_CH.UTF-8", "ja:it", "it")]
    public void Follows_the_desktop_language(string lang, string? language, string expected)
    {
        var env = new Dictionary<string, string?> { ["LANG"] = lang, ["LANGUAGE"] = language };
        Assert.Equal(expected, L.FromCulture(Loc.SystemCulture(n => env.GetValueOrDefault(n))));
    }

    [Fact]
    public void Shared_and_Linux_texts_translate_and_fall_back()
    {
        try
        {
            Loc.Use("fr");
            Assert.Equal("Copié !", T("Copied!"));
            Assert.Equal("Réglages de Cometsnap", T("Cometsnap settings"));
            Assert.Equal("not a known text", T("not a known text"));
            Loc.Use("en");
            Assert.Equal("Cometsnap settings", T("Cometsnap settings"));
        }
        finally
        {
            Loc.Use("en");
        }
    }

    [Fact]
    public void Every_text_in_the_Linux_app_is_translated_with_the_same_placeholders()
    {
        var src = Path.Combine(RepoRoot(), "linux", "src");
        var keys = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"(?<![\w.])[TF]\(""((?:[^""\\]|\\.)*)"""))
                keys.Add(Regex.Unescape(m.Groups[1].Value));
        }
        foreach (var tool in EditorTools.All)
        {
            keys.Add(tool.Label);
            keys.Add(tool.Tip);
        }
        foreach (var color in EditorTools.PaletteNames) keys.Add(color);
        Assert.True(keys.Count > 100);

        foreach (var code in new[] { "fr", "de", "it" })
        {
            var shared = L.Table(code)!;
            var own = LinuxStrings.Table(code)!;
            foreach (var key in keys)
            {
                var found = own.TryGetValue(key, out var text) || shared.TryGetValue(key, out text);
                Assert.True(found && !string.IsNullOrEmpty(text), $"{code}: missing \"{key}\"");
                Assert.Equal(Placeholders(key), Placeholders(text!));
            }
        }
    }

    private static string Placeholders(string s) =>
        string.Join(",", Regex.Matches(s, @"\{\d[^}]*\}").Select(m => m.Value).Distinct().OrderBy(v => v));

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));
}
