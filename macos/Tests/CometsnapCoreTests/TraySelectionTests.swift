import XCTest
@testable import CometsnapCore

final class TraySelectionTests: XCTestCase {
    private let items = ["a", "b", "c", "d", "e"]

    private func make() -> TraySelection {
        let s = TraySelection()
        s.setItems(items)
        return s
    }

    func testPlainClickPicksOne() {
        let s = make()
        s.click("b")
        s.click("d")
        XCTAssertEqual(s.picked, ["d"])
    }

    func testCommandClickTogglesInDisplayOrder() {
        let s = make()
        s.click("d")
        s.click("a", toggle: true)
        s.click("c", toggle: true)
        XCTAssertEqual(s.picked, ["a", "c", "d"])
        s.click("c", toggle: true)
        XCTAssertEqual(s.picked, ["a", "d"])
    }

    func testShiftClickRangeFromAnchor() {
        let s = make()
        s.click("b")
        s.click("d", range: true)
        XCTAssertEqual(s.picked, ["b", "c", "d"])
        s.click("a", range: true)
        XCTAssertEqual(s.picked, ["a", "b"])
        s.click("e", toggle: true, range: true)
        XCTAssertEqual(s.picked, ["a", "b", "c", "d", "e"])
    }

    func testBoxReplacesOrAdds() {
        let s = make()
        s.click("a")
        s.beginBox(add: false)
        s.updateBox(["c", "d"])
        XCTAssertEqual(s.picked, ["c", "d"])
        s.updateBox(["c"])
        XCTAssertEqual(s.picked, ["c"])
        s.endBox()
        s.beginBox(add: true)
        s.updateBox(["e"])
        XCTAssertEqual(s.picked, ["c", "e"])
    }

    func testTargetsAndSearch() {
        let s = make()
        s.click("a")
        s.click("c", toggle: true)
        XCTAssertEqual(s.targets(for: "c"), ["a", "c"])
        XCTAssertEqual(s.targets(for: "b"), ["b"])
        s.selectAll()
        s.setItems(["b", "c"])
        XCTAssertEqual(s.picked, ["b", "c"])
        s.clear()
        XCTAssertEqual(s.count, 0)
    }
}
