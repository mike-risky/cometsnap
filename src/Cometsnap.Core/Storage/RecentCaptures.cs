using System.IO;

namespace Cometsnap.Core.Storage;

/// <summary>
/// The newest captures, newest first, for the tray flyout and the Recent menu.
/// Built from the captures folder at startup, then kept up to date as captures are saved.
/// </summary>
public sealed class RecentCaptures
{
    private readonly object _gate = new();
    private readonly List<string> _paths = new();
    private readonly int _capacity;

    public event Action? Changed;

    public RecentCaptures(int capacity) => _capacity = Math.Max(1, capacity);

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate) return _paths.ToArray();
    }

    /// <summary>Scans month folders from newest to oldest until enough images are found.</summary>
    public void Load(string root)
    {
        var found = new List<string>(_capacity);
        try
        {
            if (Directory.Exists(root))
            {
                var folders = Directory.EnumerateDirectories(root)
                    .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                    .Prepend(root);
                foreach (var folder in folders)
                {
                    var files = Directory.EnumerateFiles(folder, "*.png")
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .Select(f => f.FullName);
                    found.AddRange(files);
                    if (found.Count >= _capacity * 2) break;
                }
            }
        }
        catch (Exception)
        {
            // An unreadable folder just means an empty list.
        }

        var newest = found
            .Select(p => (p, t: SafeWriteTime(p)))
            .OrderByDescending(x => x.t)
            .Take(_capacity)
            .Select(x => x.p);

        lock (_gate)
        {
            _paths.Clear();
            _paths.AddRange(newest);
        }
        Changed?.Invoke();
    }

    public void Add(string path)
    {
        lock (_gate)
        {
            _paths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            _paths.Insert(0, path);
            if (_paths.Count > _capacity) _paths.RemoveRange(_capacity, _paths.Count - _capacity);
        }
        Changed?.Invoke();
    }

    public void Remove(string path)
    {
        bool removed;
        lock (_gate) removed = _paths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed) Changed?.Invoke();
    }

    private static DateTime SafeWriteTime(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }
}
