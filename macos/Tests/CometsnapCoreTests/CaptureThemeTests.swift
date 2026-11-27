import XCTest
@testable import CometsnapCore

final class CaptureThemeTests: XCTestCase {
    func testDefaultIsClassicBlue() {
        XCTAssertEqual(AppSettings().captureTheme, "blue")
        XCTAssertEqual(CaptureThemes.get(nil).accent, 0x3D6BFF)
        XCTAssertEqual(CaptureThemes.get("purple").id, "blue")
        XCTAssertEqual(CaptureThemes.get("TERRACOTTA").id, "terracotta")
    }

    func testSettingsKeepTheTheme() throws {
        var s = AppSettings()
        s.captureTheme = "yellow"
        let back = try JSONDecoder().decode(AppSettings.self, from: JSONEncoder().encode(s))
        XCTAssertEqual(back.captureTheme, "yellow")
        let old = try JSONDecoder().decode(AppSettings.self, from: Data("{}".utf8))
        XCTAssertEqual(old.captureTheme, "blue")
    }

    func testColorText() {
        XCTAssertEqual(ColorText.hex(255, 170, 0), "#FFAA00")
        XCTAssertEqual(ColorText.rgb(255, 170, 0), "rgb(255, 170, 0)")
    }
}
