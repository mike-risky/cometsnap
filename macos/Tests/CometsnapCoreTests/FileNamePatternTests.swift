import XCTest
@testable import CometsnapCore

final class FileNamePatternTests: XCTestCase {
    private let when: Date = {
        var c = DateComponents()
        c.year = 2026; c.month = 11; c.day = 26; c.hour = 8; c.minute = 56; c.second = 12
        return Calendar(identifier: .gregorian).date(from: c)!
    }()

    func testDefaultKeepsTheClassicName() {
        XCTAssertEqual(FileNamePattern.expand(FileNamePattern.defaultPattern, date: when), "2026-11/snap_2026-11-26_08-56-12")
    }

    func testTokensAndCleaning() {
        XCTAssertEqual(FileNamePattern.expand("Shot {DATE} {time}", date: when), "Shot 2026-11-26 08-56-12")
        XCTAssertEqual(FileNamePattern.expand("{year}{month}{day}-{hour}{minute}{second}", date: when), "20261126-085612")
        XCTAssertEqual(FileNamePattern.expand("n{counter}", date: when, counter: 7), "n007")
        XCTAssertEqual(FileNamePattern.expand("{nope}-{date}", date: when), "{nope}-2026-11-26")
        XCTAssertEqual(FileNamePattern.expand("a:b*c?d", date: when), "a_b_c_d")
        XCTAssertEqual(FileNamePattern.expand("../../etc/{date}", date: when), "etc/2026-11-26")
        XCTAssertEqual(FileNamePattern.expand("a/b/c/d/e", date: when), "a/b/c/d_e")
        XCTAssertEqual(FileNamePattern.expand("  ", date: when), FileNamePattern.expand(nil, date: when))
        XCTAssertEqual(FileNamePattern.expand("///", date: when), "2026-11/snap_2026-11-26_08-56-12")
        XCTAssertTrue(FileNamePattern.usesCounter("x {Counter}"))
        XCTAssertFalse(FileNamePattern.usesCounter(nil))
        XCTAssertFalse(FileNamePattern.isUsable(" / "))
    }

    func testNextFreeURLUsesPatternAndFormat() {
        let root = URL(fileURLWithPath: "/tmp/r")
        let taken: Set<String> = ["/tmp/r/shots/a.jpg"]
        let url = SnapNaming.nextFreeURL(root: root, date: when, pattern: "shots/a", ext: "jpg", exists: { taken.contains($0.path) })
        XCTAssertEqual(url.path, "/tmp/r/shots/a_2.jpg")
        XCTAssertEqual(SnapNaming.fileExtension(for: "JPEG"), "jpg")
        XCTAssertEqual(SnapNaming.fileExtension(for: "heic"), "png")
        XCTAssertEqual(SnapNaming.suggestedName(pattern: nil, date: when), "snap_2026-11-26_08-56-12")
    }

    func testSettingsKeepUnknownPatternsSafe() throws {
        let json = #"{"fileNamePattern": "  ", "imageFormat": "JPG", "hotkeys": {"capture": {"keyCode": 1, "modifiers": 256, "display": "⌘S"}}}"#
        let s = try JSONDecoder().decode(AppSettings.self, from: Data(json.utf8))
        XCTAssertEqual(s.fileNamePattern, FileNamePattern.defaultPattern)
        XCTAssertEqual(s.imageFormat, "jpg")
        XCTAssertEqual(s.hotkeys["capture"]?.display, "⌘S")
        XCTAssertTrue(s.saveEveryCapture)
    }
}
