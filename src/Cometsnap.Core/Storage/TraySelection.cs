namespace Cometsnap.Core.Storage;

/// <summary>
/// Which captures are selected in the tray, Explorer style: a plain click picks one,
/// Ctrl+click toggles, Shift+click picks a range from the last clicked one, a box picks
/// what it touches, Ctrl+A picks everything shown. Kept in display order (newest first).
/// No UI in here, so Windows and Linux share it and it can be tested.
/// </summary>
public sealed class TraySelection
{
    private List<string> _items = new();
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string>? _boxBase;
    private string? _anchor;

    /// <summary>Raised whenever the selection changes.</summary>
    public event Action? Changed;

    public int Count => _selected.Count;

    public bool Contains(string path) => _selected.Contains(path);

    /// <summary>The selected captures in display order.</summary>
    public IReadOnlyList<string> Selected => _items.Where(_selected.Contains).ToList();

    /// <summary>The captures shown now (after a search). Selected ones that are gone are dropped.</summary>
    public void SetItems(IEnumerable<string> ordered)
    {
        _items = ordered.ToList();
        var shown = new HashSet<string>(_items, StringComparer.OrdinalIgnoreCase);
        var removed = _selected.RemoveWhere(p => !shown.Contains(p));
        if (_anchor is not null && !shown.Contains(_anchor)) _anchor = null;
        if (removed > 0) Changed?.Invoke();
    }

    /// <summary>
    /// A click on a capture. <paramref name="toggle"/> is Ctrl (⌘ on a Mac), <paramref name="range"/> is Shift.
    /// Ctrl+Shift adds the range to what is already selected.
    /// </summary>
    public void Click(string path, bool toggle = false, bool range = false)
    {
        var index = _items.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;
        if (range && _anchor is not null && _items.FindIndex(p => string.Equals(p, _anchor, StringComparison.OrdinalIgnoreCase)) is var a and >= 0)
        {
            if (!toggle) _selected.Clear();
            for (var i = Math.Min(a, index); i <= Math.Max(a, index); i++) _selected.Add(_items[i]);
            // The anchor stays put, so Shift+click again changes the end of the range.
        }
        else if (toggle)
        {
            if (!_selected.Remove(path)) _selected.Add(path);
            _anchor = path;
        }
        else
        {
            _selected.Clear();
            _selected.Add(path);
            _anchor = path;
        }
        Changed?.Invoke();
    }

    /// <summary>Picks only this capture, unless it is already the only one.</summary>
    public void Only(string path)
    {
        if (_selected.Count == 1 && _selected.Contains(path)) { _anchor = path; return; }
        Click(path);
    }

    public void SelectAll()
    {
        if (_items.Count == 0 || _selected.Count == _items.Count) return;
        _selected.UnionWith(_items);
        _anchor ??= _items[0];
        Changed?.Invoke();
    }

    public void Clear()
    {
        _boxBase = null;
        if (_selected.Count == 0) return;
        _selected.Clear();
        Changed?.Invoke();
    }

    /// <summary>Starts a box (rubber band) selection. With <paramref name="add"/> (Ctrl held) the current selection stays.</summary>
    public void BeginBox(bool add) =>
        _boxBase = add ? new HashSet<string>(_selected, StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The box now touches these captures: the selection is what was kept plus these.</summary>
    public void UpdateBox(IEnumerable<string> touched)
    {
        var next = new HashSet<string>(_boxBase ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
        next.UnionWith(touched.Where(p => _items.Contains(p, StringComparer.OrdinalIgnoreCase)));
        if (next.SetEquals(_selected)) return;
        _selected.Clear();
        _selected.UnionWith(next);
        _anchor = Selected.FirstOrDefault() ?? _anchor;
        Changed?.Invoke();
    }

    public void EndBox() => _boxBase = null;

    /// <summary>
    /// What an action on this capture (drag, copy, delete) works on: every selected capture when
    /// it is part of a selection of several, otherwise just this one.
    /// </summary>
    public IReadOnlyList<string> For(string path) =>
        _selected.Count > 1 && _selected.Contains(path) ? Selected : new[] { path };
}
