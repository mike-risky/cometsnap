import Foundation

/// Which captures are selected in the flyout, Finder style: a plain click picks one,
/// ⌘-click toggles, Shift-click picks a range from the last clicked one, a box picks what it
/// touches, ⌘A picks everything shown. Kept in display order (newest first). Same rules as
/// the Windows and Linux tray (TraySelection.cs).
public final class TraySelection {
    private var items: [String] = []
    private var selected = Set<String>()
    private var boxBase: Set<String>?
    private var anchor: String?

    /// Called whenever the selection changes.
    public var onChange: (() -> Void)?

    public init() {}

    public var count: Int { selected.count }

    public func contains(_ id: String) -> Bool { selected.contains(id) }

    /// The selected captures in display order.
    public var picked: [String] { items.filter { selected.contains($0) } }

    /// The captures shown now (after a search). Selected ones that are gone are dropped.
    public func setItems(_ ordered: [String]) {
        items = ordered
        let shown = Set(ordered)
        let before = selected.count
        selected = selected.filter { shown.contains($0) }
        if let a = anchor, !shown.contains(a) { anchor = nil }
        if selected.count != before { onChange?() }
    }

    /// A click on a capture. `toggle` is ⌘ (Ctrl elsewhere), `range` is Shift. Both: add the range.
    public func click(_ id: String, toggle: Bool = false, range: Bool = false) {
        guard let index = items.firstIndex(of: id) else { return }
        if range, let a = anchor, let ai = items.firstIndex(of: a) {
            if !toggle { selected.removeAll() }
            for i in min(ai, index)...max(ai, index) { selected.insert(items[i]) }
            // The anchor stays put, so Shift-click again changes the end of the range.
        } else if toggle {
            if selected.remove(id) == nil { selected.insert(id) }
            anchor = id
        } else {
            selected = [id]
            anchor = id
        }
        onChange?()
    }

    /// Picks only this capture, unless it is already the only one.
    public func only(_ id: String) {
        if selected == [id] { anchor = id; return }
        click(id)
    }

    public func selectAll() {
        guard !items.isEmpty, selected.count != items.count else { return }
        selected.formUnion(items)
        if anchor == nil { anchor = items.first }
        onChange?()
    }

    public func clear() {
        boxBase = nil
        guard !selected.isEmpty else { return }
        selected.removeAll()
        onChange?()
    }

    /// Starts a box (rubber band) selection. With `add` (⌘ held) the current selection stays.
    public func beginBox(add: Bool) { boxBase = add ? selected : [] }

    /// The box now touches these captures: the selection is what was kept plus these.
    public func updateBox(_ touched: [String]) {
        let shown = Set(items)
        let next = (boxBase ?? []).union(touched.filter { shown.contains($0) })
        guard next != selected else { return }
        selected = next
        if let first = picked.first { anchor = first }
        onChange?()
    }

    public func endBox() { boxBase = nil }

    /// What an action on this capture (drag, copy, trash) works on: every selected capture when
    /// it is part of a selection of several, otherwise just this one.
    public func targets(for id: String) -> [String] {
        selected.count > 1 && selected.contains(id) ? picked : [id]
    }
}
