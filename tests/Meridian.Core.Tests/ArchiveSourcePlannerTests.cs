using Meridian.Core.Archives;
using Xunit;

namespace Meridian.Core.Tests;

public sealed class ArchiveSourcePlannerTests
{
    [Fact]
    public void RejectsNullSourceCollection()
    {
        Assert.Throws<ArgumentNullException>(() => ArchiveSourcePlanner.Plan(null!));
    }

    [Fact]
    public void RejectsBlankSourcePath()
    {
        Assert.Throws<ArgumentException>(() => ArchiveSourcePlanner.Plan(new[] { " " }));
    }

    [Fact]
    public void RejectsRelativeSourceBeforeFilesystemAccess()
    {
        Assert.Throws<ArgumentException>(() => ArchiveSourcePlanner.Plan(new[] { "relative.txt" }));
    }

    [Fact]
    public void RejectsFileThroughLinkedAncestor()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-linked-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var actual = Path.Combine(root, "actual");
            Directory.CreateDirectory(actual);
            File.WriteAllText(Path.Combine(actual, "data.txt"), "data");
            var link = Path.Combine(root, "linked");
            try { Directory.CreateSymbolicLink(link, actual); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return;
            }
            Assert.Throws<IOException>(() => ArchiveSourcePlanner.Plan(new[] { Path.Combine(link, "data.txt") }));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RejectsFolderThroughLinkedAncestor()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-linked-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var actual = Path.Combine(root, "actual");
            var nested = Path.Combine(actual, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "data.txt"), "data");
            var link = Path.Combine(root, "linked");
            try { Directory.CreateSymbolicLink(link, actual); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return; // Symbolic links are unavailable on this host.
            }
            Assert.Throws<IOException>(() => ArchiveSourcePlanner.Plan(new[] { Path.Combine(link, "nested") }));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RejectsDriveRootBeforeEnumeratingIt()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.Throws<InvalidDataException>(() => ArchiveSourcePlanner.Plan(new[] { root }));
    }

    [Fact]
    public void PlansFilesAndNestedFolderContents()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-plan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var standalone = Path.Combine(root, "one.txt");
            File.WriteAllText(standalone, "one");
            var folder = Path.Combine(root, "nested");
            Directory.CreateDirectory(Path.Combine(folder, "deeper"));
            File.WriteAllText(Path.Combine(folder, "deeper", "two.txt"), "two");
            var result = ArchiveSourcePlanner.Plan(new[] { standalone, folder });
            Assert.Equal(2, result.Count);
            Assert.Contains("one.txt", result.Keys);
            Assert.Contains("nested/deeper/two.txt", result.Keys);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RejectsFileSelectedBothDirectlyAndThroughFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-overlap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var folder = Path.Combine(root, "docs");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, "readme.txt");
            File.WriteAllText(file, "hello");
            Assert.Throws<InvalidDataException>(() =>
                ArchiveSourcePlanner.Plan(new[] { folder, file }));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RejectsRepeatedSelectedFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-duplicate-folder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "file.txt"), "data");
            Assert.Throws<InvalidDataException>(() => ArchiveSourcePlanner.Plan(new[] { root, root }));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsOverlappingSelectedFoldersRegardlessOfOrder(bool parentFirst)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-overlap-folders-" + Guid.NewGuid().ToString("N"));
        var child = Path.Combine(root, "child");
        Directory.CreateDirectory(child);
        try
        {
            File.WriteAllText(Path.Combine(child, "file.txt"), "data");
            var paths = parentFirst ? new[] { root, child } : new[] { child, root };
            Assert.Throws<InvalidDataException>(() => ArchiveSourcePlanner.Plan(paths));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RejectsSelectionContainingOnlyEmptyFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-empty-folder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Throws<InvalidDataException>(() => ArchiveSourcePlanner.Plan(new[] { root }));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RejectsMissingSources()
    {
        Assert.Throws<FileNotFoundException>(() =>
            ArchiveSourcePlanner.Plan(new[] { Path.Combine(Path.GetTempPath(),
                "missing-" + Guid.NewGuid().ToString("N")) }));
    }

    [Fact]
    public void RejectsDuplicateNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "one.txt");
            File.WriteAllText(file, "one");
            Assert.Throws<InvalidDataException>(() => ArchiveSourcePlanner.Plan(new[] { file, file }));
        }
        finally { Directory.Delete(root, true); }
    }
}
