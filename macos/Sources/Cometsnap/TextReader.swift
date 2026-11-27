import AppKit
import CometsnapCore
import Vision

/// Reads the text in an image with Apple's Vision framework. Runs on the Mac, offline:
/// nothing is uploaded. Recognizes the Mac's preferred languages when Vision supports them.
enum TextReader {
    struct Outcome {
        let text: String
        let lines: Int
        var found: Bool { !text.isEmpty }
    }

    static func read(_ image: CGImage) async throws -> Outcome {
        try await withCheckedThrowingContinuation { continuation in
            DispatchQueue.global(qos: .userInitiated).async {
                do {
                    continuation.resume(returning: try readNow(image))
                } catch {
                    continuation.resume(throwing: error)
                }
            }
        }
    }

    private static func readNow(_ image: CGImage) throws -> Outcome {
        let request = VNRecognizeTextRequest()
        request.recognitionLevel = .accurate
        request.usesLanguageCorrection = true
        request.automaticallyDetectsLanguage = true
        if let supported = try? request.supportedRecognitionLanguages() {
            var wanted: [String] = []
            for id in Locale.preferredLanguages {
                let lower = id.lowercased()
                if let match = supported.first(where: { lower.hasPrefix($0.lowercased()) || $0.lowercased().hasPrefix(String(lower.prefix(2))) }),
                   !wanted.contains(match) {
                    wanted.append(match)
                }
            }
            if !wanted.contains("en-US"), supported.contains("en-US") { wanted.append("en-US") }
            if !wanted.isEmpty { request.recognitionLanguages = wanted }
        }
        let handler = VNImageRequestHandler(cgImage: image, options: [:])
        try handler.perform([request])
        let height = Double(image.height), width = Double(image.width)
        // Vision boxes are normalized with the origin at the bottom left.
        let lines: [RecognizedLine] = (request.results ?? []).compactMap { obs in
            guard let text = obs.topCandidates(1).first?.string else { return nil }
            let b = obs.boundingBox
            return RecognizedLine(text: text, left: b.minX * width, top: (1 - b.maxY) * height, height: b.height * height)
        }
        return Outcome(text: OcrText.join(lines), lines: lines.count)
    }
}

/// The recognized text in an editable box, to fix a letter or copy only a part.
final class TextWindow: NSWindow {
    private let textView: NSTextView
    private let hint = NSTextField(wrappingLabelWithString: L.t("The whole text is on the clipboard. Select a part to copy just that."))

    init(text: String) {
        let scroll = NSTextView.scrollableTextView()
        textView = scroll.documentView as! NSTextView
        super.init(contentRect: CGRect(x: 0, y: 0, width: 560, height: 420), styleMask: [.titled, .closable, .resizable, .miniaturizable],
                   backing: .buffered, defer: false)
        title = L.t("Text from the capture")
        isReleasedWhenClosed = false
        minSize = CGSize(width: 320, height: 200)

        textView.string = text
        textView.font = Theme.ui(14)
        textView.textColor = .white
        textView.backgroundColor = Theme.night
        textView.insertionPointColor = Theme.yellow
        textView.isRichText = false
        textView.textContainerInset = CGSize(width: 8, height: 8)
        scroll.borderType = .noBorder

        hint.font = Theme.ui(12)
        hint.textColor = Theme.muted
        let copy = SpaceButton(title: L.t("Copy"), symbol: "doc.on.doc", fill: Theme.terracotta,
                               tip: L.t("Copy the selection, or everything when nothing is selected (⌘C)"))
        copy.onClick = { [weak self] in self?.copySelectionOrAll() }

        let root = NSView()
        root.wantsLayer = true
        root.layer?.backgroundColor = Theme.night.cgColor
        for v in [scroll, hint, copy] as [NSView] {
            v.translatesAutoresizingMaskIntoConstraints = false
            root.addSubview(v)
        }
        NSLayoutConstraint.activate([
            scroll.topAnchor.constraint(equalTo: root.topAnchor, constant: 14),
            scroll.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 14),
            scroll.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -14),
            scroll.bottomAnchor.constraint(equalTo: copy.topAnchor, constant: -10),
            copy.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -14),
            copy.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -14),
            copy.widthAnchor.constraint(equalToConstant: 96),
            copy.heightAnchor.constraint(equalToConstant: 34),
            hint.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 14),
            hint.trailingAnchor.constraint(equalTo: copy.leadingAnchor, constant: -10),
            hint.centerYAnchor.constraint(equalTo: copy.centerYAnchor),
        ])
        contentView = root
        center()
    }

    private func copySelectionOrAll() {
        let range = textView.selectedRange()
        let all = textView.string as NSString
        let selected = range.length > 0
        let text = selected ? all.substring(with: range) : textView.string
        guard !text.isEmpty else { return }
        Clipboard.copyText(text)
        hint.stringValue = selected ? L.t("Selection copied.") : L.t("Everything copied.")
    }
}
