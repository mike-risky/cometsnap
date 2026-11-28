import XCTest
@testable import CometsnapCore

final class UpdateTests: XCTestCase {
    private func v(_ s: String) -> AppVersion { AppVersion(s)! }

    func testVersions() {
        XCTAssertEqual(AppVersion("v1.4.0"), AppVersion(1, 4, 0))
        XCTAssertEqual(AppVersion("2.0"), AppVersion(2, 0, 0))
        XCTAssertEqual(AppVersion("1.4.0.0"), AppVersion(1, 4, 0))
        XCTAssertEqual(AppVersion("v1.5.2-beta"), AppVersion(1, 5, 2))
        XCTAssertEqual(AppVersion("v1.3.0 — 28 November 2026"), AppVersion(1, 3, 0))
        XCTAssertNil(AppVersion("nightly"))
        XCTAssertNil(AppVersion("Latest build"))
        XCTAssertTrue(v("1.10.0") > v("1.9.9"))
        XCTAssertTrue(v("0.1.0").isDevBuild)
    }

    private let release = Data("""
        { "tag_name": "v1.4.0", "html_url": "https://github.com/mike-risky/cometsnap/releases/tag/v1.4.0",
          "draft": false, "prerelease": false,
          "assets": [ { "name": "Cometsnap-macOS.dmg", "browser_download_url": "https://example.invalid/a.dmg", "size": 9000000 } ] }
        """.utf8)

    func testReleaseAndOffer() {
        let r = ReleaseInfo.parse(release)!
        XCTAssertEqual(r.version, v("1.4.0"))
        XCTAssertEqual(r.asset("cometsnap-macos.dmg")?.size, 9000000)
        XCTAssertTrue(ReleaseInfo.isOffered(r, current: v("1.3.1"), skipped: "", manual: false))
        XCTAssertFalse(ReleaseInfo.isOffered(r, current: v("1.4.0"), skipped: "", manual: true))
        XCTAssertFalse(ReleaseInfo.isOffered(r, current: v("1.3.1"), skipped: "1.4.0", manual: false))
        XCTAssertTrue(ReleaseInfo.isOffered(r, current: v("1.3.1"), skipped: "1.4.0", manual: true))
        XCTAssertTrue(ReleaseInfo.isOffered(r, current: v("0.1.0"), skipped: "", manual: true))
        var pre = r
        pre.prerelease = true
        XCTAssertFalse(ReleaseInfo.isOffered(pre, current: v("1.3.1"), skipped: "", manual: true))
        XCTAssertNil(ReleaseInfo.parse(Data("{\"tag_name\": \"macos-nightly\"}".utf8)))
    }

    func testWhatsNew() {
        let json = Data("""
            { "//": "x", "latest": { "en": ["Brand new"], "fr": ["Tout neuf"] },
              "1.4.0": { "en": ["Updates come to you", "Mission briefing"], "fr": ["Les mises à jour viennent à vous"] },
              "1.3.1": { "en": ["Setup fix"] } }
            """.utf8)
        XCTAssertEqual(WhatsNew.fromJSON(json, current: v("1.3.1"), target: v("1.4.0"), language: "fr"), ["Les mises à jour viennent à vous"])
        XCTAssertEqual(WhatsNew.fromJSON(json, current: v("1.3.0"), target: v("1.4.0"), language: "de"), ["Updates come to you", "Mission briefing", "Setup fix"])
        XCTAssertEqual(WhatsNew.fromJSON(json, current: v("1.3.1"), target: v("1.5.0"), language: "fr"), ["Tout neuf", "Les mises à jour viennent à vous"])
        let md = """
            ## Latest build
            - **A quick mission briefing on first launch.** Text.
            ## v1.3.1 — 28 November 2026
            - **Setup no longer crashes.** It starts.
            ## v1.3.0 — 28 November 2026
            - **Change any shortcut, on every platform.** Editor.
            """
        XCTAssertEqual(WhatsNew.fromChangelog(md, current: v("1.3.0"), target: v("1.3.1")), ["Setup no longer crashes"])
        XCTAssertEqual(WhatsNew.fromChangelog(md, current: v("1.3.0"), target: v("1.4.0")), ["A quick mission briefing on first launch", "Setup no longer crashes"])
    }

    func testShippedWhatsNewHasEveryLanguage() throws {
        let file = URL(fileURLWithPath: #filePath).deletingLastPathComponent().appendingPathComponent("../../../updates/whats-new.json").standardized
        let data = try Data(contentsOf: file)
        let en = WhatsNew.fromJSON(data, current: v("1.3.1"), target: v("99.0.0"), language: "en", max: 100)
        XCTAssertFalse(en.isEmpty)
        for (code, _) in L.languages where code != "en" {
            let lines = WhatsNew.fromJSON(data, current: v("1.3.1"), target: v("99.0.0"), language: code, max: 100)
            XCTAssertFalse(lines.isEmpty, code)
            XCTAssertNotEqual(lines, en, code)
        }
    }

    func testChecksumsAndPrefs() {
        let sums = Checksums.parse("0123456789abcdef0123456789abcdef0123456789abcdef0123456789ABCDEF  Cometsnap-macOS.dmg\nnope\n")
        XCTAssertEqual(sums["cometsnap-macos.dmg"], "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")
        let now = Date(timeIntervalSince1970: 1_800_000_000)
        var p = UpdatePrefs()
        XCTAssertTrue(p.isDue(now: now, current: v("1.4.0")))
        XCTAssertFalse(p.isDue(now: now, current: v("0.1.0")))
        p.lastCheck = now.addingTimeInterval(-3 * 3600)
        XCTAssertFalse(p.isDue(now: now, current: v("1.4.0")))
        p.lastCheck = now.addingTimeInterval(-25 * 3600)
        XCTAssertTrue(p.isDue(now: now, current: v("1.4.0")))
        p.checkAutomatically = false
        XCTAssertFalse(p.isDue(now: now, current: v("1.4.0")))
    }

    func testUpdateTextsInEveryLanguage() {
        let fr = Set(UpdateStrings.french.keys)
        XCTAssertFalse(fr.isEmpty)
        for (code, _) in L.languages where code != "en" {
            let table = UpdateStrings.table(code) ?? [:]
            XCTAssertEqual(Set(table.keys), fr, code)
            for (k, val) in table where k.contains("{0}") { XCTAssertTrue(val.contains("{0}"), "\(code): \(k)") }
        }
        L.use("fr")
        XCTAssertEqual(L.f("Cometsnap {0} is here", "1.4.0"), "Cometsnap 1.4.0 est arrivé")
        XCTAssertEqual(L.t("COPIED!"), Strings.french["COPIED!"])
        L.use("en")
    }
}
