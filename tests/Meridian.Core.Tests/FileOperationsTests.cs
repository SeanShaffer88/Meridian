using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class FileOperationsTests
{
    [Fact]
    public void CopyItem_CopiesFileAndPreservesContents()
    {
        using var temp = new TemporaryDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "source")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(temp.Path, "destination")).FullName;
        var input = Path.Combine(source, "note.txt");
        File.WriteAllText(input, "hello");

        var result = FileOperations.CopyItem(input, destination);

        Assert.Equal("hello", File.ReadAllText(result));
    }

    [Fact]
    public void CopyItem_CopiesNestedDirectoryContents()
    {
        using var temp = new TemporaryDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "source")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(temp.Path, "destination")).FullName;
        Directory.CreateDirectory(Path.Combine(source, "nested", "deeper"));
        File.WriteAllText(Path.Combine(source, "root.txt"), "root");
        File.WriteAllText(Path.Combine(source, "nested", "deeper", "leaf.txt"), "leaf");

        var result = FileOperations.CopyItem(source, destination);

        Assert.Equal("root", File.ReadAllText(Path.Combine(result, "root.txt")));
        Assert.Equal("leaf", File.ReadAllText(Path.Combine(result, "nested", "deeper", "leaf.txt")));
    }

    [Fact]
    public void CopyItem_RefusesToOverwriteExistingFile()
    {
        using var temp = new TemporaryDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "source")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(temp.Path, "destination")).FullName;
        var input = Path.Combine(source, "same.txt");
        var existing = Path.Combine(destination, "same.txt");
        File.WriteAllText(input, "source");
        File.WriteAllText(existing, "destination");

        Assert.Throws<IOException>(() => FileOperations.CopyItem(input, destination));
        Assert.Equal("destination", File.ReadAllText(existing));
    }

    [Fact]
    public void CopyItem_RefusesToOverwriteExistingDirectory()
    {
        using var temp = new TemporaryDirectory();
        var sourceParent = Directory.CreateDirectory(Path.Combine(temp.Path, "source-parent")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(temp.Path, "destination")).FullName;
        var source = Directory.CreateDirectory(Path.Combine(sourceParent, "bundle")).FullName;
        var existing = Directory.CreateDirectory(Path.Combine(destination, "bundle")).FullName;
        File.WriteAllText(Path.Combine(source, "new.txt"), "new");
        File.WriteAllText(Path.Combine(existing, "keep.txt"), "keep");

        Assert.Throws<IOException>(() => FileOperations.CopyItem(source, destination));
        Assert.True(File.Exists(Path.Combine(existing, "keep.txt")));
        Assert.False(File.Exists(Path.Combine(existing, "new.txt")));
    }

    [Fact]
    public void CopyItem_RejectsCopyingDirectoryIntoItself()
    {
        using var temp = new TemporaryDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "folder")).FullName;
        File.WriteAllText(Path.Combine(source, "note.txt"), "keep");

        Assert.Throws<IOException>(() => FileOperations.CopyItem(source, source));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(source, "note.txt")));
        Assert.Empty(Directory.GetDirectories(source));
    }

    [Fact]
    public void CopyItem_RejectsCopyingDirectoryIntoDescendant()
    {
        using var temp = new TemporaryDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "folder")).FullName;
        var descendant = Directory.CreateDirectory(Path.Combine(source, "child")).FullName;
        File.WriteAllText(Path.Combine(source, "note.txt"), "keep");

        Assert.Throws<IOException>(() => FileOperations.CopyItem(source, descendant));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(source, "note.txt")));
        Assert.Empty(Directory.GetDirectories(descendant));
    }

    [Fact]
    public void CopyItem_CancelledBeforeStartLeavesNoStagingItem()
    {
        using var temp = new TemporaryDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "source")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(temp.Path, "destination")).FullName;
        File.WriteAllText(Path.Combine(source, "note.txt"), "hello");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            FileOperations.CopyItem(Path.Combine(source, "note.txt"), destination, cancellation.Token));
        Assert.Empty(Directory.GetFileSystemEntries(destination));
    }

    [Fact]
    public void CopyItem_MissingSourceThrowsFileNotFoundException()
    {
        using var temp = new TemporaryDirectory();
        var destination = Directory.CreateDirectory(Path.Combine(temp.Path, "destination")).FullName;
        var missing = Path.Combine(temp.Path, "gone.txt");

        Assert.Throws<FileNotFoundException>(() => FileOperations.CopyItem(missing, destination));
        Assert.Empty(Directory.GetFileSystemEntries(destination));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"meridian-test-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
