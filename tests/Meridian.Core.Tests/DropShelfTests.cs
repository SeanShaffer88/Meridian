using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class DropShelfTests
{
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
