import CometsnapCore
import CoreGraphics
import Foundation
import Vision

/// Scan a QR code: finds QR codes and barcodes in an image with Apple's Vision framework,
/// offline. QR codes come first; the same code seen twice is kept once.
enum CodeReader {
    struct Code {
        let text: String
        let format: String
    }

    static func read(_ image: CGImage) async throws -> [Code] {
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

    private static func readNow(_ image: CGImage) throws -> [Code] {
        let request = VNDetectBarcodesRequest()
        try VNImageRequestHandler(cgImage: image, options: [:]).perform([request])
        var seen = Set<String>()
        var codes: [Code] = []
        for observation in request.results ?? [] {
            guard let text = observation.payloadStringValue, !text.isEmpty, seen.insert(text).inserted else { continue }
            codes.append(Code(text: text, format: name(observation.symbology)))
        }
        return codes.filter { $0.format == "QR code" } + codes.filter { $0.format != "QR code" }
    }

    private static func name(_ s: VNBarcodeSymbology) -> String {
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
        default: return "Barcode"
        }
    }
}
