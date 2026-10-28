namespace Meridian.Services;

/// <summary>
/// Bounded in-memory cache of measured folder sizes, keyed by folder path and
/// validated against the folder's own last-write time: when a folder changes, its
/// write time changes and the stale entry stops matching, so the folder is measured
/// again. Least-recently-used entries are evicted past the cap. Session-only — the
/// durable thumbnail/size database is a later slice.
/// </summary>
public sealed class FolderSizeCache
{
    public const int MaximumEntries = 192;

    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<Entry> _recency = new(); // front = most recently used

    private sealed record Entry(string Path, DateTime FolderLastWriteUtc, FolderSizeResult Result);

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _byPath.Count;
            }
        }
    }

    /// <summary>
    /// Looks up a measured size. Misses when the folder was never measured or its
    /// last-write time no longer matches the stored one (the folder changed).
    /// </summary>
    public bool TryGet(string path, DateTime folderLastWriteUtc, out FolderSizeResult result)
    {
        result = null!;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        lock (_gate)
        {
            if (!_byPath.TryGetValue(path, out var node))
            {
                return false;
            }

            if (node.Value.FolderLastWriteUtc != folderLastWriteUtc)
            {
                return false; // stale — left in place; the next Store replaces it
            }

            _recency.Remove(node);
            _recency.AddFirst(node);
            result = node.Value.Result;
            return true;
        }
    }

    public void Store(string path, DateTime folderLastWriteUtc, FolderSizeResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(result);
        lock (_gate)
        {
            if (_byPath.TryGetValue(path, out var existing))
            {
                _recency.Remove(existing);
                _byPath.Remove(path);
            }

            var node = new LinkedListNode<Entry>(new Entry(path, folderLastWriteUtc, result));
            _recency.AddFirst(node);
            _byPath[path] = node;
            while (_byPath.Count > MaximumEntries)
            {
                var last = _recency.Last!;
                _recency.RemoveLast();
                _byPath.Remove(last.Value.Path);
            }
        }
    }
}
