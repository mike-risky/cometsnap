import XCTest
@testable import CometsnapCore

final class SnapStyleTests: XCTestCase {
    private func solid(_ w: Int, _ h: Int, _ b: UInt8, _ g: UInt8, _ r: UInt8) -> [UInt8] {
        var p = [UInt8](repeating: 0, count: w * h * 4)
        var i = 0
        while i < p.count { p[i] = b; p[i + 1] = g; p[i + 2] = r; i += 4 }
        return p
    }

    private func px(_ o: StyledPixels, _ x: Int, _ y: Int) -> [UInt8] {
        let i = (y * o.width + x) * 4
        return Array(o.bgra[i..<(i + 4)])
    }

    func testDefaults() {
        let s = AppSettings()
        XCTAssertFalse(s.styleOnCopy)
        XCTAssertFalse(s.styleOnExport)
        XCTAssertTrue(s.stylePerShot)
        XCTAssertEqual(s.snapStyle, SnapStyle())
        XCTAssertEqual(s.snapStyle.cornerRadius, 12)
        XCTAssertTrue(s.snapStyle.shadow)
        XCTAssertEqual(SnapStylePresets.get("nope"), SnapStyle())
        XCTAssertEqual(Set(SnapStylePresets.all.map(\.id)).count, SnapStylePresets.all.count)
    }

    func testSettingsKeepTheCustomStyleAndReadOldPresetIds() throws {
        var s = AppSettings()
        s.snapStyle.shadowX = -7
        s.snapStyle.glow = true
        s.snapStyle.glowColor = "#FF2BD6"
        s.snapStyle.backdrop = .space
        s.styleOnExport = true
        let back = try JSONDecoder().decode(AppSettings.self, from: JSONEncoder().encode(s))
        XCTAssertEqual(back.snapStyle, s.snapStyle)
        XCTAssertTrue(back.styleOnExport)

        let old = try JSONDecoder().decode(AppSettings.self, from: Data(#"{"snapStyle":"neon","styleOnCopy":true}"#.utf8))
        XCTAssertTrue(old.styleOnCopy)
        XCTAssertTrue(old.snapStyle.glow)
        XCTAssertEqual(old.snapStyle.glowColor, "#FF2BD6")

        let odd = try JSONDecoder().decode(AppSettings.self, from: Data(#"{"snapStyle":{"glowColor":"f00","cornerRadius":9999,"backdrop":"Gradient"}}"#.utf8))
        XCTAssertEqual(odd.snapStyle.glowColor, "#FF0000")
        XCTAssertEqual(odd.snapStyle.cornerRadius, 200)
        XCTAssertEqual(odd.snapStyle.backdrop, .gradient)
        XCTAssertEqual(odd.snapStyle.shadowY, 10)
    }

    func testEveryPresetKeepsTheCaptureInTheMiddle() {
        let w = 300, h = 200
        for (id, _) in SnapStylePresets.all {
            let style = SnapStylePresets.get(id)
            let out = StyleRenderer.render(solid(w, h, 10, 20, 30), width: w, height: h, stride: w * 4, style: style)
            let m = StyleRenderer.measure(width: w, height: h, style: style)
            XCTAssertEqual(out.width, m.width)
            XCTAssertEqual(px(out, m.imageX + w / 2, m.imageY + h / 2), [10, 20, 30, 255], id)
            XCTAssertEqual(px(out, m.imageX + 3, m.imageY + h / 2), [10, 20, 30, 255], id)
        }
    }

    func testShadowOffsetMakesRoomOnItsSideOnly() {
        var st = SnapStyle()
        st.cornerRadius = 0
        st.shadowX = 10; st.shadowY = 10; st.shadowBlur = 0; st.shadowOpacity = 100
        let out = StyleRenderer.render(solid(100, 100, 255, 255, 255), width: 100, height: 100, stride: 400, style: st)
        let m = StyleRenderer.measure(width: 100, height: 100, style: st)
        XCTAssertEqual(m.imageX, 0)
        XCTAssertEqual(out.width, 111)
        XCTAssertEqual(px(out, 50, 50), [255, 255, 255, 255]) // the image itself
        XCTAssertEqual(px(out, 108, 108)[0...2], [0, 0, 0]) // the hard black shadow, bottom right
        XCTAssertEqual(px(out, 5, 105)[3], 0)            // bottom left stays see-through
    }

    func testBorderAndGlowUseTheirColors() {
        var st = SnapStyle()
        st.shadow = false; st.cornerRadius = 0
        st.border = true; st.borderWidth = 6; st.borderColor = "#FF0000"
        let bordered = StyleRenderer.render(solid(50, 50, 0, 255, 0), width: 50, height: 50, stride: 200, style: st)
        XCTAssertEqual(px(bordered, 2, 30), [0, 0, 255, 255])
        st.border = false
        st.glow = true; st.glowColor = "#00E5FF"; st.glowSize = 20; st.glowStrength = 100
        let glowing = StyleRenderer.render(solid(50, 50, 0, 0, 0), width: 50, height: 50, stride: 200, style: st)
        let m = StyleRenderer.measure(width: 50, height: 50, style: st)
        let edge = px(glowing, m.imageX - 1, m.imageY + 25)
        XCTAssertEqual(edge[0], 0xFF)
        XCTAssertEqual(edge[2], 0)
    }

    func testRoundedCornersSeeThroughAndBackdropsOpaque() {
        var st = SnapStyle()
        st.shadow = false
        let clean = StyleRenderer.render(solid(120, 80, 1, 2, 3), width: 120, height: 80, stride: 480, style: st)
        XCTAssertTrue(StyleRenderer.hasTransparency(clean))
        XCTAssertEqual(px(clean, 0, 0)[3], 0)
        for backdrop in [StyleBackdrop.color, .gradient, .blur, .space] {
            st.backdrop = backdrop
            let out = StyleRenderer.render(solid(120, 80, 1, 2, 3), width: 120, height: 80, stride: 480, style: st)
            XCTAssertFalse(StyleRenderer.hasTransparency(out), backdrop.rawValue)
        }
    }

    func testScaleDoublesSizes() {
        var st = SnapStyle()
        st.backdrop = .color; st.padding = 20
        XCTAssertEqual(StyleRenderer.measure(width: 10, height: 10, style: st, scale: 2).imageX, 40)
    }

    func testStyledFileSitsBesideTheOriginal() {
        let url = URL(fileURLWithPath: "/Users/me/Pictures/Cometsnap/snap_1.png")
        XCTAssertEqual(StyledFiles.styledURL(url).path, "/Users/me/Pictures/Cometsnap/snap_1-styled.png")
        XCTAssertEqual(StyleColors.normalize("f00", "#000000"), "#FF0000")
        XCTAssertNil(StyleColors.tryParse("blue"))
    }
}
