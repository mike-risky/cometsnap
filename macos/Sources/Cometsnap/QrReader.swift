import AppKit
import CometsnapCore
import Vision

/// Reads QR codes and barcodes in an image with Apple's Vision framework, on the Mac, offline.
enum QrReader {
    static func scan(_ image: CGImage) async -> [ScannedCode] {
        await withCheckedContinuation { continuation in
            DispatchQueue.global(qos: .userInitiated).async {
                continuation.resume(returning: scanNow(image))
            }
        }
    }

    private static func scanNow(_ image: CGImage) -> [ScannedCode] {
        // People drag tight boxes around codes: give the code back its white quiet zone.
        let source = padded(image) ?? image
        let request = VNDetectBarcodesRequest()
        let handler = VNImageRequestHandler(cgImage: source, options: [:])
        do {
            try handler.perform([request])
        } catch {
            Log.info("qr: vision failed: \(error)")
            return []
        }
        // Reading order: top to bottom, then left to right (Vision's origin is bottom left).
        let found = (request.results ?? []).sorted {
            abs($0.boundingBox.maxY - $1.boundingBox.maxY) > 0.05 ? $0.boundingBox.maxY > $1.boundingBox.maxY
                : $0.boundingBox.minX < $1.boundingBox.minX
        }
        var codes: [ScannedCode] = []
        for obs in found {
            guard let text = obs.payloadStringValue, !text.isEmpty, !codes.contains(where: { $0.text == text }) else { continue }
            codes.append(ScannedCode(text: text, format: formatName(obs.symbology)))
        }
        return codes
    }

    private static func padded(_ image: CGImage) -> CGImage? {
        let margin = max(16, min(64, min(image.width, image.height) / 8))
        let w = image.width + margin * 2, h = image.height + margin * 2
        guard let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        ctx.setFillColor(CGColor(red: 1, green: 1, blue: 1, alpha: 1))
        ctx.fill(CGRect(x: 0, y: 0, width: w, height: h))
        ctx.draw(image, in: CGRect(x: margin, y: margin, width: image.width, height: image.height))
        return ctx.makeImage()
    }

    static func formatName(_ s: VNBarcodeSymbology) -> String {
        switch s {
        case .qr, .microQR: return "QR code"
        case .dataMatrix: return "Data Matrix"
        case .aztec: return "Aztec"
        case .pdf417, .microPDF417: return "PDF417"
        case .ean13: return "EAN-13"
        case .ean8: return "EAN-8"
        case .upce: return "UPC-E"
        case .code128: return "Code 128"
        case .code39, .code39Checksum, .code39FullASCII, .code39FullASCIIChecksum: return "Code 39"
        case .code93, .code93i: return "Code 93"
        case .itf14, .i2of5, .i2of5Checksum: return "ITF"
        case .codabar: return "Codabar"
        default: return "Barcode"
        }
    }
}

/// What the scanned codes say, with Copy and, for web addresses, Open link. The first code is
/// already on the clipboard when this opens. Links never open by themselves.
final class QrWindow: NSWindow {
    private let hint = NSTextField(wrappingLabelWithString: "")

    init(codes: [ScannedCode]) {
        super.init(contentRect: CGRect(x: 0, y: 0, width: 520, height: 200), styleMask: [.titled, .closable, .miniaturizable],
                   backing: .buffered, defer: false)
        title = codes.count == 1 ? L.f("{0} found", L.t(codes[0].format)) : L.f("{0} codes found", codes.count)
        isReleasedWhenClosed = false
        level = .floating

        let list = NSStackView()
        list.orientation = .vertical
        list.alignment = .leading
        list.spacing = 10
        for code in codes.prefix(8) { list.addArrangedSubview(card(for: code)) }

        hint.font = Theme.ui(12)
        hint.textColor = Theme.muted
        hint.stringValue = codes.first?.wifi?.password != nil ? L.t("The Wi-Fi password is on the clipboard.") : L.t("Copied to the clipboard.")

        let root = NSStackView(views: [list, hint])
        root.orientation = .vertical
        root.alignment = .leading
        root.spacing = 8
        root.edgeInsets = NSEdgeInsets(top: 14, left: 14, bottom: 14, right: 14)
        root.wantsLayer = true
        root.layer?.backgroundColor = Theme.night.cgColor
        for v in list.arrangedSubviews { v.widthAnchor.constraint(equalTo: list.widthAnchor).isActive = true }
        list.widthAnchor.constraint(equalTo: root.widthAnchor, constant: -28).isActive = true
        root.widthAnchor.constraint(equalToConstant: 520).isActive = true
        contentView = root
        setContentSize(root.fittingSize)
        center()
    }

    override func cancelOperation(_ sender: Any?) { close() }

    private func card(for code: ScannedCode) -> NSView {
        let label = NSTextField(labelWithString: kindLabel(code).uppercased())
        label.font = Theme.heading(12)
        label.textColor = Theme.yellow

        var rows: [NSView] = [label]
        if let wifi = code.wifi {
            rows.append(field(L.t("Network"), wifi.name))
            if let p = wifi.password { rows.append(field(L.t("Password"), p)) }
        } else {
            rows.append(value(code.text))
        }

        var buttons: [NSView] = []
        if let link = code.link {
            let open = SpaceButton(title: L.t("Open link"), symbol: "arrow.up.right.square", fill: Theme.terracotta,
                                   tip: L.f("Opens {0} in your browser", QrCodes.linkHost(code.text) ?? link.absoluteString))
            open.onClick = { [weak self] in
                NSWorkspace.shared.open(link)
                self?.close()
            }
            buttons.append(open)
        }
        if let p = code.wifi?.password {
            let copy = SpaceButton(title: L.t("Copy password"), symbol: "key", fill: Theme.terracotta, tip: L.t("Copy the Wi-Fi password"))
            copy.onClick = { [weak self] in self?.copy(p, done: L.t("Password copied.")) }
            buttons.append(copy)
        }
        let plain = code.link == nil && code.wifi == nil
        let copy = SpaceButton(title: code.wifi == nil ? L.t("Copy") : L.t("Copy all"), symbol: "doc.on.doc",
                               fill: plain ? Theme.terracotta : nil, tip: L.t("Copy what the code says"))
        copy.onClick = { [weak self] in self?.copy(code.text, done: L.t("Copied to the clipboard.")) }
        buttons.append(copy)
        for b in buttons { b.heightAnchor.constraint(equalToConstant: 34).isActive = true }
        let bar = NSStackView(views: buttons)
        bar.spacing = 8
        rows.append(bar)

        let stack = NSStackView(views: rows)
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 6
        stack.edgeInsets = NSEdgeInsets(top: 12, left: 14, bottom: 12, right: 14)
        stack.wantsLayer = true
        stack.layer?.backgroundColor = Theme.surface.cgColor
        stack.layer?.borderColor = Theme.line.cgColor
        stack.layer?.borderWidth = 1
        stack.layer?.cornerRadius = 12
        return stack
    }

    private func kindLabel(_ code: ScannedCode) -> String {
        let f = L.t(code.format)
        switch code.kind {
        case .link: return L.f("{0} · link", f)
        case .wifi: return L.f("{0} · Wi-Fi", f)
        case .email: return L.f("{0} · email", f)
        case .phone: return L.f("{0} · phone", f)
        case .text: return f
        }
    }

    private func field(_ name: String, _ text: String) -> NSView {
        let l = NSTextField(labelWithString: name)
        l.font = Theme.ui(12)
        l.textColor = Theme.muted
        let s = NSStackView(views: [l, value(text)])
        s.orientation = .vertical
        s.alignment = .leading
        s.spacing = 2
        return s
    }

    /// Selectable: pick a part with the mouse and ⌘C it.
    private func value(_ text: String) -> NSTextField {
        let shown = text.count > 600 ? String(text.prefix(600)) + "…" : text
        let v = NSTextField(wrappingLabelWithString: shown)
        v.isSelectable = true
        v.font = Theme.ui(14)
        v.textColor = .white
        v.preferredMaxLayoutWidth = 460
        return v
    }

    private func copy(_ text: String, done: String) {
        Clipboard.copyText(text)
        hint.stringValue = done
    }
}
