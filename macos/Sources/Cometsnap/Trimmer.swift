import AppKit
import AVFoundation
import CometsnapCore
import ImageIO
import UniformTypeIdentifiers

// MARK: - Preview

/// What the trimmer shows: an MP4 through AVPlayer, or a GIF frame by frame.
protocol ClipPreview: AnyObject {
    var view: NSView { get }
    var duration: Double { get }
    /// One frame, for the arrow keys.
    var frameStep: Double { get }
    var size: CGSize { get }
    var position: Double { get set }
    var isPlaying: Bool { get }
    func play()
    func pause()
    func release()
}

final class VideoPreview: ClipPreview {
    let view = NSView()
    let duration: Double
    let frameStep: Double
    let size: CGSize
    private var player: AVPlayer?
    private let playerLayer = AVPlayerLayer()
    private var target: Double?

    init(url: URL, duration: Double, size: CGSize, fps: Double) {
        self.duration = duration
        self.size = size
        frameStep = 1 / max(5, fps)
        let item = AVPlayerItem(asset: AVURLAsset(url: url))
        let p = AVPlayer(playerItem: item)
        p.actionAtItemEnd = .pause
        player = p
        playerLayer.player = p
        playerLayer.videoGravity = .resizeAspect
        playerLayer.backgroundColor = NSColor.black.cgColor
        view.layer = playerLayer // layer-hosting view: the player layer is the view
        view.wantsLayer = true
    }

    var position: Double {
        get {
            if let target { return target }
            let s = player?.currentTime().seconds ?? 0
            return s.isFinite ? s : 0
        }
        set {
            let t = min(max(0, newValue), duration)
            target = t
            player?.seek(to: CMTime(seconds: t, preferredTimescale: 600), toleranceBefore: .zero, toleranceAfter: .zero) { [weak self] _ in
                if self?.target == t { self?.target = nil }
            }
        }
    }

    var isPlaying: Bool { (player?.rate ?? 0) != 0 }
    func play() { player?.play() }
    func pause() { player?.pause() }

    func release() {
        player?.pause()
        playerLayer.player = nil
        player = nil
    }
}

/// A GIF, read with ImageIO. Frames are decoded on demand (a long GIF never sits in memory).
final class GifClip {
    let source: CGImageSource
    let starts: [Double]
    let lengths: [Double]
    let size: CGSize

    var count: Int { starts.count }
    var duration: Double { (starts.last ?? 0) + (lengths.last ?? 0) }

    init?(url: URL) {
        guard let src = CGImageSourceCreateWithURL(url as CFURL, [kCGImageSourceShouldCache: false] as CFDictionary) else { return nil }
        let n = CGImageSourceGetCount(src)
        guard n > 0 else { return nil }
        var starts: [Double] = [], lengths: [Double] = []
        var t = 0.0
        var size = CGSize.zero
        for i in 0..<n {
            let props = CGImageSourceCopyPropertiesAtIndex(src, i, nil) as? [CFString: Any]
            if i == 0 {
                size = CGSize(width: props?[kCGImagePropertyPixelWidth] as? Int ?? 0, height: props?[kCGImagePropertyPixelHeight] as? Int ?? 0)
            }
            let gif = props?[kCGImagePropertyGIFDictionary] as? [CFString: Any]
            var d = (gif?[kCGImagePropertyGIFUnclampedDelayTime] as? Double) ?? 0
            if d < 0.011 { d = (gif?[kCGImagePropertyGIFDelayTime] as? Double) ?? 0 }
            if d < 0.011 { d = 0.1 } // what browsers do with "as fast as possible"
            starts.append(t)
            lengths.append(d)
            t += d
        }
        source = src
        self.starts = starts
        self.lengths = lengths
        self.size = size
    }

    /// The frame on screen at `t`.
    func index(at t: Double) -> Int {
        var lo = 0, hi = count - 1
        while lo < hi {
            let mid = (lo + hi + 1) / 2
            if starts[mid] <= t { lo = mid } else { hi = mid - 1 }
        }
        return lo
    }

    func image(_ i: Int) -> CGImage? {
        CGImageSourceCreateImageAtIndex(source, max(0, min(count - 1, i)), [kCGImageSourceShouldCache: false] as CFDictionary)
    }
}

final class GifPreview: ClipPreview {
    let view = NSView()
    let clip: GifClip
    private var shown = -1
    private var current = 0.0
    private var timer: Timer?
    private var lastTick: CFTimeInterval = 0

    init(clip: GifClip) {
        self.clip = clip
        view.wantsLayer = true
        view.layer?.contentsGravity = .resizeAspect
        view.layer?.backgroundColor = NSColor.black.cgColor
        show()
    }

    var duration: Double { clip.duration }
    var frameStep: Double { clip.lengths.min() ?? 0.1 }
    var size: CGSize { clip.size }

    var position: Double {
        get { current }
        set {
            current = min(max(0, newValue), duration)
            show()
        }
    }

    var isPlaying: Bool { timer != nil }

    func play() {
        guard timer == nil else { return }
        lastTick = CACurrentMediaTime()
        let t = Timer(timeInterval: 1.0 / 60, repeats: true) { [weak self] _ in self?.tick() }
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    func pause() {
        timer?.invalidate()
        timer = nil
    }

    func release() { pause() }

    private func tick() {
        let now = CACurrentMediaTime()
        current += now - lastTick
        lastTick = now
        if current >= duration {
            current = duration
            pause()
        }
        show()
    }

    private func show() {
        let i = clip.index(at: current)
        guard i != shown else { return }
        shown = i
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        view.layer?.contents = clip.image(i)
        CATransaction.commit()
    }
}

// MARK: - Export

enum RecordingTrimmer {
    struct Failure: LocalizedError {
        let message: String
        var errorDescription: String? { message }
    }

    /// Writes the kept parts of `source` to `output`. MP4 is re-encoded, so a cut lands on any frame.
    static func export(source: URL, to output: URL, cuts: CutList, progress: @escaping (Double) -> Void) async throws {
        try? FileManager.default.removeItem(at: output)
        if source.pathExtension.lowercased() == "gif" {
            try await Task.detached(priority: .userInitiated) { try exportGif(source: source, to: output, cuts: cuts, progress: progress) }.value
        } else {
            try await exportVideo(source: source, to: output, cuts: cuts, progress: progress)
        }
    }

    private static func exportVideo(source: URL, to output: URL, cuts: CutList, progress: @escaping (Double) -> Void) async throws {
        let asset = AVURLAsset(url: source)
        let comp = AVMutableComposition()
        let videoTracks = try await asset.loadTracks(withMediaType: .video)
        let audioTracks = try await asset.loadTracks(withMediaType: .audio)
        guard let v = videoTracks.first,
              let cv = comp.addMutableTrack(withMediaType: .video, preferredTrackID: kCMPersistentTrackID_Invalid) else {
            throw Failure(message: L.t("Could not open this recording:"))
        }
        cv.preferredTransform = try await v.load(.preferredTransform)
        let ca = audioTracks.isEmpty ? nil : comp.addMutableTrack(withMediaType: .audio, preferredTrackID: kCMPersistentTrackID_Invalid)
        var at = CMTime.zero
        for p in cuts.kept {
            let range = CMTimeRange(start: CMTime(seconds: p.sourceStart, preferredTimescale: 600),
                                    end: CMTime(seconds: p.sourceEnd, preferredTimescale: 600))
            try cv.insertTimeRange(range, of: v, at: at)
            if let ca, let a = audioTracks.first { try? ca.insertTimeRange(range, of: a, at: at) }
            at = CMTimeAdd(at, range.duration)
        }
        guard let session = AVAssetExportSession(asset: comp, presetName: AVAssetExportPresetHighestQuality) else {
            throw Failure(message: L.t("Could not save the trimmed recording:"))
        }
        session.outputURL = output
        session.outputFileType = .mp4
        session.shouldOptimizeForNetworkUse = true
        let poll = Timer(timeInterval: 0.1, repeats: true) { _ in progress(Double(session.progress)) }
        RunLoop.main.add(poll, forMode: .common)
        defer { poll.invalidate() }
        await withCheckedContinuation { (c: CheckedContinuation<Void, Never>) in
            session.exportAsynchronously { c.resume() }
        }
        if session.status != .completed {
            throw session.error ?? Failure(message: L.t("Saving the video failed."))
        }
    }

    private static func exportGif(source: URL, to output: URL, cuts: CutList, progress: @escaping (Double) -> Void) throws {
        guard let clip = GifClip(url: source) else { throw Failure(message: L.t("Could not open this recording:")) }
        // Which source frame goes where in the output.
        var frames: [(index: Int, at: Double)] = []
        for i in 0..<clip.count {
            let start = clip.starts[i]
            if start >= cuts.trimEnd { break }
            for t in cuts.frameOutputTimes(from: start, to: start + clip.lengths[i]) { frames.append((i, t)) }
        }
        frames.sort { $0.at < $1.at }
        guard !frames.isEmpty,
              let dest = CGImageDestinationCreateWithURL(output as CFURL, UTType.gif.identifier as CFString, frames.count, nil) else {
            throw Failure(message: L.t("Could not create the GIF file."))
        }
        CGImageDestinationSetProperties(dest, [kCGImagePropertyGIFDictionary as String: [kCGImagePropertyGIFLoopCount as String: 0]] as CFDictionary)
        let total = cuts.keptLength
        for (k, f) in frames.enumerated() {
            let next = k + 1 < frames.count ? frames[k + 1].at : total
            let d = max(0.02, next - f.at)
            guard let img = clip.image(f.index) else { continue }
            let props = [kCGImagePropertyGIFDictionary as String: [kCGImagePropertyGIFDelayTime as String: d,
                                                                    kCGImagePropertyGIFUnclampedDelayTime as String: d]] as CFDictionary
            CGImageDestinationAddImage(dest, img, props)
            if k % 8 == 0 { DispatchQueue.main.async { progress(Double(k) / Double(frames.count)) } }
        }
        guard CGImageDestinationFinalize(dest) else { throw Failure(message: L.t("Could not create the GIF file.")) }
    }
}

// MARK: - Timeline

/// Yellow handles trim the start and end, dragging across the track selects a part to cut out,
/// cut parts show striped red (click one to put it back), and the white playhead follows the
/// preview. Click anywhere to jump there.
final class TrimTimeline: NSView {
    private static let side: CGFloat = 16
    private static let trackTop: CGFloat = 22
    private static let trackHeight: CGFloat = 40
    private static let handleWidth: CGFloat = 10
    private static let grab: CGFloat = 9

    private enum Drag { case none, start, end, playhead, select }

    var cuts = CutList(duration: 1) { didSet { needsDisplay = true } }
    var position = 0.0 { didSet { needsDisplay = true } }
    var selection: TimeRange? { didSet { needsDisplay = true; onSelection?(selection) } }

    var onSeek: ((Double) -> Void)?
    /// The trim or cuts changed; gets the list as it was before (for undo).
    var onCutsChanged: ((CutList) -> Void)?
    var onSelection: ((TimeRange?) -> Void)?

    private var drag = Drag.none
    private var pressX: CGFloat = 0
    private var pressTime = 0.0
    private var moved = false
    private var before: CutList?
    private var hover: Double?

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { false }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override init(frame: NSRect) {
        super.init(frame: frame)
        toolTip = L.t("Drag the yellow handles to trim · Drag across to select a part to cut · Click a cut to put it back")
    }

    required init?(coder: NSCoder) { fatalError() }

    private var trackWidth: CGFloat { max(1, bounds.width - 2 * Self.side) }
    private func x(_ t: Double) -> CGFloat { Self.side + CGFloat(cuts.duration <= 0 ? 0 : t / cuts.duration) * trackWidth }
    private func t(_ x: CGFloat) -> Double { min(max(0, Double((x - Self.side) / trackWidth) * cuts.duration), cuts.duration) }

    override func draw(_ dirtyRect: NSRect) {
        let top = Self.trackTop, h = Self.trackHeight
        let track = CGRect(x: Self.side, y: top, width: trackWidth, height: h)
        Theme.surface.setFill()
        NSBezierPath(roundedRect: track, xRadius: 6, yRadius: 6).fill()

        // Kept parts.
        for p in cuts.kept {
            Theme.blue.setFill()
            CGRect(x: x(p.sourceStart), y: top, width: max(1, x(p.sourceEnd) - x(p.sourceStart)), height: h).fill()
        }

        // Ticks and labels.
        let step = Self.tickStep(cuts.duration, width: trackWidth)
        let label: [NSAttributedString.Key: Any] = [.font: Theme.ui(10.5), .foregroundColor: Theme.muted]
        var tt = 0.0
        while tt <= cuts.duration + 0.0001 {
            let xx = x(tt)
            Theme.muted.withAlphaComponent(0.4).setFill()
            CGRect(x: xx, y: top - 5, width: 1, height: 4).fill()
            let s = CutList.label(tt, tenths: step < 1) as NSString
            let w = s.size(withAttributes: label).width
            if xx - w / 2 >= 0, xx + w / 2 <= bounds.width { s.draw(at: CGPoint(x: xx - w / 2, y: top + h + 4), withAttributes: label) }
            tt += step
        }

        // Cut parts: striped red, orange under the mouse.
        for c in cuts.cuts {
            let r = CGRect(x: x(c.start), y: top, width: max(2, x(c.end) - x(c.start)), height: h)
            let hovered = hover.map { c.contains($0) } ?? false && drag == .none
            (hovered ? Theme.flame : Theme.recordRed).withAlphaComponent(0.7).setFill()
            r.fill()
            NSGraphicsContext.saveGraphicsState()
            NSBezierPath(rect: r).addClip()
            NSColor.black.withAlphaComponent(0.45).setStroke()
            let stripes = NSBezierPath()
            stripes.lineWidth = 2.5
            var sx = r.minX - h
            while sx < r.maxX {
                stripes.move(to: CGPoint(x: sx, y: r.maxY))
                stripes.line(to: CGPoint(x: sx + h, y: r.minY))
                sx += 10
            }
            stripes.stroke()
            NSGraphicsContext.restoreGraphicsState()
            if r.width > 18 {
                let mark = (hovered ? "↺" : "✂") as NSString
                let attrs: [NSAttributedString.Key: Any] = [.font: Theme.ui(15, .bold), .foregroundColor: NSColor.white]
                let ms = mark.size(withAttributes: attrs)
                mark.draw(at: CGPoint(x: r.midX - ms.width / 2, y: r.midY - ms.height / 2), withAttributes: attrs)
            }
        }

        // Trimmed ends: dimmed.
        let a = x(cuts.trimStart), b = x(cuts.trimEnd)
        Theme.night.withAlphaComponent(0.78).setFill()
        if a > track.minX { CGRect(x: track.minX, y: top, width: a - track.minX, height: h).fill() }
        if b < track.maxX { CGRect(x: b, y: top, width: track.maxX - b, height: h).fill() }

        // Selection.
        if let s = selection {
            let r = CGRect(x: x(s.start), y: top + 1, width: max(1, x(s.end) - x(s.start)), height: h - 2)
            let path = NSBezierPath(roundedRect: r, xRadius: 3, yRadius: 3)
            Theme.yellow.withAlphaComponent(0.33).setFill()
            path.fill()
            Theme.yellow.setStroke()
            path.lineWidth = 1.5
            path.stroke()
            let len = CutList.label(s.length) as NSString
            let attrs: [NSAttributedString.Key: Any] = [.font: Theme.ui(11, .semibold), .foregroundColor: Theme.yellow]
            let w = len.size(withAttributes: attrs).width
            len.draw(at: CGPoint(x: min(max(0, r.midX - w / 2), bounds.width - w), y: 2), withAttributes: attrs)
        }

        // Handles (the Sun yellow of the brand).
        drawHandle(at: a, left: true)
        drawHandle(at: b, left: false)

        // Playhead.
        let px = x(position)
        NSColor.white.setFill()
        CGRect(x: px - 1, y: top - 6, width: 2, height: h + 10).fill()
        let knob = NSBezierPath()
        knob.move(to: CGPoint(x: px - 6, y: top - 12))
        knob.line(to: CGPoint(x: px + 6, y: top - 12))
        knob.line(to: CGPoint(x: px, y: top - 4))
        knob.close()
        knob.fill()
        if selection == nil {
            let now = CutList.label(position) as NSString
            let attrs: [NSAttributedString.Key: Any] = [.font: Theme.ui(11, .semibold), .foregroundColor: NSColor.white]
            let w = now.size(withAttributes: attrs).width
            now.draw(at: CGPoint(x: min(max(0, px + 8), bounds.width - w), y: 0), withAttributes: attrs)
        }
    }

    private func drawHandle(at x: CGFloat, left: Bool) {
        let w = Self.handleWidth
        let r = CGRect(x: left ? x - w : x, y: Self.trackTop - 4, width: w, height: Self.trackHeight + 8)
        Theme.yellow.setFill()
        NSBezierPath(roundedRect: r, xRadius: 3, yRadius: 3).fill()
        Theme.night.setFill()
        for dy in [-5.0, 0, 5] {
            CGRect(x: r.midX - 1, y: r.midY + CGFloat(dy) - 1.5, width: 2, height: 3).fill()
        }
    }

    private static func tickStep(_ duration: Double, width: CGFloat) -> Double {
        let target = duration / Double(max(1, width / 70))
        for s in [0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600] where s >= target { return s }
        return 600
    }

    private func hitHandle(_ p: CGPoint) -> Drag {
        if p.y < Self.trackTop - 14 || p.y > Self.trackTop + Self.trackHeight + 6 { return .none }
        let da = abs(p.x - (x(cuts.trimStart) - Self.handleWidth / 2))
        let db = abs(p.x - (x(cuts.trimEnd) + Self.handleWidth / 2))
        if da <= Self.grab && da <= db { return .start }
        if db <= Self.grab { return .end }
        if abs(p.x - x(position)) <= 5 && p.y < Self.trackTop { return .playhead }
        return .none
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(rect: bounds, options: [.mouseMoved, .mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect, .cursorUpdate],
                                       owner: self))
    }

    override func mouseMoved(with event: NSEvent) {
        let p = convert(event.locationInWindow, from: nil)
        hover = p.y >= Self.trackTop && p.y <= Self.trackTop + Self.trackHeight ? t(p.x) : nil
        let h = hitHandle(p)
        if h == .start || h == .end { NSCursor.resizeLeftRight.set() }
        else if let hv = hover, cuts.cut(at: hv) != nil { NSCursor.pointingHand.set() }
        else { NSCursor.iBeam.set() }
        needsDisplay = true
    }

    override func mouseExited(with event: NSEvent) {
        hover = nil
        NSCursor.arrow.set()
        needsDisplay = true
    }

    override func mouseDown(with event: NSEvent) {
        let p = convert(event.locationInWindow, from: nil)
        pressX = p.x
        pressTime = t(p.x)
        moved = false
        drag = hitHandle(p)
        if drag == .none { drag = p.y < Self.trackTop ? .playhead : .select }
        if drag == .start || drag == .end { before = cuts }
        if drag == .playhead { onSeek?(pressTime) }
    }

    override func mouseDragged(with event: NSEvent) {
        let p = convert(event.locationInWindow, from: nil)
        let tt = t(p.x)
        if abs(p.x - pressX) >= 3 { moved = true }
        switch drag {
        case .start:
            cuts.trimStart = tt
            onSeek?(cuts.trimStart)
        case .end:
            cuts.trimEnd = tt
            onSeek?(cuts.trimEnd)
        case .playhead:
            onSeek?(tt)
        case .select where moved:
            let a = max(min(pressTime, tt), cuts.trimStart)
            let b = min(max(pressTime, tt), cuts.trimEnd)
            selection = b > a ? TimeRange(a, b) : nil
            onSeek?(tt)
        default:
            break
        }
        needsDisplay = true
    }

    override func mouseUp(with event: NSEvent) {
        let was = drag
        drag = .none
        if was == .start || was == .end, let before, before != cuts { onCutsChanged?(before) }
        before = nil
        if was == .select && !moved {
            // A click: put back the cut under it, or jump there.
            let prior = cuts
            if cuts.restoreCut(at: pressTime) {
                onCutsChanged?(prior)
            } else {
                selection = nil
                onSeek?(pressTime)
            }
        }
        needsDisplay = true
    }
}

// MARK: - Window

/// The trimmer: trim the start and end, cut parts out of the middle. Nothing is lost: the first
/// save keeps the untouched recording in the hidden .cometsnap folder (name.original.ext) with
/// the cuts (name.cuts.json), so every cut stays adjustable and Reset brings it all back.
final class TrimWindow: NSWindow, NSWindowDelegate {
    private unowned let app: AppController
    let url: URL            // the recording (what Save writes)
    private let source: URL // what is shown and trimmed (the original, once trimmed before)
    private let isGif: Bool
    private var preview: ClipPreview
    private let timeline = TrimTimeline(frame: .zero)
    private let previewBox = PreviewBox()
    private let playButton: SpaceButton
    private let timeLabel = NSTextField(labelWithString: "")
    private let status = NSTextField(labelWithString: "")
    private let busy = NSView()
    private let busyBar = NSProgressIndicator()
    private var saved: CutList
    private var undoStack: [CutList] = []
    private var redoStack: [CutList] = []
    private var ticker: Timer?
    private var working = false
    private var closingAfterSave = false
    private var flashUntil = Date.distantPast
    private var showsPause = false

    private var cuts: CutList {
        get { timeline.cuts }
        set { timeline.cuts = newValue }
    }

    private var dirty: Bool { cuts != saved }

    /// Opens a recording in the trimmer (nil, with a message, when it can't be read).
    @MainActor
    static func open(app: AppController, url: URL) async -> TrimWindow? {
        do {
            let fm = FileManager.default
            guard fm.fileExists(atPath: url.path) else { throw RecordingTrimmer.Failure(message: L.t("The recording is no longer there.")) }
            let original = EditSidecar.originalRecording(for: url)
            let source = fm.fileExists(atPath: original.path) ? original : url
            let preview: ClipPreview
            if url.pathExtension.lowercased() == "gif" {
                guard let clip = GifClip(url: source) else { throw RecordingTrimmer.Failure(message: L.t("Could not open this recording:")) }
                preview = GifPreview(clip: clip)
            } else {
                let asset = AVURLAsset(url: source)
                let duration = try await asset.load(.duration).seconds
                var size = CGSize(width: 1280, height: 720)
                var fps = 30.0
                if let track = try await asset.loadTracks(withMediaType: .video).first {
                    let (natural, transform, rate) = try await track.load(.naturalSize, .preferredTransform, .nominalFrameRate)
                    let r = CGRect(origin: .zero, size: natural).applying(transform)
                    size = CGSize(width: abs(r.width), height: abs(r.height))
                    if rate > 0 { fps = Double(rate) }
                }
                preview = VideoPreview(url: source, duration: duration.isFinite ? duration : 0, size: size, fps: fps)
            }
            var cuts = CutList(duration: preview.duration)
            let cutsFile = EditSidecar.cuts(for: url)
            if source == original, let data = try? Data(contentsOf: cutsFile) {
                cuts = CutList.from(json: data, duration: preview.duration)
            }
            guard cuts.duration > CutList.minPiece else { throw RecordingTrimmer.Failure(message: L.t("This recording is too short to trim.")) }
            return TrimWindow(app: app, url: url, source: source, cuts: cuts, preview: preview)
        } catch {
            Log.info("trimmer: could not open \(url.lastPathComponent): \(error.localizedDescription)")
            NSApp.activate()
            let a = NSAlert()
            a.messageText = L.t("Could not open this recording:")
            a.informativeText = error.localizedDescription
            a.runModal()
            return nil
        }
    }

    private init(app: AppController, url: URL, source: URL, cuts: CutList, preview: ClipPreview) {
        self.app = app
        self.url = url
        self.source = source
        self.preview = preview
        isGif = url.pathExtension.lowercased() == "gif"
        saved = cuts
        playButton = SpaceButton(title: L.t("Play"), symbol: "play.fill", fill: Theme.surface, tip: L.t("Play or pause (Space)"))
        let vf = NSScreen.underMouse.visibleFrame
        let w = min(vf.width * 0.9, max(760, min(1200, preview.size.width / 2 + 80)))
        let h = min(vf.height * 0.9, max(560, (w - 48) * max(0.3, preview.size.height / max(1, preview.size.width)) + 260))
        super.init(contentRect: CGRect(x: vf.midX - w / 2, y: vf.midY - h / 2, width: w, height: h),
                   styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView], backing: .buffered, defer: false)
        titlebarAppearsTransparent = true
        appearance = NSAppearance(named: .darkAqua)
        backgroundColor = Theme.night
        isReleasedWhenClosed = false
        minSize = NSSize(width: 680, height: 460)
        delegate = self
        timeline.cuts = cuts
        build()
        updateTitle()
        updateStatus()
        seek(cuts.trimStart)
    }

    private func build() {
        let root = NSView()
        root.wantsLayer = true
        root.layer?.backgroundColor = Theme.night.cgColor
        contentView = root

        let heading = NSTextField(labelWithString: L.t("TRIM"))
        heading.font = Theme.heading(12)
        heading.textColor = Theme.muted
        let undo = SpaceButton(title: L.t("Undo"), symbol: "arrow.uturn.backward", tip: L.t("Undo (⌘Z)"))
        undo.onClick = { [weak self] in self?.undo() }
        let redo = SpaceButton(title: L.t("Redo"), symbol: "arrow.uturn.forward", tip: L.t("Redo (⇧⌘Z)"))
        redo.onClick = { [weak self] in self?.redo() }
        let reset = SpaceButton(title: L.t("Reset"), symbol: "arrow.counterclockwise", tip: L.t("Keep the whole recording again"))
        reset.onClick = { [weak self] in self?.resetAll() }
        let saveAs = SpaceButton(title: L.t("Save as…"), symbol: "square.and.arrow.down.on.square", tip: L.t("Save the trimmed copy somewhere else (⇧⌘S)"))
        saveAs.onClick = { [weak self] in self?.run { await self?.saveAs() } }
        let save = SpaceButton(title: L.t("Save"), symbol: "square.and.arrow.down", fill: Theme.surface, tip: L.t("Save and close (⌘S)"))
        save.layer?.borderWidth = 1
        save.onClick = { [weak self] in self?.run { await self?.saveAndClose() } }
        let copy = SpaceButton(title: L.t("Copy & close"), symbol: "doc.on.doc", fill: Theme.terracotta,
                               tip: L.t("Save, copy the file to the clipboard, and close (⌘C or ⌘↩)"))
        copy.onClick = { [weak self] in self?.run { await self?.copyAndClose() } }
        let spacer = NSView()
        spacer.setContentHuggingPriority(.init(1), for: .horizontal)
        let top = NSStackView(views: [heading, undo, redo, reset, spacer, saveAs, save, copy])
        top.spacing = 8
        for b in [undo, redo, reset, saveAs, save, copy] {
            b.heightAnchor.constraint(equalToConstant: 32).isActive = true
            b.widthAnchor.constraint(greaterThanOrEqualToConstant: 70).isActive = true
            b.setContentHuggingPriority(.defaultHigh, for: .horizontal)
        }

        previewBox.content = preview.view
        previewBox.aspect = preview.size
        previewBox.toolTip = L.t("Click to play or pause (Space)")
        previewBox.onClick = { [weak self] in self?.togglePlay() }

        playButton.onClick = { [weak self] in self?.togglePlay() }
        timeLabel.font = NSFont.monospacedDigitSystemFont(ofSize: 13, weight: .semibold)
        timeLabel.textColor = .white
        let setIn = SpaceButton(title: L.t("Start here"), symbol: "arrow.right.to.line", tip: L.t("Trim the start to the playhead (I)"))
        setIn.onClick = { [weak self] in self?.setTrim(start: true) }
        let setOut = SpaceButton(title: L.t("End here"), symbol: "arrow.left.to.line", tip: L.t("Trim the end to the playhead (O)"))
        setOut.onClick = { [weak self] in self?.setTrim(start: false) }
        let cutOut = SpaceButton(title: L.t("Cut out"), symbol: "scissors", fill: Theme.terracotta, tip: L.t("Remove the selected part (⌫)"))
        cutOut.onClick = { [weak self] in self?.cutSelection() }
        let spacer2 = NSView()
        spacer2.setContentHuggingPriority(.init(1), for: .horizontal)
        let controls = NSStackView(views: [playButton, timeLabel, spacer2, setIn, setOut, cutOut])
        controls.spacing = 8
        for b in [playButton, setIn, setOut, cutOut] {
            b.heightAnchor.constraint(equalToConstant: 34).isActive = true
            b.widthAnchor.constraint(greaterThanOrEqualToConstant: 96).isActive = true
        }

        timeline.onSeek = { [weak self] t in self?.seek(t) }
        timeline.onCutsChanged = { [weak self] before in self?.changed(before) }
        timeline.onSelection = { [weak self] _ in self?.updateStatus() }

        status.font = Theme.ui(12)
        status.textColor = Theme.muted
        status.lineBreakMode = .byTruncatingTail

        busy.wantsLayer = true
        busy.layer?.backgroundColor = Theme.night.withAlphaComponent(0.82).cgColor
        busy.isHidden = true
        let busyText = NSTextField(labelWithString: L.t("Trimming…"))
        busyText.font = Theme.heading(18)
        busyText.textColor = Theme.yellow
        busyBar.isIndeterminate = false
        busyBar.minValue = 0
        busyBar.maxValue = 1
        busyBar.style = .bar
        let busyStack = NSStackView(views: [busyText, busyBar])
        busyStack.orientation = .vertical
        busyStack.spacing = 12
        busyBar.widthAnchor.constraint(equalToConstant: 260).isActive = true

        for v in [top, previewBox, controls, timeline, status, busy] as [NSView] {
            v.translatesAutoresizingMaskIntoConstraints = false
            root.addSubview(v)
        }
        busyStack.translatesAutoresizingMaskIntoConstraints = false
        busy.addSubview(busyStack)
        NSLayoutConstraint.activate([
            top.topAnchor.constraint(equalTo: root.topAnchor, constant: 34),
            top.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 16),
            top.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -16),
            previewBox.topAnchor.constraint(equalTo: top.bottomAnchor, constant: 12),
            previewBox.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 16),
            previewBox.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -16),
            controls.topAnchor.constraint(equalTo: previewBox.bottomAnchor, constant: 12),
            controls.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 16),
            controls.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -16),
            timeline.topAnchor.constraint(equalTo: controls.bottomAnchor, constant: 8),
            timeline.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 8),
            timeline.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -8),
            timeline.heightAnchor.constraint(equalToConstant: 84),
            status.topAnchor.constraint(equalTo: timeline.bottomAnchor, constant: 2),
            status.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 18),
            status.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -18),
            status.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -12),
            busy.topAnchor.constraint(equalTo: root.topAnchor),
            busy.bottomAnchor.constraint(equalTo: root.bottomAnchor),
            busy.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            busy.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            busyStack.centerXAnchor.constraint(equalTo: busy.centerXAnchor),
            busyStack.centerYAnchor.constraint(equalTo: busy.centerYAnchor),
        ])
    }

    func present() {
        NSApp.activate()
        makeKeyAndOrderFront(nil)
        ticker = Timer(timeInterval: 1.0 / 30, repeats: true) { [weak self] _ in self?.tick() }
        RunLoop.main.add(ticker!, forMode: .common)
        flash(L.t("Drag the yellow handles to trim, or drag across the timeline and press ⌫ to cut"))
    }

    private func updateTitle() {
        title = (dirty ? "• " : "") + url.lastPathComponent + " · " + L.t("Trim") + " · Cometsnap"
        isDocumentEdited = dirty
    }

    // MARK: playback

    private func togglePlay() {
        guard !working else { return }
        if preview.isPlaying {
            preview.pause()
        } else {
            var at = preview.position
            if at >= cuts.trimEnd - 0.05 || cuts.nextKept(at) == nil { at = cuts.trimStart }
            preview.position = cuts.nextKept(at) ?? cuts.trimStart
            preview.play()
        }
        updatePlayButton()
    }

    private func tick() {
        guard !working else { return }
        var at = preview.position
        if preview.isPlaying {
            // Jump over cut parts and stop at the trimmed end.
            let next = cuts.nextKept(at)
            if next == nil || at >= cuts.trimEnd {
                preview.pause()
                preview.position = cuts.trimEnd
                at = cuts.trimEnd
            } else if let n = next, n - at > 0.03 {
                preview.position = n
                at = n
            }
        }
        updatePlayButton()
        showPosition(at)
        if Date() > flashUntil, status.stringValue != statusText { status.stringValue = statusText }
    }

    private func updatePlayButton() {
        let playing = preview.isPlaying
        guard playing != showsPause else { return }
        showsPause = playing
        playButton.update(title: playing ? L.t("Pause") : L.t("Play"), symbol: playing ? "pause.fill" : "play.fill")
    }

    private func showPosition(_ at: Double) {
        timeline.position = at
        timeLabel.stringValue = CutList.label(cuts.toOutput(at)) + " / " + CutList.label(cuts.keptLength)
    }

    private func seek(_ t: Double) {
        let t = min(max(0, t), cuts.duration)
        preview.position = t
        showPosition(t)
    }

    private func step(_ dir: Double, big: Bool) {
        preview.pause()
        updatePlayButton()
        seek(preview.position + dir * (big ? 1 : preview.frameStep))
    }

    // MARK: editing

    private func changed(_ before: CutList) {
        undoStack.append(before)
        redoStack.removeAll()
        afterChange()
    }

    private func afterChange() {
        updateTitle()
        updateStatus()
        showPosition(preview.position)
    }

    private func cutSelection() {
        guard let s = timeline.selection else {
            flash(L.t("First drag across the timeline to select the part to cut"))
            return
        }
        let before = cuts
        var next = cuts
        if !next.addCut(s.start, s.end) {
            flash(s.length < CutList.minPiece ? L.t("That part is too short to cut") : L.t("Something has to stay: you can't cut everything"))
            return
        }
        cuts = next
        timeline.selection = nil
        changed(before)
        flash(L.f("Cut out {0}", CutList.label(s.length)))
    }

    private func setTrim(start: Bool) {
        let before = cuts
        var next = cuts
        if start { next.trimStart = preview.position } else { next.trimEnd = preview.position }
        guard next != before else { return }
        cuts = next
        changed(before)
        flash(start ? L.f("Starts at {0}", CutList.label(cuts.trimStart)) : L.f("Ends at {0}", CutList.label(cuts.trimEnd)))
    }

    private func resetAll() {
        guard !cuts.isUntouched else { return }
        let before = cuts
        cuts = CutList(duration: cuts.duration)
        timeline.selection = nil
        changed(before)
        flash(L.t("The whole recording is back"))
    }

    private func undo() {
        guard let prev = undoStack.popLast() else { NSSound.beep(); return }
        redoStack.append(cuts)
        cuts = prev
        afterChange()
    }

    private func redo() {
        guard let next = redoStack.popLast() else { NSSound.beep(); return }
        undoStack.append(cuts)
        cuts = next
        afterChange()
    }

    private var statusText: String {
        var parts = [cuts.isUntouched ? L.f("{0} · whole recording", CutList.label(cuts.duration))
                                      : L.f("Keeps {0} of {1}", CutList.label(cuts.keptLength), CutList.label(cuts.duration))]
        if cuts.cuts.count == 1 { parts.append(L.t("1 part cut out")) } else if cuts.cuts.count > 1 { parts.append(L.f("{0} parts cut out", cuts.cuts.count)) }
        if let s = timeline.selection { parts.append(L.f("Selected {0}: press ⌫ to cut it", CutList.label(s.length))) }
        return parts.joined(separator: " · ")
    }

    private func updateStatus() {
        if Date() > flashUntil { status.stringValue = statusText }
    }

    private func flash(_ text: String) {
        status.stringValue = text
        flashUntil = Date().addingTimeInterval(2.6)
    }

    override func keyDown(with event: NSEvent) {
        guard !working else { return }
        let mods = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        let chars = event.charactersIgnoringModifiers?.lowercased() ?? ""
        if mods.contains(.command) {
            switch chars {
            case "z": mods.contains(.shift) ? redo() : undo()
            case "y": redo()
            case "s":
                if mods.contains(.shift) { run { await self.saveAs() } } else { run { await self.saveAndClose() } }
            case "c", "\r": run { await self.copyAndClose() }
            default: super.keyDown(with: event)
            }
            return
        }
        switch event.keyCode {
        case 49: togglePlay(); return                         // Space
        case 123: step(-1, big: mods.contains(.shift)); return // ←
        case 124: step(1, big: mods.contains(.shift)); return  // →
        case 51, 117: cutSelection(); return                   // ⌫ / ⌦
        case 53:                                               // Esc
            if timeline.selection != nil { timeline.selection = nil } else { performClose(nil) }
            return
        default: break
        }
        switch chars {
        case "k": togglePlay()
        case "i": setTrim(start: true)
        case "o": setTrim(start: false)
        default: super.keyDown(with: event)
        }
    }

    // MARK: saving

    private func run(_ work: @escaping () async -> Void) {
        guard !working else { return }
        Task { @MainActor in await work() }
    }

    /// Writes the trim over the recording, keeping the untouched original in the sidecar folder.
    private func save() async -> Bool {
        if !dirty { return true }
        preview.pause()
        updatePlayButton()
        let fm = FileManager.default
        let original = EditSidecar.originalRecording(for: url)
        let cutsFile = EditSidecar.cuts(for: url)
        do {
            if cuts.isUntouched {
                // Back to the untouched recording.
                if source == original, fm.fileExists(atPath: original.path) {
                    try? fm.removeItem(at: url)
                    try fm.moveItem(at: original, to: url)
                }
                try? fm.removeItem(at: cutsFile)
            } else {
                let temp = url.deletingLastPathComponent()
                    .appendingPathComponent(url.deletingPathExtension().lastPathComponent + ".trimming." + url.pathExtension)
                guard await export(to: temp) else { return false }
                try fm.createDirectory(at: EditSidecar.folder(for: url), withIntermediateDirectories: true)
                if source == url {
                    try? fm.removeItem(at: original)
                    try fm.moveItem(at: url, to: original)
                } else {
                    try? fm.removeItem(at: url)
                }
                try fm.moveItem(at: temp, to: url)
                try cuts.json().write(to: cutsFile, options: .atomic)
            }
            saved = cuts
            updateTitle()
            app.capturesChanged()
            Log.info("trimmed \(url.lastPathComponent): \(String(format: "%.1f", cuts.keptLength)) s kept")
            return true
        } catch {
            alert(L.t("Could not save the trimmed recording:"), error)
            return false
        }
    }

    /// Writes the kept parts of the source, with the progress overlay.
    private func export(to output: URL) async -> Bool {
        working = true
        busy.isHidden = false
        busyBar.doubleValue = 0
        defer {
            working = false
            busy.isHidden = true
        }
        do {
            try await RecordingTrimmer.export(source: source, to: output, cuts: cuts) { [weak self] p in self?.busyBar.doubleValue = p }
            return true
        } catch {
            try? FileManager.default.removeItem(at: output)
            alert(L.t("Could not save the trimmed recording:"), error)
            return false
        }
    }

    private func alert(_ text: String, _ error: Error) {
        Log.info("trimmer: \(error.localizedDescription)")
        let a = NSAlert()
        a.messageText = text
        a.informativeText = error.localizedDescription
        a.beginSheetModal(for: self)
    }

    private func saveAndClose() async {
        guard await save() else { return }
        flyHome()
        closeNow()
    }

    private func copyAndClose() async {
        guard await save() else { return }
        Clipboard.copyFile(url)
        flyHome()
        closeNow()
    }

    private func saveAs() async {
        preview.pause()
        updatePlayButton()
        let panel = NSSavePanel()
        panel.title = L.t("Save a copy")
        panel.allowedContentTypes = [isGif ? .gif : .mpeg4Movie]
        panel.nameFieldStringValue = url.deletingPathExtension().lastPathComponent + "-trimmed." + url.pathExtension
        panel.directoryURL = url.deletingLastPathComponent()
        guard await panel.beginSheetModal(for: self) == .OK, let dest = panel.url else { return }
        if dest.standardizedFileURL == url.standardizedFileURL {
            await saveAndClose()
            return
        }
        if cuts.isUntouched {
            do {
                try? FileManager.default.removeItem(at: dest)
                try FileManager.default.copyItem(at: source, to: dest)
            } catch {
                alert(L.t("Could not save:"), error)
                return
            }
        } else if !(await export(to: dest)) {
            return
        }
        flyHome()
        closeNow()
    }

    private func flyHome() {
        guard app.settings.cometAnimation else { return }
        CometFlight.fly(from: frame, to: app.statusItem.buttonFrame, image: nil)
    }

    private func closeNow() {
        closingAfterSave = true
        close()
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        if working { return false }
        if closingAfterSave || !dirty { return true }
        let a = NSAlert()
        a.messageText = L.t("Save the trimmed recording?")
        a.addButton(withTitle: L.t("Save"))
        a.addButton(withTitle: L.t("Discard"))
        a.addButton(withTitle: L.t("Cancel"))
        a.beginSheetModal(for: self) { [weak self] resp in
            guard let self else { return }
            switch resp {
            case .alertFirstButtonReturn: self.run { await self.saveAndClose() }
            case .alertSecondButtonReturn: self.closeNow()
            default: break
            }
        }
        return false
    }

    func windowWillClose(_ notification: Notification) {
        ticker?.invalidate()
        ticker = nil
        preview.release()
        app.trimmerClosed(self)
    }
}

/// Holds the preview, fitted to its aspect ratio, and turns a click into play / pause.
final class PreviewBox: NSView {
    var onClick: (() -> Void)?
    var aspect = CGSize(width: 16, height: 9) { didSet { needsLayout = true } }
    var content: NSView? {
        didSet {
            oldValue?.removeFromSuperview()
            if let content { addSubview(content) }
            needsLayout = true
        }
    }

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        layer?.backgroundColor = NSColor.black.cgColor
        layer?.cornerRadius = 8
        layer?.masksToBounds = true
    }

    required init?(coder: NSCoder) { fatalError() }

    override func layout() {
        super.layout()
        guard let content, aspect.width > 0, aspect.height > 0 else { return }
        let s = min(bounds.width / aspect.width, bounds.height / aspect.height)
        let w = aspect.width * s, h = aspect.height * s
        content.frame = CGRect(x: (bounds.width - w) / 2, y: (bounds.height - h) / 2, width: w, height: h)
    }

    override func hitTest(_ point: NSPoint) -> NSView? { frame.contains(point) ? self : nil }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func resetCursorRects() { addCursorRect(bounds, cursor: .pointingHand) }
    override func mouseUp(with event: NSEvent) {
        if bounds.contains(convert(event.locationInWindow, from: nil)) { onClick?() }
    }
}
