using Meridian.Services;

namespace Meridian.Core.Tests;

/// <summary>
/// The folder-size cache: hits need the folder's exact last-write time, paths are
/// case-insensitive, and the least-recently-used entries fall out past the cap.
/// </summary>
public sealed class FolderSizeCacheTests
{
    private static readonly DateTime Stamp = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly FolderSizeResult Result = new(TotalBytes: 4096, FileCount: 3, DirectoryCount: 1, SkippedCount: 0);

    [Fact]
    public void TryGet_MissesWhenNothingWasStored()
    {
        var cache = new FolderSizeCache();
        Assert.False(cache.TryGet(@"C:\Docs", Stamp, out _));
        Assert.False(cache.TryGet(" ", Stamp, out _));
    }

    [Fact]
    public void StoreThenTryGet_HitsWithTheStoredResult()
    {
        var cache = new FolderSizeCache();
        cache.Store(@"C:\Docs", Stamp, Result);
        Assert.True(cache.TryGet(@"C:\Docs", Stamp, out var found));
        Assert.Equal(Result, found);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void TryGet_MissesWhenTheFolderChangedSinceTheMeasurement()
    {
        var cache = new FolderSizeCache();
        cache.Store(@"C:\Docs", Stamp, Result);
        Assert.False(cache.TryGet(@"C:\Docs", Stamp.AddMinutes(1), out _));
    }

    [Fact]
    public void Paths_AreCaseInsensitive()
    {
        var cache = new FolderSizeCache();
        cache.Store(@"C:\Docs", Stamp, Result);
        Assert.True(cache.TryGet(@"c:\docs", Stamp, out _));
    }

    [Fact]
    public void Store_SameFolderTwice_ReplacesTheEntry()
    {
        var cache = new FolderSizeCache();
        var newer = new FolderSizeResult(TotalBytes: 8192, FileCount: 4, DirectoryCount: 1, SkippedCount: 0);
        cache.Store(@"C:\Docs", Stamp, Result);
        cache.Store(@"C:\Docs", Stamp.AddMinutes(5), newer);
        Assert.Equal(1, cache.Count);
        Assert.False(cache.TryGet(@"C:\Docs", Stamp, out _));
        Assert.True(cache.TryGet(@"C:\Docs", Stamp.AddMinutes(5), out var found));
        Assert.Equal(newer, found);
    }

    [Fact]
    public void Eviction_PastTheCap_RemovesTheLeastRecentlyUsed()
    {
        var cache = new FolderSizeCache();
        for (var i = 0; i < FolderSizeCache.MaximumEntries + 10; i++)
        {
            cache.Store($@"C:\Folder{i}", Stamp, Result);
        }

        Assert.Equal(FolderSizeCache.MaximumEntries, cache.Count);
        Assert.False(cache.TryGet(@"C:\Folder0", Stamp, out _));
        Assert.True(cache.TryGet($@"C:\Folder{FolderSizeCache.MaximumEntries + 9}", Stamp, out _));
    }

    [Fact]
    public void TryGet_PromotesTheEntry_SoItSurvivesTheNextEviction()
    {
        var cache = new FolderSizeCache();
        for (var i = 0; i < FolderSizeCache.MaximumEntries; i++)
        {
            cache.Store($@"C:\Folder{i}", Stamp, Result);
        }

        Assert.True(cache.TryGet(@"C:\Folder0", Stamp, out _)); // Folder0 is now most-recently-used
        cache.Store(@"C:\BrandNew", Stamp, Result);
        Assert.True(cache.TryGet(@"C:\Folder0", Stamp, out _));
        Assert.False(cache.TryGet(@"C:\Folder1", Stamp, out _)); // Folder1 became the oldest instead
    }
}
