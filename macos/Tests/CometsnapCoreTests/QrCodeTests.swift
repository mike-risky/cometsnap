import XCTest
@testable import CometsnapCore

final class QrCodeTests: XCTestCase {
    func testOnlyWebAddressesCanBeOpened() {
        XCTAssertEqual(QrCodes.openableLink("https://example.com/a?b=c")?.absoluteString, "https://example.com/a?b=c")
        XCTAssertNotNil(QrCodes.openableLink("  http://example.com  "))
        XCTAssertNil(QrCodes.openableLink("javascript:alert(1)"))
        XCTAssertNil(QrCodes.openableLink("file:///etc/passwd"))
        XCTAssertNil(QrCodes.openableLink("x-apple.systempreferences:com.apple.preference.security"))
        XCTAssertNil(QrCodes.openableLink("example.com"))
        XCTAssertNil(QrCodes.openableLink("https://exa mple.com"))
        XCTAssertEqual(QrCodes.linkHost("https://Cometsnap.com/fr"), "cometsnap.com")
    }

    func testReadsAWifiCode() {
        let wifi = QrCodes.parseWifi(#"WIFI:T:WPA;S:My\;Net;P:pa\:ss\\word;H:true;;"#)
        XCTAssertEqual(wifi, WifiNetwork(name: "My;Net", password: #"pa:ss\word"#, security: "WPA", hidden: true))
        let code = ScannedCode(text: "WIFI:S:Cafe;T:WPA;P:latte42;;", format: "QR code")
        XCTAssertEqual(code.kind, .wifi)
        XCTAssertEqual(code.clipboardText, "latte42")
        let open = ScannedCode(text: "WIFI:S:Lobby;T:nopass;;", format: "QR code")
        XCTAssertNil(open.wifi?.password)
        XCTAssertEqual(open.clipboardText, open.text)
        XCTAssertNil(QrCodes.parseWifi("WIFI:T:WPA;;"))
    }

    func testKnowsWhatACodeHolds() {
        XCTAssertEqual(QrCodes.kind(of: "https://cometsnap.com"), .link)
        XCTAssertEqual(QrCodes.kind(of: "mailto:hi@example.com"), .email)
        XCTAssertEqual(QrCodes.kind(of: "tel:+41790000000"), .phone)
        XCTAssertEqual(QrCodes.kind(of: "4006381333931"), .text)
    }
}
