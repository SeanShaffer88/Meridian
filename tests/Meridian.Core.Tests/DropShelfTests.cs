using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class DropShelfTests
{
    [Fact]
    public void RemoveMissing_PrunesOnlyStaleReferences()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-shelf-prune-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var existing = Path.Combine(root, "existing.txt");
            var missing = Path.Combine(root, "missing.txt");
            File.WriteAllText(existing, "preserve");
            var shelf = new DropShelf();
            shelf.Add(missing);
            shelf.Add(existing);

            Assert.Equal(1, shelf.RemoveMissing());
            Assert.Equal(Path.GetFullPath(existing), Assert.Single(shelf.Paths));
            Assert.Equal("preserve", File.ReadAllText(existing));
            Assert.Equal(0, shelf.RemoveMissing());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Add_DeduplicatesNormalizedPathsIgnoringCase()
    {
        var shelf = new DropShelf();
        var path = Path.Combine(Path.GetTempPath(), "meridian-shelf-item.txt");
        Assert.True(shelf.Add(path));
        Assert.False(shelf.Add(Path.Combine(Path.GetDirectoryName(path)!, ".", Path.GetFileName(path).ToUpperInvariant())));
        Assert.Single(shelf.Paths);
    }

    [Fact]
    public void Remove_OnlyChangesCollectionAndPreservesOrder()
    {
        var shelf = new DropShelf();
        var a = Path.Combine(Path.GetTempPath(), "meridian-a");
        var b = Path.Combine(Path.GetTempPath(), "meridian-b");
        shelf.Add(a);
        shelf.Add(b);
        Assert.True(shelf.Remove(a));
        Assert.False(shelf.Remove(a));
        Assert.Equal(Path.GetFullPath(b), Assert.Single(shelf.Paths));
        shelf.Clear();
        Assert.Empty(shelf.Paths);
    }

    [Fact]
    public void Add_DoesNotTouchDiskOrRequireExistingFiles()
    {
        var shelf = new DropShelf();
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.True(shelf.Add(missing));
        Assert.False(File.Exists(missing));
        Assert.False(Directory.Exists(missing));
    }
}
