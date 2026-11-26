namespace Cometsnap.Core.Fun;

/// <summary>Watches key presses for ↑ ↑ ↓ ↓ ← → ← → B A.</summary>
public sealed class KonamiCode
{
    private static readonly string[] Sequence = { "Up", "Up", "Down", "Down", "Left", "Right", "Left", "Right", "B", "A" };
    private int _matched;

    /// <summary>Feeds one key name (as WPF's Key.ToString()); true when the code is complete.</summary>
    public bool Push(string key)
    {
        if (string.Equals(key, Sequence[_matched], StringComparison.OrdinalIgnoreCase)) _matched++;
        else _matched = string.Equals(key, Sequence[0], StringComparison.OrdinalIgnoreCase) ? (_matched == 2 ? 2 : 1) : 0;
        if (_matched < Sequence.Length) return false;
        _matched = 0;
        return true;
    }

    public void Reset() => _matched = 0;
}
