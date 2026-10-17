namespace Meridian.Services;

/// <summary>
/// An in-memory, non-owning collection of filesystem paths.
/// Adding and removing entries never moves, copies, or deletes the underlying files.
/// </summary>
public sealed class DropShelf
{
    private readonly List<string> _paths = [];
    public IReadOnlyList<string> Paths => _paths.AsReadOnly();

    public bool Add(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = Path.GetFullPath(path);
        if (_paths.Contains(normalized, StringComparer.OrdinalIgnoreCase)) return false;
        _paths.Add(normalized);
        return true;
    }

    public bool Remove(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = Path.GetFullPath(path);
        var index = _paths.FindIndex(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;
        _paths.RemoveAt(index);
        return true;
    }

    public void Clear() => _paths.Clear();
}
