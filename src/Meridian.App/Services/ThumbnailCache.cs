using Meridian.Models;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Meridian.Services;

/// <summary>The two thumbnail sizes the view modes request: grid cells and tiles.</summary>
internal enum ThumbnailSizeClass
{
    Grid,
    Tile,
}

/// <summary>
/// In-memory thumbnail cache for the Grid and Tiles view modes. Thumbnails come
/// from the same shell machinery the details panel already uses
/// (StorageFile/StorageFolder.GetThumbnailAsync — images get a bounded decode at
/// the requested size, videos get a poster frame from the shell for free), so
/// there is no second decoder path. The cache is capped by count with
/// least-recently-used eviction; a path that fails or has no thumbnail is
/// remembered as a miss so scrolling does not retry it endlessly. Session-only —
/// a durable thumbnail cache is a later slice.
/// </summary>
internal sealed class ThumbnailCache
{
    public const int MaximumEntries = 256;

    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _byKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<CacheEntry> _recency = new(); // front = most recently used

    private sealed record CacheEntry(string Key, BitmapImage? Bitmap);

    private static string KeyFor(string path, ThumbnailSizeClass sizeClass) => $"{sizeClass}:{path}";

    private static uint RequestedSize(ThumbnailSizeClass sizeClass) => sizeClass == ThumbnailSizeClass.Grid ? 192u : 64u;

    /// <summary>
    /// True when this path + size was already resolved — bitmap may still be null,
    /// which means "known to have no thumbnail; keep the glyph".
    /// </summary>
    public bool TryGet(string path, ThumbnailSizeClass sizeClass, out BitmapImage? bitmap)
    {
        bitmap = null;
        var key = KeyFor(path, sizeClass);
        if (!_byKey.TryGetValue(key, out var node))
        {
            return false;
        }

        _recency.Remove(node);
        _recency.AddFirst(node);
        bitmap = node.Value.Bitmap;
        return true;
    }

    /// <summary>Returns the cached thumbnail, loading and caching it on a miss.</summary>
    public async Task<BitmapImage?> LoadAsync(FileEntry entry, ThumbnailSizeClass sizeClass)
    {
        if (TryGet(entry.FullPath, sizeClass, out var cached))
        {
            return cached;
        }

        BitmapImage? bitmap = null;
        try
        {
            var requested = RequestedSize(sizeClass);
            if (entry.IsDirectory)
            {
                var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(entry.FullPath);
                using var thumbnail = await folder.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, requested);
                if (thumbnail is not null)
                {
                    bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(thumbnail);
                }
            }
            else
            {
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(entry.FullPath);
                using var thumbnail = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, requested);
                if (thumbnail is not null)
                {
                    bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(thumbnail);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            System.Runtime.InteropServices.COMException or NotSupportedException)
        {
            bitmap = null;
        }

        Store(KeyFor(entry.FullPath, sizeClass), bitmap);
        return bitmap;
    }

    private void Store(string key, BitmapImage? bitmap)
    {
        if (_byKey.TryGetValue(key, out var existing))
        {
            _recency.Remove(existing);
            _byKey.Remove(key);
        }

        var node = new LinkedListNode<CacheEntry>(new CacheEntry(key, bitmap));
        _recency.AddFirst(node);
        _byKey[key] = node;
        while (_byKey.Count > MaximumEntries)
        {
            var last = _recency.Last!;
            _recency.RemoveLast();
            _byKey.Remove(last.Value.Key);
        }
    }
}
