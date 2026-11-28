using System.IO;
using Cometsnap.Core.Updates;

namespace Cometsnap.Core.Tests;

// Also linked into the Linux tests (linux/tests/Cometsnap.Linux.Tests): the same file runs on both.
public class UpdateTests
{
    private static AppVersion V(string s) => AppVersion.Parse(s);

    [Theory]
    [InlineData("v1.4.0", 1, 4, 0)]
    [InlineData("1.3.1", 1, 3, 1)]
    [InlineData("2.0", 2, 0, 0)]
    [InlineData("1.4.0.0", 1, 4, 0)]
    [InlineData("v1.5.2-beta", 1, 5, 2)]
    public void Reads_version_numbers(string text, int major, int minor, int patch) =>
        Assert.Equal(new AppVersion(major, minor, patch), V(text));

    [Fact]
    public void Compares_versions_and_spots_dev_builds()
    {
        Assert.True(V("1.10.0") > V("1.9.9"));
        Assert.True(V("1.3.1") > V("1.3.0"));
        Assert.True(V("2.0.0") > V("1.99.99"));
        Assert.True(V("0.1.812").IsDevBuild);
        Assert.False(V("1.0.0").IsDevBuild);
        Assert.False(AppVersion.TryParse("nightly", out _));
        Assert.False(AppVersion.TryParse("", out _));
    }

    private const string ReleaseJson = """
        {
          "tag_name": "v1.4.0", "html_url": "https://github.com/mike-risky/cometsnap/releases/tag/v1.4.0",
          "draft": false, "prerelease": false,
          "assets": [
            { "name": "Cometsnap-Setup-x64.exe", "browser_download_url": "https://example.invalid/setup.exe", "size": 6200000 },
            { "name": "SHA256SUMS.txt", "browser_download_url": "https://example.invalid/sums.txt", "size": 400 }
          ]
        }
        """;

    [Fact]
    public void Reads_a_GitHub_release()
    {
        var r = ReleaseInfo.Parse(ReleaseJson)!;
        Assert.Equal("v1.4.0", r.Tag);
        Assert.Equal(V("1.4.0"), r.Version);
        Assert.Equal(2, r.Assets.Count);
        Assert.Equal(6200000, r.Asset("cometsnap-setup-x64.exe")!.Size);
        Assert.Null(r.Asset("Cometsnap-macOS.dmg"));
        Assert.Null(ReleaseInfo.Parse("""{ "tag_name": "nightly" }"""));
        Assert.Null(ReleaseInfo.Parse("not json"));
    }

    [Fact]
    public void Offers_only_newer_stable_releases()
    {
        var r = ReleaseInfo.Parse(ReleaseJson)!;
        Assert.True(UpdateChecker.IsOffered(r, V("1.3.1"), "", manual: false));
        Assert.False(UpdateChecker.IsOffered(r, V("1.4.0"), "", manual: false));
        Assert.False(UpdateChecker.IsOffered(r, V("1.5.0"), "", manual: true));
        // Skip this version: quiet on its own, but "Check now" still shows it.
        Assert.False(UpdateChecker.IsOffered(r, V("1.3.1"), "1.4.0", manual: false));
        Assert.True(UpdateChecker.IsOffered(r, V("1.3.1"), "1.4.0", manual: true));
        Assert.True(UpdateChecker.IsOffered(r, V("1.3.1"), "1.3.5", manual: false));
        // Nightly and test builds: only when asked, so the card can be tried.
        Assert.True(UpdateChecker.IsOffered(r, V("0.1.900"), "", manual: true));
        Assert.False(UpdateChecker.IsOffered(r with { Prerelease = true }, V("1.3.1"), "", manual: true));
        Assert.False(UpdateChecker.IsOffered(r with { Draft = true }, V("1.3.1"), "", manual: false));
        Assert.False(UpdateChecker.IsOffered(null, V("1.3.1"), "", manual: true));
    }

    [Fact]
    public void Checks_once_a_day_and_never_on_dev_builds()
    {
        var now = new DateTime(2026, 11, 28, 15, 0, 0, DateTimeKind.Utc);
        var p = new UpdatePrefs();
        Assert.True(p.IsDue(now, V("1.4.0")));
        Assert.False(p.IsDue(now, V("0.1.5")));
        p.LastCheckUtc = now.AddHours(-3);
        Assert.False(p.IsDue(now, V("1.4.0")));
        p.LastCheckUtc = now.AddHours(-25);
        Assert.True(p.IsDue(now, V("1.4.0")));
        p.LastCheckUtc = now.AddDays(3); // clock went back: check again
        Assert.True(p.IsDue(now, V("1.4.0")));
        p.CheckAutomatically = false;
        Assert.False(p.IsDue(now, V("1.4.0")));
    }

    [Fact]
    public void Prefs_survive_a_restart_and_a_broken_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cometsnap-update-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "updates.json");
            new UpdatePrefs { CheckAutomatically = false, SkippedVersion = "1.4.0", LastCheckUtc = new DateTime(2026, 11, 28, 0, 0, 0, DateTimeKind.Utc) }.Save(path);
            var back = UpdatePrefs.Load(path);
            Assert.False(back.CheckAutomatically);
            Assert.Equal("1.4.0", back.SkippedVersion);
            Assert.NotNull(back.LastCheckUtc);
            File.WriteAllText(path, "{ broken");
            Assert.True(UpdatePrefs.Load(path).CheckAutomatically);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    private const string WhatsNewJson = """
        {
          "//": "comment",
          "latest": { "en": ["Brand new"], "fr": ["Tout neuf"] },
          "1.4.0": { "en": ["Updates come to you", "Mission briefing"], "fr": ["Les mises à jour viennent à vous"] },
          "1.3.1": { "en": ["Setup fix"] },
          "1.3.0": { "en": ["Shortcut editor"] }
        }
        """;

    [Fact]
    public void Whats_new_in_the_users_language_for_every_missed_version()
    {
        Assert.Equal(new[] { "Updates come to you", "Mission briefing" }, WhatsNew.FromJson(WhatsNewJson, V("1.3.1"), V("1.4.0"), "en"));
        Assert.Equal(new[] { "Les mises à jour viennent à vous" }, WhatsNew.FromJson(WhatsNewJson, V("1.3.1"), V("1.4.0"), "fr"));
        // Missing language: English. Several versions missed: newest first.
        Assert.Equal(new[] { "Updates come to you", "Mission briefing", "Setup fix" }, WhatsNew.FromJson(WhatsNewJson, V("1.3.0"), V("1.4.0"), "de"));
        // A tag whose entry is still called "latest".
        Assert.Equal(new[] { "Tout neuf", "Les mises à jour viennent à vous" }, WhatsNew.FromJson(WhatsNewJson, V("1.3.1"), V("1.5.0"), "fr"));
        Assert.Equal(2, WhatsNew.FromJson(WhatsNewJson, V("1.0.0"), V("1.4.0"), "en", max: 2).Count);
        Assert.Empty(WhatsNew.FromJson("nope", V("1.0.0"), V("1.4.0"), "en"));
    }

    [Fact]
    public void The_shipped_whats_new_file_has_every_language()
    {
        var json = File.ReadAllText(Path.Combine(RepoRoot(), "updates", "whats-new.json"));
        foreach (var lang in new[] { "en", "fr", "de", "it", "es", "pt", "zh", "hi" })
        {
            var lines = WhatsNew.FromJson(json, V("1.3.1"), V("99.0.0"), lang, max: 100);
            Assert.NotEmpty(lines);
            if (lang != "en") Assert.NotEqual(WhatsNew.FromJson(json, V("1.3.1"), V("99.0.0"), "en", max: 100), lines);
        }
    }

    private const string Changelog = """
        # What's new in Cometsnap

        ## Latest build

        ### Improved
        - **A quick mission briefing on first launch.** The window explains Cometsnap.
          - **Windows:** a sub-point that is not a headline.

        ## v1.3.1 — 28 November 2026

        ### Fixed
        - **Setup no longer crashes.** It starts again.

        ## v1.3.0 — 28 November 2026

        ### New
        - **Change any shortcut, on every platform.** Settings has a shortcut editor.
        - **Name your captures your way.** A pattern.
        """;

    [Fact]
    public void Whats_new_from_the_changelog_as_a_fallback()
    {
        Assert.Equal(new[] { "Setup no longer crashes" }, WhatsNew.FromChangelog(Changelog, V("1.3.0"), V("1.3.1")));
        Assert.Equal(new[] { "Setup no longer crashes", "Change any shortcut, on every platform", "Name your captures your way" },
            WhatsNew.FromChangelog(Changelog, V("1.2.0"), V("1.3.1")));
        Assert.Equal(new[] { "A quick mission briefing on first launch", "Setup no longer crashes" },
            WhatsNew.FromChangelog(Changelog, V("1.3.0"), V("1.4.0")));
    }

    [Fact]
    public void Reads_checksum_files()
    {
        var sums = Checksums.Parse("""
            0123456789abcdef0123456789abcdef0123456789abcdef0123456789ABCDEF  Cometsnap-Setup-x64.exe
            fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210 *rel/Cometsnap-x86_64.AppImage
            not a line
            """);
        Assert.Equal(2, sums.Count);
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", sums["cometsnap-setup-x64.exe"]);
        Assert.True(sums.ContainsKey("Cometsnap-x86_64.AppImage"));
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
