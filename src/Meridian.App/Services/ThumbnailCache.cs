using System.Runtime.InteropServices.WindowsRuntime;
using Meridian.Models;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace Meridian.Services;

/// <summary>The two thumbnail sizes the view modes request: grid cells and tiles.</summary>
internal enum ThumbnailSizeClass
{
    Grid,
    Tile,
}

/// <summary>
/// In-memory thumbnail cache for the Grid and Tiles view modes. For the
/// formats ThumbKit decodes itself (BMP/PPM/PNG/JPEG — see
/// <see cref="ThumbKitRouting"/>), the thumbnail comes from ThumbKit first:
/// its from-scratch decoders are fast, its on-disk cache survives restarts,
/// and it never fabricates a placeholder. Every other file — and every
/// ThumbKit failure, including unsupported variants like progressive JPEG —
/// falls back to the shell machinery (StorageFile/StorageFolder
/// .GetThumbnailAsync — images get a bounded decode at the requested size,
/// videos get a poster frame from the shell for free), so ThumbKit is an
/// accelerator, never a single point of failure. The cache is capped by
/// count with least-recently-used eviction; a path that fails or has no
/// thumbnail is remembered as a miss so scrolling does not retry it
/// endlessly. Session-only — the durable cache is ThumbKit's own.
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
        if (!entry.IsDirectory && ThumbKitRouting.IsSupportedPath(entry.FullPath))
        {
            bitmap = await TryLoadViaThumbKitAsync(entry, sizeClass);
        }

        if (bitmap is null)
        {
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
        }

        Store(KeyFor(entry.FullPath, sizeClass), bitmap);
        return bitmap;
    }

    /// <summary>
    /// The ThumbKit attempt: the shared pipeline generates (or serves from
    /// its disk cache) a real decoded thumbnail, and its BMP bytes become
    /// the same BitmapImage the shell path produces. Any failure at all —
    /// pipeline unavailable, the kit declining the file, a decode the
    /// BitmapImage rejects — returns null and the caller runs the shell
    /// path, so a ThumbKit problem can never cost a file its thumbnail.
    /// </summary>
    private async Task<BitmapImage?> TryLoadViaThumbKitAsync(FileEntry entry, ThumbnailSizeClass sizeClass)
    {
        var service = ThumbKitService.Shared;
        if (service is null) return null;
        try
        {
            var requested = (int)RequestedSize(sizeClass);
            var bytes = await service.GetThumbnailBytesAsync(entry.FullPath, requested, requested);
            if (bytes is null || bytes.Length == 0) return null;
            var bitmap = new BitmapImage();
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            System.Runtime.InteropServices.COMException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
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
