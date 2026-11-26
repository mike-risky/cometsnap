import AppKit
import CometsnapCore

/// The drawing surface. Its size is the (cropped) image in points; it is flipped so view
/// coordinates run top-down like image pixels.
final class CanvasView: NSView, NSTextFieldDelegate {
    let base: CGImage
    let imageScale: CGFloat
    private(set) var doc: AnnotationDocument
    private var undoStack = UndoStack<AnnotationDocument>()

    var tool: EditorTool = .arrow { didSet { if tool != .select { selected = nil }; window?.invalidateCursorRects(for: self) } }
    var color = RGBA.palette[0]
    var strokeWidth: Double = 4
    var onChange: (() -> Void)?
    var onSelection: ((Annotation?) -> Void)?

    private var drawing: Annotation?
    private var dragOrigin: CGPoint?
    private var cropDraft: CGRect?
    private var selected: UUID? { didSet { needsDisplay = true } }
    private var moveStart: CGPoint?
    private var movedOnce = false
    private var textField: NSTextField?
    private var editingID: UUID?

    init(base: CGImage, doc: AnnotationDocument, imageScale: CGFloat) {
        self.base = base
        self.doc = doc
        self.imageScale = imageScale
        super.init(frame: .zero)
        updateSize()
    }

    required init?(coder: NSCoder) { fatalError() }

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    var cropRect: CGRect {
        let full = CGRect(x: 0, y: 0, width: base.width, height: base.height)
        return (doc.crop ?? full).intersection(full)
    }

    private func updateSize() {
        let c = cropRect
        setFrameSize(NSSize(width: c.width / imageScale, height: c.height / imageScale))
    }

    /// View point → image pixel.
    private func pixel(_ p: CGPoint) -> CGPoint {
        CGPoint(x: p.x * imageScale + cropRect.minX, y: p.y * imageScale + cropRect.minY)
    }

    /// Image pixel → view point.
    private func viewPoint(_ p: CGPoint) -> CGPoint {
        CGPoint(x: (p.x - cropRect.minX) / imageScale, y: (p.y - cropRect.minY) / imageScale)
    }

    private func viewRect(_ r: CGRect) -> CGRect {
        let o = viewPoint(r.origin)
        return CGRect(x: o.x, y: o.y, width: r.width / imageScale, height: r.height / imageScale)
    }

    override func resetCursorRects() {
        addCursorRect(bounds, cursor: tool == .select ? .arrow : (tool == .text ? .iBeam : .crosshair))
    }

    // MARK: drawing

    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        ctx.saveGState()
        ctx.scaleBy(x: 1 / imageScale, y: 1 / imageScale)
        ctx.translateBy(x: -cropRect.minX, y: -cropRect.minY)
        var shown = doc
        if let d = drawing { shown.annotations.append(d) }
        AnnotationRenderer.draw(base: base, doc: shown, ctx: ctx, skip: editingID)
        ctx.restoreGState()

        if let id = selected, let a = doc.annotations.first(where: { $0.id == id }) {
            var r = viewRect(a.kind == .callout ? AnnotationRenderer.calloutBubble(a).union(a.box) : a.bounds).insetBy(dx: -5, dy: -5)
            if r.width < 10 { r = r.insetBy(dx: -5, dy: 0) }
            let path = NSBezierPath(roundedRect: r, xRadius: 4, yRadius: 4)
            path.lineWidth = 1.5
            path.setLineDash([5, 3], count: 2, phase: 0)
            Theme.blue.setStroke()
            path.stroke()
        }
        if let c = cropDraft {
            let r = viewRect(c)
            let dim = NSBezierPath(rect: bounds)
            dim.append(NSBezierPath(rect: r))
            dim.windingRule = .evenOdd
            NSColor.black.withAlphaComponent(0.5).setFill()
            dim.fill()
            let edge = NSBezierPath(rect: r)
            edge.lineWidth = 1.5
            Theme.yellow.setStroke()
            edge.stroke()
        }
    }

    // MARK: mouse

    override func mouseDown(with event: NSEvent) {
        window?.makeFirstResponder(self)
        commitText()
        let p = pixel(convert(event.locationInWindow, from: nil))
        dragOrigin = p
        switch tool {
        case .select:
            if let i = doc.hit(p, tolerance: 6 * imageScale) {
                selected = doc.annotations[i].id
                onSelection?(doc.annotations[i])
                moveStart = p
                movedOnce = false
                if event.clickCount == 2, [.text, .callout].contains(doc.annotations[i].kind) { beginText(for: doc.annotations[i].id) }
            } else {
                selected = nil
            }
        case .step:
            pushUndo()
            let a = Annotation(kind: .step, points: [Pt(p)], color: color, width: strokeWidth, number: doc.nextStepNumber)
            doc.annotations.append(a)
            changed()
        case .text:
            // Click an existing text to edit it, elsewhere to start a new one.
            if let i = doc.hit(p, tolerance: 4 * imageScale), doc.annotations[i].kind == .text {
                beginText(for: doc.annotations[i].id)
                return
            }
            pushUndo()
            let a = Annotation(kind: .text, points: [Pt(p)], color: color, width: strokeWidth)
            doc.annotations.append(a)
            beginText(for: a.id)
        case .crop:
            cropDraft = CGRect(origin: p, size: .zero)
        default:
            guard let kind = shapeKind(tool) else { return }
            drawing = Annotation(kind: kind, points: [Pt(p), Pt(p)], color: color, width: strokeWidth)
        }
        needsDisplay = true
    }

    override func mouseDragged(with event: NSEvent) {
        let p = pixel(convert(event.locationInWindow, from: nil))
        let shift = event.modifierFlags.contains(.shift)
        guard let origin = dragOrigin else { return }
        switch tool {
        case .select:
            guard let id = selected, let start = moveStart, let i = doc.annotations.firstIndex(where: { $0.id == id }) else { return }
            if !movedOnce { pushUndo(); movedOnce = true }
            doc.annotations[i].move(by: CGSize(width: p.x - start.x, height: p.y - start.y))
            moveStart = p
            changed()
        case .crop:
            cropDraft = CGRect(corner: origin, corner: p).intersection(cropRect)
        default:
            guard var d = drawing else { return }
            switch d.kind {
            case .pen, .highlighter:
                if let last = d.points.last, hypot(last.x - p.x, last.y - p.y) >= 1.5 { d.points.append(Pt(p)) }
            case .arrow, .line:
                d.points[1] = Pt(shift ? Angles.snap45(from: origin, to: p) : p)
            case .rectangle, .ellipse:
                d.points[1] = Pt(shift ? Angles.square(from: origin, to: p) : p)
            default:
                d.points[1] = Pt(p)
            }
            drawing = d
        }
        needsDisplay = true
    }

    override func mouseUp(with event: NSEvent) {
        defer { dragOrigin = nil; moveStart = nil; needsDisplay = true }
        if tool == .crop, let c = cropDraft {
            cropDraft = nil
            if c.width >= 4 && c.height >= 4 {
                pushUndo()
                doc.crop = c.integral
                updateSize()
                changed()
            }
            return
        }
        guard let d = drawing else { return }
        drawing = nil
        let size = d.bounds
        let tiny = size.width < 3 && size.height < 3
        if tiny && ![.pen, .highlighter].contains(d.kind) { return }
        pushUndo()
        doc.annotations.append(d)
        changed()
        if d.kind == .callout { beginText(for: d.id) }
    }

    private func shapeKind(_ t: EditorTool) -> ShapeKind? {
        switch t {
        case .arrow: return .arrow
        case .line: return .line
        case .rectangle: return .rectangle
        case .ellipse: return .ellipse
        case .callout: return .callout
        case .highlighter: return .highlighter
        case .pen: return .pen
        case .blur: return .blur
        case .spotlight: return .spotlight
        default: return nil
        }
    }

    // MARK: text

    var isEditingText: Bool { textField != nil }

    private func beginText(for id: UUID) {
        guard let a = doc.annotations.first(where: { $0.id == id }) else { return }
        editingID = id
        let fontSize = CGFloat(a.fontSize) / imageScale
        let origin: CGPoint
        if a.kind == .callout {
            let b = AnnotationRenderer.calloutBubble(a)
            let pad = CGFloat(a.fontSize) * 0.6
            origin = viewPoint(CGPoint(x: b.minX + pad, y: b.minY + pad * 0.7))
        } else {
            origin = viewPoint(a.start)
        }
        let f = NSTextField(string: a.text)
        f.font = NSFont.systemFont(ofSize: fontSize, weight: a.kind == .callout ? .semibold : .bold)
        f.textColor = a.kind == .callout ? (a.color.isLight ? .black : .white) : NSColor(cgColor: a.color.cgColor)
        f.backgroundColor = a.kind == .callout ? NSColor(cgColor: a.color.cgColor) : NSColor.black.withAlphaComponent(0.25)
        f.drawsBackground = true
        f.isBordered = false
        f.focusRingType = .none
        f.usesSingleLineMode = false
        f.cell?.wraps = false
        f.cell?.isScrollable = true
        f.delegate = self
        f.placeholderString = "Type…"
        f.frame = CGRect(x: origin.x - 2, y: origin.y - 1, width: max(160, f.intrinsicContentSize.width + 40), height: fontSize * 1.5 + 4)
        addSubview(f)
        textField = f
        needsDisplay = true
        window?.makeFirstResponder(f)
    }

    func controlTextDidChange(_ obj: Notification) {
        guard let f = textField else { return }
        let lines = max(1, f.stringValue.components(separatedBy: "\n").count)
        let fontSize = f.font?.pointSize ?? 14
        f.setFrameSize(NSSize(width: max(160, f.intrinsicContentSize.width + 40), height: CGFloat(lines) * fontSize * 1.3 + 6))
    }

    func control(_ control: NSControl, textView: NSTextView, doCommandBy sel: Selector) -> Bool {
        if sel == #selector(NSResponder.insertNewline(_:)) {
            if NSApp.currentEvent?.modifierFlags.contains(.shift) == true || NSApp.currentEvent?.modifierFlags.contains(.option) == true {
                textView.insertNewlineIgnoringFieldEditor(nil)
                controlTextDidChange(Notification(name: NSControl.textDidChangeNotification))
                return true
            }
            commitText()
            return true
        }
        if sel == #selector(NSResponder.cancelOperation(_:)) {
            commitText()
            return true
        }
        return false
    }

    func controlTextDidEndEditing(_ obj: Notification) { commitText() }

    func commitText() {
        guard let f = textField, let id = editingID else { return }
        textField = nil
        editingID = nil
        let text = f.stringValue
        f.removeFromSuperview()
        if let i = doc.annotations.firstIndex(where: { $0.id == id }) {
            if text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                if doc.annotations[i].kind == .text { doc.annotations.remove(at: i) } else { doc.annotations[i].text = "" }
            } else if doc.annotations[i].text != text {
                doc.annotations[i].text = text
            }
        }
        changed()
        window?.makeFirstResponder(self)
        needsDisplay = true
    }

    // MARK: edits

    private func pushUndo() { undoStack.push(doc) }

    private func changed() { onChange?(); needsDisplay = true }

    func undo() {
        commitText()
        guard let prev = undoStack.undo(current: doc) else { NSSound.beep(); return }
        doc = prev
        selected = nil
        updateSize()
        changed()
    }

    func redo() {
        commitText()
        guard let next = undoStack.redo(current: doc) else { NSSound.beep(); return }
        doc = next
        selected = nil
        updateSize()
        changed()
    }

    func deleteSelection() {
        guard let id = selected, let i = doc.annotations.firstIndex(where: { $0.id == id }) else { return }
        pushUndo()
        doc.annotations.remove(at: i)
        selected = nil
        changed()
    }

    func escape() {
        if selected != nil { selected = nil } else if drawing != nil { drawing = nil }
        needsDisplay = true
    }

    func applyStyleToSelection() {
        guard let id = selected, let i = doc.annotations.firstIndex(where: { $0.id == id }) else { return }
        if doc.annotations[i].color == color && doc.annotations[i].width == strokeWidth { return }
        pushUndo()
        doc.annotations[i].color = color
        doc.annotations[i].width = strokeWidth
        changed()
    }
}
