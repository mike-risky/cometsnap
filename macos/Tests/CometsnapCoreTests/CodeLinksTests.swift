import XCTest
@testable import CometsnapCore

final class CodeLinksTests: XCTestCase {
    func testOnlyWebLinksGetTheOpenButton() {
        XCTAssertEqual(CodeLinks.linkOf("https://cometsnap.com"), "https://cometsnap.com")
        XCTAssertEqual(CodeLinks.linkOf("  http://example.org/a?b=c "), "http://example.org/a?b=c")
        XCTAssertEqual(CodeLinks.linkOf("www.cometsnap.com"), "https://www.cometsnap.com")
        XCTAssertNil(CodeLinks.linkOf("javascript:alert(1)"))
        XCTAssertNil(CodeLinks.linkOf("file:///Users/me"))
        XCTAssertNil(CodeLinks.linkOf("WIFI:S:home;T:WPA;P:secret;;"))
        XCTAssertNil(CodeLinks.linkOf("hello there"))
        XCTAssertNil(CodeLinks.linkOf(""))
        XCTAssertNil(CodeLinks.linkOf(nil))
    }

    func testQrShortcutIsRemappable() {
        XCTAssertTrue(HotkeyAction.allCases.contains(.qr))
    }
}
