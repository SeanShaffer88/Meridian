using System.Text.Json;
using Meridian.Services;

namespace Meridian.Core.Tests;

/// <summary>
/// The window no longer has workspace tabs: it keeps a single session.
/// These tests pin the migration contract for workspace.json files —
/// a multi-tab file from 0.3.x resolves to the tab that was active,
/// tab-less files resolve to their top-level state, and damaged files
/// still fall back to no saved state at all.
/// </summary>
public sealed class WorkspaceSessionMigrationTests
{
    [Fact]
    public async Task MultiTabFileResolvesToTheActiveTab()
    {
        using var fixture = new Fixture();
        // A 0.3.x-shaped file: no top-level layout names, three tabs,
        // the middle one active. The active tab sets its left path and
        // leaves the right path to the top-level fallback.
        var json = $$"""
            {
              "LeftPath": "{{fixture.Root}}",
              "RightPath": "{{fixture.Root}}",
              "Bookmarks": [],
              "Tabs": [
                { "Name": "First", "LeftPath": null, "RightPath": null, "LeftLayout": "Columns", "RightLayout": "Standard" },
                { "Name": "Active", "LeftPath": "{{fixture.Root}}", "RightPath": null, "LeftLayout": "Standard", "RightLayout": "Columns" },
                { "Name": "Last", "LeftPath": null, "RightPath": null, "LeftLayout": "Columns", "RightLayout": "Columns" }
              ],
              "ActiveTabIndex": 1
            }
            """;
        await File.WriteAllTextAsync(fixture.File, json);
        var loaded = Assert.IsType<WorkspaceSnapshot>(await fixture.Store.LoadAsync());
        var session = WorkspaceStore.ResolveSession(loaded);
        Assert.Equal(fixture.Root, session.LeftPath);
        Assert.Equal(fixture.Root, session.RightPath);
        // Layouts discriminate the index: tab 0 and tab 2 both say Columns/Columns or Columns/Standard.
        Assert.Equal("Standard", session.LeftLayout);
        Assert.Equal("Columns", session.RightLayout);
    }

    [Theory]
    [InlineData(99, "Last")]
    [InlineData(-2, "First")]
    public void OutOfRangeActiveTabIndexClampsToAnEndTab(int activeTabIndex, string expectedName)
    {
        var snapshot = new WorkspaceSnapshot(null, null, Tabs:
        [
            new WorkspaceTabSnapshot("First", "C:\\first", null, "Columns", null),
            new WorkspaceTabSnapshot("Last", "C:\\last", null, "Standard", null)
        ], ActiveTabIndex: activeTabIndex);
        var session = WorkspaceStore.ResolveSession(snapshot);
        var expected = expectedName == "First"
            ? new SessionSnapshot("C:\\first", null, "Columns", null)
            : new SessionSnapshot("C:\\last", null, "Standard", null);
        Assert.Equal(expected, session);
    }

    [Fact]
    public async Task SingleTabFileResolvesToThatTab()
    {
        using var fixture = new Fixture();
        await fixture.Store.SaveAsync(new WorkspaceSnapshot(null, null, Tabs:
            [new WorkspaceTabSnapshot("Only", fixture.Root, fixture.Root, "Columns", "Standard")]));
        var loaded = Assert.IsType<WorkspaceSnapshot>(await fixture.Store.LoadAsync());
        var session = WorkspaceStore.ResolveSession(loaded);
        Assert.Equal(new SessionSnapshot(fixture.Root, fixture.Root, "Columns", "Standard"), session);
    }

    [Fact]
    public async Task TabLessFileResolvesToTopLevelStateAndRoundTripsLayouts()
    {
        using var fixture = new Fixture();
        await fixture.Store.SaveAsync(new WorkspaceSnapshot(fixture.Root, fixture.Root,
            LeftLayout: "Columns", RightLayout: "Standard"));
        var loaded = Assert.IsType<WorkspaceSnapshot>(await fixture.Store.LoadAsync());
        Assert.Equal("Columns", loaded.LeftLayout);
        Assert.Equal("Standard", loaded.RightLayout);
        Assert.Null(loaded.Tabs);
        var session = WorkspaceStore.ResolveSession(loaded);
        Assert.Equal(new SessionSnapshot(fixture.Root, fixture.Root, "Columns", "Standard"), session);
    }

    [Fact]
    public async Task LegacyFileWithOnlyPanePathsResolvesToThosePaths()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.File,
            JsonSerializer.Serialize(new { LeftPath = fixture.Root, RightPath = fixture.Root }));
        var loaded = Assert.IsType<WorkspaceSnapshot>(await fixture.Store.LoadAsync());
        var session = WorkspaceStore.ResolveSession(loaded);
        Assert.Equal(new SessionSnapshot(fixture.Root, fixture.Root, null, null), session);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"Tabs\":[null]}")]
    [InlineData("{\"LeftPath\":\"relative/path\",\"Tabs\":[{\"Name\":\"ok\"}]}")]
    public async Task DamagedFileStillFallsBackToNoSavedState(string json)
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.File, json);
        Assert.Null(await fixture.Store.LoadAsync());
        Assert.Equal(json, await File.ReadAllTextAsync(fixture.File));
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
