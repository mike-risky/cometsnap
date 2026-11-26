import CoreGraphics
import Foundation
import XCTest
@testable import CometsnapCore

final class LoupePlacementTests: XCTestCase {
    let screen = CGRect(x: 0, y: 0, width: 1440, height: 900)
    let size = CGSize(width: 160, height: 190)

    func testStaysInPreferredCornerWhenCursorIsFar() {
        let c = LoupePlacement.resolveCorner(preferred: .bottomLeft, current: .bottomLeft, screen: screen, size: size,
                                             cursor: CGPoint(x: 700, y: 450))
        XCTAssertEqual(c, .bottomLeft)
    }

    func testJumpsAwayWhenCursorComesClose() {
        let c = LoupePlacement.resolveCorner(preferred: .bottomLeft, current: .bottomLeft, screen: screen, size: size,
                                             cursor: CGPoint(x: 60, y: 60))
        XCTAssertEqual(c, .bottomRight)
    }

    func testHysteresisKeepsTheAlternativeCorner() {
        // Just outside the avoid zone of the preferred corner, but inside the hysteresis band.
        let cursor = CGPoint(x: 16 + 160 + 48 + 20, y: 100)
        let c = LoupePlacement.resolveCorner(preferred: .bottomLeft, current: .bottomRight, screen: screen, size: size, cursor: cursor)
        XCTAssertEqual(c, .bottomRight)
    }

    func testTopCornersUseMaxY() {
        let r = LoupePlacement.rect(for: .topRight, in: screen, size: size)
        XCTAssertEqual(r.maxY, screen.maxY - LoupePlacement.margin)
        XCTAssertEqual(r.maxX, screen.maxX - LoupePlacement.margin)
    }

    func testFollowRectStaysOnScreen() {
        let r = LoupePlacement.followRect(in: screen, size: size, cursor: CGPoint(x: 1430, y: 10))
        XCTAssertTrue(screen.contains(r))
    }
}

final class GeometryTests: XCTestCase {
    func testEdgeSnap() {
        XCTAssertEqual(EdgeSnapper.snap(103, to: [100, 200]), 100)
        XCTAssertEqual(EdgeSnapper.snap(120, to: [100, 200]), 120)
    }

    func testSnap45() {
        let p = Angles.snap45(from: .zero, to: CGPoint(x: 10, y: 9))
        XCTAssertEqual(p.x, p.y, accuracy: 0.001)
    }

    func testSquare() {
        let p = Angles.square(from: .zero, to: CGPoint(x: -10, y: 4))
        XCTAssertEqual(p, CGPoint(x: -10, y: 10))
    }
}

final class StorageTests: XCTestCase {
    func testNamingAddsSuffixForSameSecond() {
        let root = URL(fileURLWithPath: "/tmp/caps")
        var c = DateComponents()
        c.year = 2026; c.month = 11; c.day = 26; c.hour = 8; c.minute = 56; c.second = 12
        let date = Calendar(identifier: .gregorian).date(from: c)!
        let first = SnapNaming.nextFreeURL(root: root, date: date, exists: { _ in false })
        XCTAssertEqual(first.path, "/tmp/caps/2026-11/snap_2026-11-26_08-56-12.png")
        let second = SnapNaming.nextFreeURL(root: root, date: date, exists: { $0 == first })
        XCTAssertEqual(second.lastPathComponent, "snap_2026-11-26_08-56-12_2.png")
    }

    func testRecentSkipsHiddenSidecars() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        let month = root.appendingPathComponent("2026-11")
        try FileManager.default.createDirectory(at: month.appendingPathComponent(".cometsnap"), withIntermediateDirectories: true)
        try Data([1]).write(to: month.appendingPathComponent("snap_a.png"))
        try Data([1]).write(to: month.appendingPathComponent("rec_b.mp4"))
        try Data([1]).write(to: month.appendingPathComponent(".cometsnap/snap_a.original.png"))
        try Data([1]).write(to: month.appendingPathComponent("notes.txt"))
        let items = RecentCaptures.scan(root: root)
        XCTAssertEqual(Set(items.map(\.name)), ["snap_a.png", "rec_b.mp4"])
        try? FileManager.default.removeItem(at: root)
    }

    func testSettingsToleratesMissingKeys() throws {
        let s = try JSONDecoder().decode(AppSettings.self, from: Data("{\"playSound\": true}".utf8))
        XCTAssertTrue(s.playSound)
        XCTAssertEqual(s.loupeCorner, .bottomLeft)
        XCTAssertFalse(s.firstRunDone)
    }
}

final class AnnotationTests: XCTestCase {
    func testRoundTrip() {
        var doc = AnnotationDocument()
        doc.annotations.append(Annotation(kind: .arrow, points: [Pt(x: 1, y: 2), Pt(x: 30, y: 40)], color: RGBA.palette[0], width: 4))
        doc.crop = CGRect(x: 1, y: 2, width: 3, height: 4)
        let back = AnnotationDocument.decode(doc.encoded()!)
        XCTAssertEqual(back, doc)
    }

    func testStepNumbersContinue() {
        var doc = AnnotationDocument()
        doc.annotations.append(Annotation(kind: .step, points: [Pt(x: 1, y: 1)], color: RGBA.palette[0], width: 3, number: 1))
        doc.annotations.append(Annotation(kind: .step, points: [Pt(x: 9, y: 9)], color: RGBA.palette[0], width: 3, number: 2))
        XCTAssertEqual(doc.nextStepNumber, 3)
    }

    func testHitTestLine() {
        let a = Annotation(kind: .line, points: [Pt(x: 0, y: 0), Pt(x: 100, y: 0)], color: RGBA.palette[0], width: 2)
        XCTAssertTrue(a.hitTest(CGPoint(x: 50, y: 3), tolerance: 4))
        XCTAssertFalse(a.hitTest(CGPoint(x: 50, y: 30), tolerance: 4))
    }

    func testHex() {
        XCTAssertEqual(RGBA(hex: "#3D6BFF")?.hex, "#3D6BFF")
        XCTAssertEqual(EditorTool.allCases.count, 13)
        XCTAssertEqual(EditorTool.forKey("n"), .step)
    }

    func testUndo() {
        var u = UndoStack<Int>()
        u.push(1)
        XCTAssertEqual(u.undo(current: 2), 1)
        XCTAssertEqual(u.redo(current: 1), 2)
    }
}

final class FunTests: XCTestCase {
    private var utc: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "UTC")!
        return c
    }

    private func day(_ y: Int, _ m: Int, _ d: Int) -> Date {
        utc.date(from: DateComponents(year: y, month: m, day: d, hour: 12))!
    }

    func testKonami() {
        var k = KonamiCode()
        let seq: [KonamiKey] = [.up, .up, .up, .down, .down, .left, .right, .left, .right, .b]
        for key in seq { XCTAssertFalse(k.feed(key)) }
        XCTAssertTrue(k.feed(.a))
        XCTAssertFalse(k.feed(.a))
        XCTAssertFalse(k.feed(.other))
        XCTAssertEqual(k.matched, 0)
    }

    func testSeasons() {
        XCTAssertEqual(Season.at(day(2026, 10, 17), calendar: utc), .none)
        XCTAssertEqual(Season.at(day(2026, 10, 18), calendar: utc), .halloween)
        XCTAssertEqual(Season.at(day(2026, 10, 31), calendar: utc), .halloween)
        XCTAssertEqual(Season.at(day(2026, 12, 1), calendar: utc), .winter)
        XCTAssertEqual(Season.at(day(2027, 1, 1), calendar: utc), .none)
    }

    func testMilestones() {
        XCTAssertTrue(SnapMilestones.isMilestone(10))
        XCTAssertTrue(SnapMilestones.isMilestone(1000))
        XCTAssertTrue(SnapMilestones.isMilestone(30000))
        XCTAssertFalse(SnapMilestones.isMilestone(11))
        XCTAssertFalse(SnapMilestones.isMilestone(15000))
        XCTAssertEqual(SnapMilestones.title(1000), "1,000th snap!")
        XCTAssertEqual(SnapMilestones.suffix(21), "st")
        XCTAssertEqual(SnapMilestones.suffix(112), "th")
        XCTAssertEqual(SnapMilestones.suffix(23), "rd")
    }

    func testRecordAndMonthSummary() {
        var s = AppSettings()
        s.snapCount = 9
        let e = SnapMilestones.record(&s, now: day(2026, 10, 5), calendar: utc)
        XCTAssertEqual(e.milestone, 10)
        XCTAssertNil(e.finishedMonth)
        _ = SnapMilestones.record(&s, now: day(2026, 10, 6), calendar: utc)
        let next = SnapMilestones.record(&s, now: day(2026, 11, 1), calendar: utc)
        XCTAssertEqual(next.finishedMonth, MonthSummary(month: "2026-10", count: 2))
        XCTAssertEqual(next.finishedMonth?.title, "October 2026")
        XCTAssertEqual(s.snapMonthCount, 1)
        XCTAssertEqual(s.snapCount, 12)
    }

    func testSeedOnce() {
        var s = AppSettings()
        SnapMilestones.seed(&s, existing: [day(2026, 9, 1), day(2026, 11, 2), day(2026, 11, 3)], now: day(2026, 11, 20), calendar: utc)
        XCTAssertEqual(s.snapCount, 3)
        XCTAssertEqual(s.snapMonthCount, 2)
        XCTAssertTrue(s.snapCountSeeded)
        SnapMilestones.seed(&s, existing: Array(repeating: day(2026, 11, 2), count: 50), now: day(2026, 11, 20), calendar: utc)
        XCTAssertEqual(s.snapCount, 3)
    }

    func testDocumentKeepsConstellation() {
        var doc = AnnotationDocument()
        doc.connectSteps = true
        XCTAssertEqual(AnnotationDocument.decode(doc.encoded()!)?.connectSteps, true)
        let old = AnnotationDocument.decode(Data("{\"version\":1,\"annotations\":[]}".utf8))
        XCTAssertEqual(old?.connectSteps, false)
    }

    func testSettingsNewFieldsDefault() throws {
        let s = try JSONDecoder().decode(AppSettings.self, from: Data("{}".utf8))
        XCTAssertTrue(s.celebrations)
        XCTAssertTrue(s.seasonalLooks)
        XCTAssertFalse(s.snapCountSeeded)
    }
}

final class LocalizationTests: XCTestCase {
    override func tearDown() { L.use("en") }

    func testPicksSystemLanguage() {
        XCTAssertEqual(L.fromPreferred(["de-CH", "en-US"]), "de")
        XCTAssertEqual(L.fromPreferred(["es-ES", "it-IT"]), "it")
        XCTAssertEqual(L.fromPreferred(["ja-JP"]), "en")
        L.use("", preferred: ["fr-CH"])
        XCTAssertEqual(L.language, "fr")
        L.use("xx")
        XCTAssertEqual(L.language, "en")
    }

    func testTranslatesWithFallback() {
        L.use("fr")
        XCTAssertEqual(L.t("COPIED!"), "COPIÉ !")
        XCTAssertEqual(L.t("Delayed"), "Retardateur")
        XCTAssertEqual(L.t("not a known text"), "not a known text")
        XCTAssertEqual(L.f("Press {0} and grab something!", "⌘⇧2"), "Appuyez sur ⌘⇧2 et attrapez quelque chose !")
        XCTAssertEqual(EditorTool.step.label, "Étapes")
        L.use("de")
        XCTAssertEqual(L.t("Constellation"), "Sternbild")
        XCTAssertEqual(SnapMilestones.title(1000), "1.000. Screenshot!")
        L.use("it")
        XCTAssertEqual(L.t("Delayed"), "Autoscatto")
    }

    func testEveryLanguageHasEveryText() {
        let fr = Set(Strings.french.keys), de = Set(Strings.german.keys), it = Set(Strings.italian.keys)
        XCTAssertEqual(fr, de)
        XCTAssertEqual(fr, it)
        for (k, v) in Strings.french where k.contains("{0}") { XCTAssertTrue(v.contains("{0}"), k) }
    }
}

final class CutListTests: XCTestCase {
    func testTrimAndCuts() {
        var c = CutList(duration: 10)
        XCTAssertTrue(c.isUntouched)
        c.trimStart = 1
        c.trimEnd = 9
        XCTAssertTrue(c.addCut(3, 4))
        XCTAssertTrue(c.addCut(5, 6))
        XCTAssertEqual(c.kept.count, 3)
        XCTAssertEqual(c.keptLength, 6, accuracy: 1e-9)
        XCTAssertEqual(c.toOutput(4.5), 2.0, accuracy: 1e-9)
        XCTAssertEqual(c.nextKept(3.5)!, 4, accuracy: 1e-9)
        XCTAssertNil(c.nextKept(9.5))
        XCTAssertTrue(c.addCut(3.5, 5.5)) // merges both
        XCTAssertEqual(c.cuts, [TimeRange(3, 6)])
        XCTAssertFalse(c.addCut(1, 9))    // would cut everything
        XCTAssertFalse(c.addCut(2, 2.05)) // too short
        XCTAssertTrue(c.restoreCut(at: 4))
        XCTAssertTrue(c.cuts.isEmpty)
    }

    func testTrimLimits() {
        var c = CutList(duration: 5)
        c.trimEnd = 2
        c.trimStart = 3
        XCTAssertEqual(c.trimStart, 1.9, accuracy: 1e-9)
        c.trimEnd = -1
        XCTAssertEqual(c.trimEnd, 2.0, accuracy: 1e-9)
    }

    func testFrameTimes() {
        var c = CutList(duration: 4)
        c.addCut(1, 2)
        XCTAssertEqual(c.frameOutputTimes(from: 0.5, to: 0.75), [0.5])
        XCTAssertEqual(c.frameOutputTimes(from: 1.2, to: 1.4), [])
        XCTAssertEqual(c.frameOutputTimes(from: 1.9, to: 2.1), [1.0])
    }

    func testJsonRoundTripAndWindowsFormat() {
        var c = CutList(duration: 8)
        c.trimStart = 0.5
        c.addCut(2, 3)
        let back = CutList.from(json: c.json(), duration: 8)
        XCTAssertEqual(back, c)
        let win = CutList.from(json: Data(#"{"Start":1,"End":7,"Cuts":[[2,3]]}"#.utf8), duration: 8)
        XCTAssertEqual(win.trimStart, 1)
        XCTAssertEqual(win.cuts, [TimeRange(2, 3)])
        XCTAssertTrue(CutList.from(json: Data("nope".utf8), duration: 8).isUntouched)
        XCTAssertEqual(CutList.label(65.37), "1:05.3")
        XCTAssertEqual(CutList.label(65.37, tenths: false), "1:05")
    }

    func testSidecarPaths() {
        let u = URL(fileURLWithPath: "/x/2026-11/rec_1.mp4")
        XCTAssertEqual(EditSidecar.originalRecording(for: u).path, "/x/2026-11/.cometsnap/rec_1.original.mp4")
        XCTAssertEqual(EditSidecar.cuts(for: u).path, "/x/2026-11/.cometsnap/rec_1.cuts.json")
    }
}
