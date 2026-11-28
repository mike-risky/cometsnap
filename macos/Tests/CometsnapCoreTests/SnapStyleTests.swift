import XCTest
@testable import CometsnapCore

final class SnapStyleTests: XCTestCase {
    private func solid(_ w: Int, _ h: Int, _ b: UInt8, _ g: UInt8, _ r: UInt8) -> [UInt8] {
        var p = [UInt8](repeating: 0, count: w * h * 4)
        var i = 0
        while i < p.count { p[i] = b; p[i + 1] = g; p[i + 2] = r; i += 4 }
        return p
    }

    func testIdsAndDefaults() {
        XCTAssertEqual(SnapStyles.get(nil).id, "clean")
        XCTAssertEqual(SnapStyles.get("COMET").id, "comet")
        XCTAssertEqual(Set(SnapStyles.all.map(\.id)).count, SnapStyles.all.count)
        let s = AppSettings()
        XCTAssertFalse(s.styleOnCopy)
        XCTAssertFalse(s.styleOnExport)
        XCTAssertTrue(s.stylePerShot)
        XCTAssertEqual(s.snapStyle, "clean")
    }

    func testSettingsKeepTheStyle() throws {
        var s = AppSettings()
        s.snapStyle = "frosted"
        s.styleOnExport = true
        let back = try JSONDecoder().decode(AppSettings.self, from: JSONEncoder().encode(s))
        XCTAssertEqual(back.snapStyle, "frosted")
        XCTAssertTrue(back.styleOnExport)
    }

    func testEveryStyleKeepsTheCaptureInTheMiddle() {
        let w = 300, h = 200
        for style in SnapStyles.all {
            let out = StyleRenderer.render(solid(w, h, 10, 20, 30), width: w, height: h, stride: w * 4, style: style, accent: 0x3D6BFF)
            let m = StyleRenderer.measure(width: w, height: h, style: style)
            XCTAssertEqual(out.width, m.width)
            XCTAssertGreaterThan(out.width, w)
            let c = ((m.imageY + h / 2) * out.width + m.imageX + w / 2) * 4
            XCTAssertEqual(Array(out.bgra[c..<(c + 4)]), [10, 20, 30, 255], style.id)
            let e = ((m.imageY + h / 2) * out.width + m.imageX + 3) * 4
            XCTAssertEqual(Array(out.bgra[e..<(e + 4)]), [10, 20, 30, 255], style.id)
        }
    }

    func testCleanHasSeeThroughCornersAndBackdropsDoNot() {
        let clean = StyleRenderer.render(solid(120, 80, 1, 2, 3), width: 120, height: 80, stride: 480, style: SnapStyles.get("clean"), accent: 0x3D6BFF)
        XCTAssertTrue(StyleRenderer.hasTransparency(clean))
        for id in ["showcase", "frosted", "comet"] {
            let out = StyleRenderer.render(solid(120, 80, 1, 2, 3), width: 120, height: 80, stride: 480, style: SnapStyles.get(id), accent: 0xE2553A)
            XCTAssertFalse(StyleRenderer.hasTransparency(out), id)
        }
    }

    func testStyledFileSitsBesideTheOriginal() {
        let url = URL(fileURLWithPath: "/Users/me/Pictures/Cometsnap/snap_1.png")
        XCTAssertEqual(SnapStyles.styledURL(url).path, "/Users/me/Pictures/Cometsnap/snap_1-styled.png")
    }
}
