using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Cometsnap.Core.Localization;

namespace Cometsnap.Core.Tests;

public class LocalizationTests
{
    [Theory]
    [InlineData("fr-CH", "fr")]
    [InlineData("de-DE", "de")]
    [InlineData("it-IT", "it")]
    [InlineData("en-US", "en")]
    [InlineData("es-MX", "es")]
    [InlineData("pt-BR", "pt")]
    [InlineData("pt-PT", "pt")]
    [InlineData("zh-CN", "zh")]
    [InlineData("zh-TW", "zh")]
    [InlineData("hi-IN", "hi")]
    [InlineData("ja-JP", "en")]
    [InlineData("ru-RU", "en")]
    public void System_language_falls_back_to_English(string culture, string expected) =>
        Assert.Equal(expected, L.FromCulture(CultureInfo.GetCultureInfo(culture)));

    [Fact]
    public void Translates_and_falls_back()
    {
        try
        {
            L.Use("fr");
            Assert.Equal("Copié !", L.T("Copied!"));
            Assert.Equal("Couleur 3", L.F("Color {0}", 3));
            Assert.Equal("not a known text", L.T("not a known text"));
            L.Use("", CultureInfo.GetCultureInfo("de-AT"));
            Assert.Equal("de", L.Language);
            L.Use("xx");
            Assert.Equal("en", L.Language);
            Assert.Equal("Copied!", L.T("Copied!"));
        }
        finally
        {
            L.Use("en");
        }
    }

    [Fact]
    public void Every_text_in_the_app_is_translated_with_the_same_placeholders()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var keys = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"L\.[TF]\(""((?:[^""\\]|\\.)*)"""))
                keys.Add(Regex.Unescape(m.Groups[1].Value));
        }
        Assert.NotEmpty(keys);
        foreach (var code in L.Languages.Select(l => l.Code).Where(c => c != "en"))
        {
            var table = L.Table(code)!;
            foreach (var key in keys)
            {
                Assert.True(table.TryGetValue(key, out var text) && text.Length > 0, $"{code}: missing \"{key}\"");
                Assert.Equal(Placeholders(key), Placeholders(text!));
            }
        }
    }

    private static string Placeholders(string s) =>
        string.Join(",", Regex.Matches(s, @"\{\d[^}]*\}").Select(m => m.Value).Distinct().OrderBy(v => v));

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
