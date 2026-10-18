using System.Text.Json;
using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class WorkspaceStoreTests
{
    [Fact]
    public async Task RoundTripsTabsBookmarksAndActiveIndex()
    {
        using var fixture = new Fixture();
        var snapshot = new WorkspaceSnapshot(fixture.Root, fixture.Root,
            [new("Files 文件", fixture.Root)],
            [new("Work", fixture.Root, fixture.Root), new("Other", null, null), new("Third", fixture.Root, null)], 2);
        await fixture.Store.SaveAsync(snapshot);
        var loaded = Assert.IsType<WorkspaceSnapshot>(await fixture.Store.LoadAsync());
        Assert.Equal(snapshot.LeftPath, loaded.LeftPath);
        Assert.Equal(snapshot.RightPath, loaded.RightPath);
        Assert.Equal(snapshot.Bookmarks, loaded.Bookmarks);
        Assert.Equal(snapshot.Tabs, loaded.Tabs);
        Assert.Equal(snapshot.ActiveTabIndex, loaded.ActiveTabIndex);
        Assert.Equal(new[] { fixture.File }, Directory.GetFiles(fixture.Root));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"Bookmarks\":[null]}")]
    [InlineData("{\"Bookmarks\":[{}]}")]
    [InlineData("{\"Tabs\":[null]}")]
    [InlineData("{\"Tabs\":[{\"Name\":\"bad\\u0000name\"}]}")]
    [InlineData("{\"LeftPath\":\"relative/path\"}")]
    [InlineData("{\"ActiveTabIndex\":\"bad\"}")]
    public async Task MalformedStateFallsBackWithoutChangingTheFile(string json)
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.File, json);
        Assert.Null(await fixture.Store.LoadAsync());
        Assert.Equal(json, await File.ReadAllTextAsync(fixture.File));
    }

    [Fact]
    public async Task MissingStateFallsBackWithoutCreatingAFile()
    {
        using var fixture = new Fixture();
        Assert.Null(await fixture.Store.LoadAsync());
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Root));
    }

    [Fact]
    public async Task LegacyStateWithOnlyPanePathsIsStillReadable()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.File, JsonSerializer.Serialize(new { LeftPath = fixture.Root, RightPath = fixture.Root }));
        var loaded = Assert.IsType<WorkspaceSnapshot>(await fixture.Store.LoadAsync());
        Assert.Equal(fixture.Root, loaded.LeftPath);
        Assert.Null(loaded.Tabs);
        Assert.Null(loaded.Bookmarks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExcessiveCollectionsAreNotRestored(bool bookmarks)
    {
        using var fixture = new Fixture();
        var state = bookmarks
            ? new WorkspaceSnapshot(null, null, Enumerable.Repeat(new BookmarkSnapshot("a", fixture.Root), 513).ToArray())
            : new WorkspaceSnapshot(null, null, Tabs: Enumerable.Repeat(new WorkspaceTabSnapshot("a", null, null), 129).ToArray());
        await File.WriteAllTextAsync(fixture.File, JsonSerializer.Serialize(state));
        Assert.Null(await fixture.Store.LoadAsync());
    }

    [Fact]
    public async Task OversizedAndDeeplyNestedFilesAreIgnored()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.File, new string(' ', WorkspaceStore.MaximumStateBytes + 1));
        Assert.Null(await fixture.Store.LoadAsync());
        await File.WriteAllTextAsync(fixture.File, "{\"unknown\":" + new string('[', 32) + "0" + new string(']', 32) + "}");
        Assert.Null(await fixture.Store.LoadAsync());
    }

    [Fact]
    public async Task ExcessivelyLongWhitespaceLabelsAreNotRestored()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.File, JsonSerializer.Serialize(
            new WorkspaceSnapshot(null, null, Tabs: [new(new string(' ', 1025), null, null)])));
        Assert.Null(await fixture.Store.LoadAsync());
    }

    [Fact]
    public async Task InvalidSavePreservesPreviousStateAndLeavesNoStaging()
    {
        using var fixture = new Fixture();
        await fixture.Store.SaveAsync(new(fixture.Root, fixture.Root));
        var original = await File.ReadAllTextAsync(fixture.File);
        await Assert.ThrowsAsync<IOException>(() => fixture.Store.SaveAsync(new(null, null,
            [new BookmarkSnapshot("a", "relative")] )));
        Assert.Equal(original, await File.ReadAllTextAsync(fixture.File));
        Assert.Equal(new[] { fixture.File }, Directory.GetFiles(fixture.Root));
    }

    [Fact]
    public async Task EncodedSizeLimitPreservesPreviousState()
    {
        using var fixture = new Fixture();
        await fixture.Store.SaveAsync(new(fixture.Root, fixture.Root));
        var original = await File.ReadAllTextAsync(fixture.File);
        var longPath = Path.GetPathRoot(fixture.Root)! + new string('a', 30000);
        var state = new WorkspaceSnapshot(null, null, Tabs:
            Enumerable.Repeat(new WorkspaceTabSnapshot("tab", longPath, longPath), 20).ToArray());
        await Assert.ThrowsAsync<IOException>(() => fixture.Store.SaveAsync(state));
        Assert.Equal(original, await File.ReadAllTextAsync(fixture.File));
        Assert.Equal(new[] { fixture.File }, Directory.GetFiles(fixture.Root));
    }

    [Fact]
    public async Task ConcurrentSavesLeaveOneCompleteReadableStateAndNoTemporaryFiles()
    {
        using var fixture = new Fixture();
        await Task.WhenAll(Enumerable.Range(0, 16).Select(index => fixture.Store.SaveAsync(
            new(null, null, Tabs: [new("tab-" + index, fixture.Root, fixture.Root)]))));
        var loaded = Assert.IsType<WorkspaceSnapshot>(await fixture.Store.LoadAsync());
        Assert.StartsWith("tab-", Assert.Single(loaded.Tabs!).Name);
        Assert.Equal(new[] { fixture.File }, Directory.GetFiles(fixture.Root));
    }

    [Fact]
    public async Task FailedPublicationCleansStagingAndDoesNotBlockTheNextSave()
    {
        using var fixture = new Fixture();
        await fixture.Store.SaveAsync(new(fixture.Root, fixture.Root));
        var original = await File.ReadAllTextAsync(fixture.File);
        using (var held = System.IO.File.Open(fixture.File, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failure = await Record.ExceptionAsync(() => fixture.Store.SaveAsync(new(null, null, Tabs: [new("new", null, null)])));
            Assert.True(failure is IOException or UnauthorizedAccessException);
            Assert.Equal(original, await File.ReadAllTextAsync(fixture.File));
            Assert.Equal(new[] { fixture.File }, Directory.GetFiles(fixture.Root));
        }
        await fixture.Store.SaveAsync(new(null, null, Tabs: [new("new", null, null)])).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("new", Assert.Single((await fixture.Store.LoadAsync())!.Tabs!).Name);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "meridian-state-" + Guid.NewGuid().ToString("N"));
        public string File => Path.Combine(Root, "workspace.json");
        public WorkspaceStore Store { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Store = new WorkspaceStore(File);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
