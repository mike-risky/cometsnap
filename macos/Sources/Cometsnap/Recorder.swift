import AppKit
import AVFoundation
import CometsnapCore
import ImageIO
import ScreenCaptureKit
import UniformTypeIdentifiers
import VideoToolbox

enum RecordingFormat { case mp4, gif }

/// "Transmissions": screen recording to MP4 (H.264 + system sound) or animated GIF, with
/// ScreenCaptureKit. Cometsnap's own frame and pill are never in the recording.
final class Recorder: NSObject, SCStreamDelegate {
    private unowned let app: AppController
    private var session: RecordingSession?
    private var frameWindow: RecordingFrameWindow?
    private var pill: RecordingPill?
    private var starting = false

    init(app: AppController) { self.app = app }

    var isRecording: Bool { session != nil }

    func toggleRegion(format: RecordingFormat) {
        if isRecording { stop(); return }
        guard !starting else { return }
        app.capture.pickRegion { [weak self] result in
            guard let self, let result else { return }
            self.start(screen: result.frozen.screen, rect: result.rect, format: format)
        }
    }

    func startFullscreen(format: RecordingFormat) {
        if isRecording { stop(); return }
        app.hidePanels()
        let s = NSScreen.underMouse
        start(screen: s, rect: s.frame, format: format)
    }

    private func start(screen: NSScreen, rect: CGRect, format: RecordingFormat) {
        guard ScreenGrabber.hasPermission else { app.showPermissionHelp(); return }
        starting = true
        let settings = app.settings
        let ext = format == .gif ? "gif" : "mp4"
        let url = SnapNaming.nextFreeURL(root: app.capturesDirectory, date: Date(), prefix: SnapNaming.recordingPrefix, ext: ext)
        Task { @MainActor in
            defer { self.starting = false }
            do {
                let content = try await ScreenGrabber.shared.content(fresh: true)
                guard let display = ScreenGrabber.shared.display(for: screen, in: content) else {
                    throw CaptureError.failed(L.t("That screen is not available for recording."))
                }
                let own = ScreenGrabber.shared.ownApplication(in: content)
                let filter = SCContentFilter(display: display, excludingApplications: own.map { [$0] } ?? [], exceptingWindows: [])
                let config = SCStreamConfiguration()
                let local = screen.displayRect(fromGlobal: rect)
                config.sourceRect = local
                let size = RecordingSession.outputSize(points: rect.size, scale: screen.backingScaleFactor, format: format)
                config.width = Int(size.width)
                config.height = Int(size.height)
                config.showsCursor = true
                config.pixelFormat = kCVPixelFormatType_32BGRA
                config.colorSpaceName = CGColorSpace.sRGB
                config.queueDepth = 6
                let fps = format == .gif ? settings.gifFramesPerSecond : settings.videoFramesPerSecond
                config.minimumFrameInterval = CMTime(value: 1, timescale: CMTimeScale(fps))
                let withAudio = format == .mp4 && settings.recordSystemAudio
                if withAudio {
                    config.capturesAudio = true
                    config.excludesCurrentProcessAudio = true
                    config.sampleRate = 48_000
                    config.channelCount = 2
                }
                try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
                let s = try RecordingSession(url: url, format: format, size: size, fps: fps, audio: withAudio)
                let stream = SCStream(filter: filter, configuration: config, delegate: self)
                try stream.addStreamOutput(s, type: .screen, sampleHandlerQueue: s.queue)
                if withAudio { try stream.addStreamOutput(s, type: .audio, sampleHandlerQueue: s.queue) }
                s.stream = stream
                try await stream.startCapture()
                self.session = s
                self.showChrome(rect: rect, screen: screen, format: format)
                Log.info("recording \(ext) \(Int(size.width))x\(Int(size.height)) @\(fps)fps → \(url.lastPathComponent)")
            } catch {
                self.app.report(error)
            }
        }
    }

    func stop() {
        guard let s = session else { return }
        session = nil
        hideChrome()
        Task { @MainActor in
            try? await s.stream?.stopCapture()
            s.stream = nil
            s.finish { [weak self] url, thumb, error in
                DispatchQueue.main.async {
                    guard let self else { return }
                    if let error { self.app.report(error); return }
                    guard let url else { return }
                    Clipboard.copyFile(url)
                    self.app.capturesChanged()
                    if self.app.settings.showEditCard { self.app.editCard.show(recording: url, thumbnail: thumb) }
                    Log.info("recording saved \(url.lastPathComponent)")
                }
            }
        }
    }

    func stream(_ stream: SCStream, didStopWithError error: Error) {
        DispatchQueue.main.async { [weak self] in
            guard let self, self.session?.stream === stream else { return }
            self.stop()
            self.app.report(error)
        }
    }

    private func showChrome(rect: CGRect, screen: NSScreen, format: RecordingFormat) {
        app.statusItem.setRecording(true)
        if rect != screen.frame {
            let f = RecordingFrameWindow(around: rect)
            f.orderFrontRegardless()
            frameWindow = f
        }
        let p = RecordingPill(format: format) { [weak self] in self?.stop() }
        p.place(near: rect, on: screen)
        p.orderFrontRegardless()
        pill = p
    }

    private func hideChrome() {
        app.statusItem.setRecording(false)
        frameWindow?.orderOut(nil)
        frameWindow = nil
        pill?.stopTimer()
        pill?.orderOut(nil)
        pill = nil
    }
}

// MARK: - Session (writer side, runs on its own queue)

final class RecordingSession: NSObject, SCStreamOutput {
    let url: URL
    let format: RecordingFormat
    let queue = DispatchQueue(label: "cometsnap.recording")
    var stream: SCStream?

    private var writer: AVAssetWriter?
    private var videoInput: AVAssetWriterInput?
    private var audioInput: AVAssetWriterInput?
    private var started = false
    private var lastPTS = CMTime.invalid

    private var gif: CGImageDestination?
    private var gifPending: (CGImage, CMTime)?
    private var gifFrames = 0
    private var thumbnail: CGImage?
    private var finished = false
    private static let maxGifFrames = 1800

    /// Output pixel size: Retina, capped for H.264 (3840 wide) and GIF (960 wide); even numbers.
    static func outputSize(points: CGSize, scale: CGFloat, format: RecordingFormat) -> CGSize {
        var w = points.width * scale, h = points.height * scale
        let maxW: CGFloat = format == .gif ? 960 : 3840
        let maxH: CGFloat = format == .gif ? 960 : 2160
        let f = min(1, min(maxW / w, maxH / h))
        w = (w * f / 2).rounded(.down) * 2
        h = (h * f / 2).rounded(.down) * 2
        return CGSize(width: max(16, w), height: max(16, h))
    }

    init(url: URL, format: RecordingFormat, size: CGSize, fps: Int, audio: Bool) throws {
        self.url = url
        self.format = format
        super.init()
        if format == .gif {
            guard let d = CGImageDestinationCreateWithURL(url as CFURL, UTType.gif.identifier as CFString, RecordingSession.maxGifFrames, nil) else {
                throw CaptureError.failed(L.t("Could not create the GIF file."))
            }
            CGImageDestinationSetProperties(d, [kCGImagePropertyGIFDictionary as String: [kCGImagePropertyGIFLoopCount as String: 0]] as CFDictionary)
            gif = d
            return
        }
        let w = try AVAssetWriter(outputURL: url, fileType: .mp4)
        let bitrate = Int(size.width * size.height * Double(fps) * 0.12)
        let video = AVAssetWriterInput(mediaType: .video, outputSettings: [
            AVVideoCodecKey: AVVideoCodecType.h264,
            AVVideoWidthKey: Int(size.width),
            AVVideoHeightKey: Int(size.height),
            AVVideoCompressionPropertiesKey: [
                AVVideoAverageBitRateKey: max(2_000_000, min(40_000_000, bitrate)),
                AVVideoExpectedSourceFrameRateKey: fps,
                AVVideoMaxKeyFrameIntervalKey: fps * 2,
                AVVideoProfileLevelKey: AVVideoProfileLevelH264HighAutoLevel,
            ] as [String: Any],
        ])
        video.expectsMediaDataInRealTime = true
        guard w.canAdd(video) else { throw CaptureError.failed(L.t("Could not set up the video encoder.")) }
        w.add(video)
        videoInput = video
        if audio {
            let a = AVAssetWriterInput(mediaType: .audio, outputSettings: [
                AVFormatIDKey: kAudioFormatMPEG4AAC,
                AVSampleRateKey: 48_000,
                AVNumberOfChannelsKey: 2,
                AVEncoderBitRateKey: 160_000,
            ])
            a.expectsMediaDataInRealTime = true
            if w.canAdd(a) { w.add(a); audioInput = a }
        }
        writer = w
    }

    func stream(_ stream: SCStream, didOutputSampleBuffer sb: CMSampleBuffer, of type: SCStreamOutputType) {
        guard !finished, sb.isValid else { return }
        switch type {
        case .screen:
            guard isCompleteFrame(sb), let pixels = sb.imageBuffer else { return }
            let pts = sb.presentationTimeStamp
            if thumbnail == nil { thumbnail = Self.copyImage(pixels, maxWidth: 360) }
            if format == .gif { addGifFrame(pixels, pts) } else { appendVideo(sb, pts) }
        case .audio:
            guard started, let a = audioInput, a.isReadyForMoreMediaData else { return }
            a.append(sb)
        default:
            break
        }
    }

    private func isCompleteFrame(_ sb: CMSampleBuffer) -> Bool {
        guard let attachments = CMSampleBufferGetSampleAttachmentsArray(sb, createIfNecessary: false) as? [[SCStreamFrameInfo: Any]],
              let raw = attachments.first?[.status] as? Int, let status = SCFrameStatus(rawValue: raw) else { return false }
        return status == .complete
    }

    private func appendVideo(_ sb: CMSampleBuffer, _ pts: CMTime) {
        guard let w = writer, let v = videoInput else { return }
        if !started {
            guard w.startWriting() else { return }
            w.startSession(atSourceTime: pts)
            started = true
        }
        if v.isReadyForMoreMediaData, v.append(sb) { lastPTS = pts }
    }

    private func addGifFrame(_ pixels: CVPixelBuffer, _ pts: CMTime) {
        guard gifFrames < RecordingSession.maxGifFrames, let img = Self.copyImage(pixels, maxWidth: 960) else { return }
        if let pending = gifPending { writeGif(pending.0, delay: CMTimeGetSeconds(CMTimeSubtract(pts, pending.1))) }
        gifPending = (img, pts)
    }

    private func writeGif(_ img: CGImage, delay: Double) {
        guard let gif else { return }
        let d = max(0.02, min(5, delay))
        let props = [kCGImagePropertyGIFDictionary as String: [kCGImagePropertyGIFDelayTime as String: d, kCGImagePropertyGIFUnclampedDelayTime as String: d]] as CFDictionary
        CGImageDestinationAddImage(gif, img, props)
        gifFrames += 1
    }

    /// Finalises the file on the session queue.
    func finish(_ done: @escaping (URL?, CGImage?, Error?) -> Void) {
        queue.async {
            self.finished = true
            if self.format == .gif {
                if let pending = self.gifPending { self.writeGif(pending.0, delay: 0.5) }
                guard let gif = self.gif, self.gifFrames > 0, CGImageDestinationFinalize(gif) else {
                    try? FileManager.default.removeItem(at: self.url)
                    done(nil, nil, CaptureError.failed(L.t("The GIF was empty: nothing moved on screen.")))
                    return
                }
                done(self.url, self.thumbnail, nil)
                return
            }
            guard let w = self.writer, self.started else {
                self.writer?.cancelWriting()
                try? FileManager.default.removeItem(at: self.url)
                done(nil, nil, CaptureError.failed(L.t("The recording was empty.")))
                return
            }
            self.videoInput?.markAsFinished()
            self.audioInput?.markAsFinished()
            if self.lastPTS.isValid { w.endSession(atSourceTime: self.lastPTS) }
            w.finishWriting {
                if w.status == .completed { done(self.url, self.thumbnail, nil) } else { done(nil, nil, w.error ?? CaptureError.failed(L.t("Saving the video failed."))) }
            }
        }
    }

    /// Copies the frame out of ScreenCaptureKit's buffer pool (holding buffers stalls the stream).
    static func copyImage(_ pixels: CVPixelBuffer, maxWidth: CGFloat) -> CGImage? {
        var cg: CGImage?
        VTCreateCGImageFromCVPixelBuffer(pixels, options: nil, imageOut: &cg)
        guard let src = cg else { return nil }
        let f = min(1, maxWidth / CGFloat(src.width))
        let w = max(1, Int(CGFloat(src.width) * f)), h = max(1, Int(CGFloat(src.height) * f))
        guard let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue)
        else { return nil }
        ctx.interpolationQuality = .high
        ctx.draw(src, in: CGRect(x: 0, y: 0, width: w, height: h))
        return ctx.makeImage()
    }
}

// MARK: - Recording chrome

/// A red outline around the recorded area. Click-through.
final class RecordingFrameWindow: NSPanel {
    init(around rect: CGRect) {
        let f = rect.insetBy(dx: -3, dy: -3)
        super.init(contentRect: f, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        level = .screenSaver
        backgroundColor = .clear
        isOpaque = false
        hasShadow = false
        ignoresMouseEvents = true
        isReleasedWhenClosed = false
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        let v = BorderView(frame: CGRect(origin: .zero, size: f.size))
        contentView = v
    }

    final class BorderView: NSView {
        override func draw(_ dirtyRect: NSRect) {
            let p = NSBezierPath(rect: bounds.insetBy(dx: 1.5, dy: 1.5))
            p.lineWidth = 3
            p.setLineDash([10, 6], count: 2, phase: 0)
            Theme.recordRed.setStroke()
            p.stroke()
        }
    }
}

/// "● REC 00:12  ■ Stop" pill.
final class RecordingPill: NSPanel {
    private let time = NSTextField(labelWithString: "00:00")
    private let dot = NSView()
    private var timer: Timer?
    private let began = Date()

    init(format: RecordingFormat, onStop: @escaping () -> Void) {
        super.init(contentRect: CGRect(x: 0, y: 0, width: 210, height: 40), styleMask: [.borderless, .nonactivatingPanel],
                   backing: .buffered, defer: false)
        level = .statusBar
        backgroundColor = .clear
        isOpaque = false
        hasShadow = true
        isReleasedWhenClosed = false
        isMovableByWindowBackground = true
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle]
        let root = NSView(frame: CGRect(x: 0, y: 0, width: 210, height: 40))
        root.wantsLayer = true
        root.layer?.backgroundColor = Theme.night.withAlphaComponent(0.95).cgColor
        root.layer?.cornerRadius = 20
        root.layer?.borderWidth = 1
        root.layer?.borderColor = Theme.line.cgColor
        dot.wantsLayer = true
        dot.layer?.backgroundColor = Theme.recordRed.cgColor
        dot.layer?.cornerRadius = 6
        dot.frame = CGRect(x: 16, y: 14, width: 12, height: 12)
        let label = NSTextField(labelWithString: format == .gif ? "GIF" : "REC")
        label.font = Theme.heading(12)
        label.textColor = .white
        label.frame = CGRect(x: 36, y: 11, width: 40, height: 18)
        time.font = NSFont.monospacedDigitSystemFont(ofSize: 13, weight: .semibold)
        time.textColor = Theme.muted
        time.frame = CGRect(x: 76, y: 11, width: 50, height: 18)
        let stop = SpaceButton(title: L.t("Stop"), symbol: "stop.fill", fill: Theme.recordRed, tip: L.t("Stop recording (same hotkey, or click the menu bar icon)"))
        stop.layer?.cornerRadius = 14
        stop.frame = CGRect(x: 128, y: 6, width: 74, height: 28)
        stop.onClick = onStop
        for v in [dot, label, time, stop] as [NSView] { root.addSubview(v) }
        contentView = root
        timer = Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { [weak self] _ in self?.tick() }
        let pulse = CABasicAnimation(keyPath: "opacity")
        pulse.fromValue = 1
        pulse.toValue = 0.25
        pulse.duration = 0.6
        pulse.autoreverses = true
        pulse.repeatCount = .infinity
        dot.layer?.add(pulse, forKey: "pulse")
    }

    override var canBecomeKey: Bool { false }

    private func tick() {
        let s = Int(Date().timeIntervalSince(began))
        time.stringValue = String(format: "%02d:%02d", s / 60, s % 60)
    }

    func stopTimer() {
        timer?.invalidate()
        timer = nil
    }

    /// Below the area, or above it, or inside its bottom edge when it fills the screen.
    func place(near rect: CGRect, on screen: NSScreen) {
        let vf = screen.visibleFrame
        var o = CGPoint(x: rect.midX - frame.width / 2, y: rect.minY - frame.height - 10)
        if o.y < vf.minY + 4 { o.y = rect.maxY + 10 }
        if o.y + frame.height > vf.maxY - 4 { o.y = max(vf.minY + 12, rect.minY + 12) }
        o.x = min(max(vf.minX + 8, o.x), vf.maxX - frame.width - 8)
        setFrameOrigin(o)
    }
}
