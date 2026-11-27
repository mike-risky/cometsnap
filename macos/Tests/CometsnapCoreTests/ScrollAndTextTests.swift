import XCTest
@testable import CometsnapCore

final class ScrollStitcherTests: XCTestCase {
    private let w = 240, h = 320, headerRows = 30, footerRows = 16, scrollbar = 14

    private func page(_ height: Int, seed: UInt64 = 7) -> [UInt8] {
        var state = seed
        func next() -> UInt8 {
            state = state &* 6364136223846793005 &+ 1442695040888963407
            return UInt8(truncatingIfNeeded: state >> 33)
        }
        var p = [UInt8](repeating: 255, count: w * 4 * height)
        for y in 0..<height where y / 10 % 3 != 2 {
            for x in 0..<w {
                let v = next()
                let i = (y * w + x) * 4
                p[i] = v; p[i + 1] = v; p[i + 2] = v
            }
        }
        return p
    }

    private func frame(_ page: [UInt8], _ pageHeight: Int, _ scroll: Int) -> [UInt8] {
        var f = [UInt8](repeating: 255, count: w * 4 * h)
        for y in 0..<h {
            for x in 0..<w {
                let v: UInt8
                let thumb = scroll * h / pageHeight
                if x >= w - scrollbar { v = y >= thumb && y < thumb + 20 ? 40 : 210 }
                else if y < headerRows { v = UInt8((x * 7 + y) % 256) }
                else if y >= h - footerRows { v = UInt8((x * 3 + y * 5) % 256) }
                else { v = page[((scroll + y - headerRows) * w + x) * 4] }
                let i = (y * w + x) * 4
                f[i] = v; f[i + 1] = v; f[i + 2] = v
            }
        }
        return f
    }

    func testGluesALongPageWithStickyHeaderAndFooter() {
        let pageHeight = 1200
        let p = page(pageHeight)
        let maxScroll = pageHeight - (h - headerRows - footerRows)
        let s = ScrollStitcher(width: w, frameHeight: h)
        XCTAssertEqual(s.add(frame(p, pageHeight, 0)), .first)
        let steps = [90, 140, 37, 200, 120]
        var at = 0, k = 0
        while at < maxScroll {
            let next = min(maxScroll, at + steps[k % steps.count])
            k += 1
            XCTAssertEqual(s.add(frame(p, pageHeight, next)), .appended)
            XCTAssertEqual(s.lastShift, next - at)
            at = next
        }
        XCTAssertEqual(s.add(frame(p, pageHeight, maxScroll)), .unchanged)
        XCTAssertEqual(s.header, headerRows)
        XCTAssertEqual(s.footer, footerRows)
        let (img, height) = s.compose()
        XCTAssertEqual(height, headerRows + pageHeight + footerRows)
        for y in stride(from: 0, to: pageHeight, by: 7) {
            XCTAssertEqual(img[((headerRows + y) * w + 5) * 4], p[(y * w + 5) * 4])
        }
    }

    /// Soft blocks (like text lines seen from afar), drawn at a fractional scroll position.
    private func softFrame(_ scrollTimes10: Int) -> [UInt8] {
        var state: UInt64 = 3
        var blocks = [[UInt8]](repeating: [UInt8](repeating: 0, count: w / 16 + 1), count: 400)
        for by in 0..<400 {
            for bx in 0...(w / 16) {
                state = state &* 6364136223846793005 &+ 1442695040888963407
                blocks[by][bx] = by % 4 == 3 ? 250 : UInt8(30 + Int(state >> 33) % 200)
            }
        }
        var f = [UInt8](repeating: 255, count: w * 4 * h)
        for y in 0..<h {
            let py = (Double(y) + Double(scrollTimes10) / 10) / 9
            let b0 = Int(py.rounded(.down)), t = py - Double(b0)
            for x in 0..<w {
                let v = UInt8((Double(blocks[b0][x / 16]) * (1 - t) + Double(blocks[b0 + 1][x / 16]) * t).rounded())
                let i = (y * w + x) * 4
                f[i] = v; f[i + 1] = v; f[i + 2] = v
            }
        }
        return f
    }

    func testFollowsFractionalSmoothScrolling() {
        let s = ScrollStitcher(width: w, frameHeight: h, ignoreRight: 0)
        _ = s.add(softFrame(0))
        var at = 0
        for step in [123, 457, 31, 806, 999, 52, 1500] {
            at += step
            XCTAssertEqual(s.add(softFrame(at)), .appended)
        }
        let (_, height) = s.compose()
        XCTAssertTrue(abs(height - (h + at / 10)) <= 5, "\(height)")
    }

    func testAFrameFromElsewhereIsLost() {
        let s = ScrollStitcher(width: w, frameHeight: h)
        _ = s.add(frame(page(1000), 1000, 0))
        XCTAssertEqual(s.add(frame(page(1000, seed: 99), 1000, 100)), .lost)
    }
}

final class OcrTextTests: XCTestCase {
    func testJoinsLinesAndKeepsParagraphs() {
        let text = OcrText.join([
            RecognizedLine(text: "Hello world", left: 0, top: 0, height: 10),
            RecognizedLine(text: "second line", left: 0, top: 13, height: 10),
            RecognizedLine(text: "New paragraph", left: 0, top: 40, height: 10),
        ])
        XCTAssertEqual(text, "Hello world\nsecond line\n\nNew paragraph")
    }

    func testRemovesSpacesBetweenChineseCharacters() {
        XCTAssertEqual(OcrText.tidyLine("你 好 世 界"), "你好世界")
        XCTAssertEqual(OcrText.tidyLine("截 图 Cometsnap 工 具"), "截图 Cometsnap 工具")
    }
}
