using Meridian.Services;

namespace Meridian.Core.Tests;

/// <summary>
/// The per-folder view store: JSON roundtrip, damaged file reads as empty, the cap
/// evicts the least-recently-viewed folders, and re-recording a folder refreshes
/// it in place (case-insensitively) instead of duplicating it.
/// </summary>
public sealed class FolderViewStoreTests
{
    private static readonly FolderViewState State = new(
        Layout: "Columns",
        ViewMode: "Grid",
        SortField: "Size",
        Descending: true,
        Grouping: "Type",
        VisibleColumns: ["Name", "Size"]);

    private static FolderViewStore NewStore(string root) => new(Path.Combine(root, "folder-views.json"));

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-folderviews-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public async Task Find_OnAnEmptyStore_ReturnsNull()
    {
        var root = NewRoot();
        try
        {
            var store = NewStore(root);
            await store.LoadAsync();
            Assert.Null(store.Find(@"C:\Docs"));
            Assert.Null(store.Find(""));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Record_ThenReload_RoundTripsTheState()
    {
        var root = NewRoot();
        try
        {
            var store = NewStore(root);
            await store.RecordAsync(@"C:\Docs", State);

            var reloaded = NewStore(root);
            await reloaded.LoadAsync();
            var found = reloaded.Find(@"C:\Docs");
            Assert.NotNull(found);
            Assert.Equal("Columns", found.Layout);
            Assert.Equal("Grid", found.ViewMode);
            Assert.Equal("Size", found.SortField);
            Assert.True(found.Descending);
            Assert.Equal("Type", found.Grouping);
            Assert.Equal(new[] { "Name", "Size" }, found.VisibleColumns);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Record_SameFolderTwice_RefreshesInsteadOfDuplicating_AndMovesItToTheEnd()
    {
        var root = NewRoot();
        try
        {
            var store = NewStore(root);
            await store.RecordAsync(@"C:\Docs", State);
            await store.RecordAsync(@"C:\Other", State);
            var updated = State with { ViewMode = "Tiles" };
            await store.RecordAsync(@"c:\docs", updated); // different casing, same folder

            Assert.Equal(2, store.Entries.Count);
            Assert.Equal(@"C:\Other", store.Entries[0].Path);
            Assert.Equal("Tiles", store.Find(@"C:\DOCS")?.ViewMode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Load_WithADamagedFile_StartsEmptyWithoutThrowing()
    {
        var root = NewRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "folder-views.json"), "{ this is not json");
            var store = NewStore(root);
            await store.LoadAsync();
            Assert.Empty(store.Entries);
            Assert.Null(store.Find(@"C:\Docs"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Load_WithNoFile_StartsEmpty()
    {
        var root = NewRoot();
        try
        {
            var store = NewStore(root);
            await store.LoadAsync();
            Assert.Empty(store.Entries);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Record_PastTheCap_EvictsTheLeastRecentlyViewedFolders()
    {
        var root = NewRoot();
        try
        {
            var store = NewStore(root);
            for (var i = 0; i < FolderViewStore.MaximumEntries + 5; i++)
            {
                await store.RecordAsync($@"C:\Folder{i}", State);
            }

            Assert.Equal(FolderViewStore.MaximumEntries, store.Entries.Count);
            Assert.Null(store.Find(@"C:\Folder0"));
            Assert.Null(store.Find(@"C:\Folder4"));
            Assert.NotNull(store.Find($@"C:\Folder{FolderViewStore.MaximumEntries + 4}"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Record_WithABlankPath_IsIgnored()
    {
        var root = NewRoot();
        try
        {
            var store = NewStore(root);
            await store.RecordAsync("  ", State);
            Assert.Empty(store.Entries);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
