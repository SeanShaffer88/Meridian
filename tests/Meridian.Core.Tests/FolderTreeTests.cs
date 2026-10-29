using Meridian.Services;

namespace Meridian.Tests;

public sealed class FolderTreeTests
{
    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "MeridianFolderTreeTests-" + Guid.NewGuid().ToString("N"))).FullName;
            Zeta = Directory.CreateDirectory(Path.Combine(Root, "zeta")).FullName;
            Alpha = Directory.CreateDirectory(Path.Combine(Root, "Alpha")).FullName;
            Deep = Directory.CreateDirectory(Path.Combine(Root, "Alpha", "Deep")).FullName;
            File.WriteAllText(Path.Combine(Root, "notes.txt"), "not a folder");
        }

        public string Root { get; }
        public string Zeta { get; }
        public string Alpha { get; }
        public string Deep { get; }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void AncestorChain_RunsRootFirstDownToTheFolderItself()
    {
        using var fixture = new Fixture();
        var root = Path.GetPathRoot(fixture.Root)!;

        var chain = FolderTree.AncestorChain(fixture.Deep);

        Assert.Equal(root, chain[0]);
        Assert.Equal(fixture.Deep, chain[^1]);
        // Every step is the parent of the next, so expanding in order walks
        // the tree straight to the folder.
        for (var i = 1; i < chain.Count; i++)
            Assert.Equal(chain[i - 1], Path.GetDirectoryName(chain[i]));
        Assert.Contains(fixture.Alpha, chain);
        Assert.Contains(fixture.Root, chain);
    }

    [Fact]
    public void AncestorChain_OfARootIsJustTheRoot()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.Equal([root], FolderTree.AncestorChain(root));
        Assert.Equal(root, FolderTree.RootOf(Path.GetTempPath()));
    }

    [Fact]
    public void AncestorChain_RejectsEmptyAndRelativePaths()
    {
        Assert.Empty(FolderTree.AncestorChain(null));
        Assert.Empty(FolderTree.AncestorChain(""));
        Assert.Empty(FolderTree.AncestorChain("relative"));
        Assert.Null(FolderTree.RootOf(null));
        Assert.Null(FolderTree.RootOf("relative"));
    }

    [Fact]
    public void ChildFolders_ReturnsFoldersOnlyInPaneOrder()
    {
        using var fixture = new Fixture();

        var children = FolderTree.ChildFolders(fixture.Root);

        // The pane lists folders first, name-sorted ignoring case, so the
        // tree's children are Alpha then zeta; the file never appears.
        Assert.Equal(["Alpha", "zeta"], children.Select(entry => entry.Name));
        Assert.All(children, entry => Assert.True(entry.IsDirectory));
    }

    [Fact]
    public void HasChildFolders_SeesFoldersAndSurvivesMissingPaths()
    {
        using var fixture = new Fixture();
        Assert.True(FolderTree.HasChildFolders(fixture.Root));
        Assert.True(FolderTree.HasChildFolders(fixture.Alpha));
        Assert.False(FolderTree.HasChildFolders(fixture.Zeta));
        Assert.False(FolderTree.HasChildFolders(Path.Combine(fixture.Root, "missing")));
        Assert.False(FolderTree.HasChildFolders(""));
    }
}
