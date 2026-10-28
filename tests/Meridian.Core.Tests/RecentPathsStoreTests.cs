using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class RecentPathsStoreTests
{
    private static RecentPathsStore NewStore(string root) => new(Path.Combine(root, "recents.json"));

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-recents-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public async Task Record_PutsTheNewestFirst_AndKeepsTheKind()
    {
        var root = NewRoot();
        try
        {
            var store = NewStore(root);
            await store.RecordAsync(@"C:\Docs", isDirectory: true, DateTimeOffset.UnixEpoch);
            await store.RecordAsync(@"C:\Docs\a.txt", isDirectory: false, DateTimeOffset.UnixEpoch.AddMinutes(1));
            Assert.Equal(2, store.Entries.Count);
            Assert.Equal(@"C:\Docs\a.txt", store.Entries[0].Path);
            Assert.False(store.Entries[0].IsDirectory);
            Assert.Equal(@"C:\Docs", store.Entries[1].Path);
            Assert.True(store.Entries[1].IsDirectory);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Record_SamePath_MovesItToTheFrontWithoutDuplicating()
    {
        var root = NewRoot();
        try
        {
            var store = NewStore(root);
            await store.RecordAsync(@"C:\Docs", isDirectory: true, DateTimeOffset.UnixEpoch);
            await store.RecordAsync(@"C:\Pictures", isDirectory: true, DateTimeOffset.UnixEpoch.AddMinutes(1));
            await store.RecordAsync(@"c:\docs", isDirectory: true, DateTimeOffset.UnixEpoch.AddMinutes(2));
            Assert.Equal(2, store.Entries.Count);
            Assert.Equal(@"c:\docs", store.Entries[0].Path);
            Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(2), store.Entries[0].LastUsedUtc);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Record_BeyondTheCap_DropsTheOldest()
    {
        var root = NewRoot();
        try
        {
            var store = NewStore(root);
            for (var i = 0; i < RecentPathsStore.MaximumEntries + 5; i++)
                await store.RecordAsync($@"C:\Folder{i}", isDirectory: true, DateTimeOffset.UnixEpoch.AddMinutes(i));
            Assert.Equal(RecentPathsStore.MaximumEntries, store.Entries.Count);
            Assert.Equal($@"C:\Folder{RecentPathsStore.MaximumEntries + 4}", store.Entries[0].Path);
            Assert.DoesNotContain(store.Entries, entry => entry.Path == @"C:\Folder0");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Record_BlankPath_IsIgnored()
    {
        var root = NewRoot();
        try
        {
            var store = NewStore(root);
            await store.RecordAsync("   ", isDirectory: true);
            Assert.Empty(store.Entries);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Record_ThenReload_RoundTripsThroughTheFile()
    {
        var root = NewRoot();
        try
        {
            var store = NewStore(root);
            await store.RecordAsync(@"C:\Docs", isDirectory: true, DateTimeOffset.UnixEpoch);
            await store.RecordAsync(@"C:\Docs\a.txt", isDirectory: false, DateTimeOffset.UnixEpoch.AddMinutes(1));
            var reloaded = NewStore(root);
            await reloaded.LoadAsync();
            Assert.Equal(2, reloaded.Entries.Count);
            Assert.Equal(@"C:\Docs\a.txt", reloaded.Entries[0].Path);
            Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(1), reloaded.Entries[0].LastUsedUtc);
            Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Load_MissingFile_YieldsAnEmptyList()
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
    public async Task Load_DamagedFile_YieldsAnEmptyList_AndRecordingStillWorks()
    {
        var root = NewRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "recents.json"), "{not json");
            var store = NewStore(root);
            await store.LoadAsync();
            Assert.Empty(store.Entries);
            await store.RecordAsync(@"C:\Docs", isDirectory: true);
            Assert.Single(store.Entries);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Load_SkipsBlankAndDuplicatePaths_AndCapsTheList()
    {
        var root = NewRoot();
        try
        {
            var entries = new List<RecentPathEntry>
            {
                new("", true, DateTimeOffset.UnixEpoch),
                new(@"C:\Docs", true, DateTimeOffset.UnixEpoch),
                new(@"c:\docs", true, DateTimeOffset.UnixEpoch)
            };
            for (var i = 0; i < RecentPathsStore.MaximumEntries + 4; i++)
                entries.Add(new RecentPathEntry($@"C:\Folder{i}", true, DateTimeOffset.UnixEpoch));
            await File.WriteAllTextAsync(Path.Combine(root, "recents.json"),
                System.Text.Json.JsonSerializer.Serialize(entries));
            var store = NewStore(root);
            await store.LoadAsync();
            Assert.Equal(RecentPathsStore.MaximumEntries, store.Entries.Count);
            Assert.Equal(@"C:\Docs", store.Entries[0].Path);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Clear_EmptiesTheList_AndTheEmptyStatePersists()
    {
        var root = NewRoot();
        try
        {
            var store = NewStore(root);
            await store.RecordAsync(@"C:\Docs", isDirectory: true);
            await store.ClearAsync();
            Assert.Empty(store.Entries);
            var reloaded = NewStore(root);
            await reloaded.LoadAsync();
            Assert.Empty(reloaded.Entries);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
