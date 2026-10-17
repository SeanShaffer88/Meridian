using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class FileOperationsTests
{
    [Fact]
    public void CopyItem_RefusesLinkedDestinationWithoutWriting()
    {
        using var temp = new TemporaryDirectory();
        var real = Directory.CreateDirectory(Path.Combine(temp.Path, "real")).FullName;
        var link = Path.Combine(temp.Path, "linked");
        var source = Path.Combine(temp.Path, "source.txt");
        File.WriteAllText(source, "untouched");
        try { Directory.CreateSymbolicLink(link, real); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return; // Symbolic link creation can be restricted on Windows runners.
        }

        Assert.Throws<IOException>(() => FileOperations.CopyItem(source, link));
        Assert.False(File.Exists(Path.Combine(real, "source.txt")));
        Assert.Equal("untouched", File.ReadAllText(source));
    }

    [Fact]
    public void MoveItem_RefusesLinkedDestinationWithoutMoving()
    {
        using var temp = new TemporaryDirectory();
        var real = Directory.CreateDirectory(Path.Combine(temp.Path, "real")).FullName;
        var link = Path.Combine(temp.Path, "linked");
        var source = Path.Combine(temp.Path, "source.txt");
        File.WriteAllText(source, "untouched");
        try { Directory.CreateSymbolicLink(link, real); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        Assert.Throws<IOException>(() => FileOperations.MoveItem(source, link));
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(Path.Combine(real, "source.txt")));
    }

    [Fact]
    public void RenameItem_RenamesFileWithoutChangingContents()
    {
        using var temp = new TemporaryDirectory();
        var parent = Directory.CreateDirectory(Path.Combine(temp.Path, "parent")).FullName;
        var source = Path.Combine(parent, "before.txt");
        File.WriteAllText(source, "keep this");

        var result = FileOperations.RenameItem(source, "after.txt");

        Assert.Equal(Path.Combine(parent, "after.txt"), result);
        Assert.False(File.Exists(source));
        Assert.Equal("keep this", File.ReadAllText(result));
    }

    [Fact]
    public void RenameItem_RefusesToOverwriteExistingItem()
    {
        using var temp = new TemporaryDirectory();
        var parent = Directory.CreateDirectory(Path.Combine(temp.Path, "parent")).FullName;
        var source = Path.Combine(parent, "before.txt");
        var existing = Path.Combine(parent, "after.txt");
        File.WriteAllText(source, "source");
        File.WriteAllText(existing, "destination");

        Assert.Throws<IOException>(() => FileOperations.RenameItem(source, "after.txt"));
        Assert.Equal("source", File.ReadAllText(source));
        Assert.Equal("destination", File.ReadAllText(existing));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("folder\\name")]
    [InlineData(" folder ")]
    public void RenameItem_RejectsInvalidName(string newName)
    {
        using var temp = new TemporaryDirectory();
        var parent = Directory.CreateDirectory(Path.Combine(temp.Path, "parent")).FullName;
        var source = Path.Combine(parent, "before.txt");
        File.WriteAllText(source, "keep");

        Assert.Throws<ArgumentException>(() => FileOperations.RenameItem(source, newName));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void RenameItem_RenamesDirectoryAndKeepsChildren()
    {
        using var temp = new TemporaryDirectory();
        var parent = Directory.CreateDirectory(Path.Combine(temp.Path, "parent")).FullName;
        var source = Directory.CreateDirectory(Path.Combine(parent, "before")).FullName;
        File.WriteAllText(Path.Combine(source, "inside.txt"), "keep");

        var result = FileOperations.RenameItem(source, "after");

        Assert.False(Directory.Exists(source));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(result, "inside.txt")));
    }

    [Fact]
    public void RenameItem_SupportsCaseOnlyRename()
    {
        using var temp = new TemporaryDirectory();
        var parent = Directory.CreateDirectory(Path.Combine(temp.Path, "parent")).FullName;
        var source = Path.Combine(parent, "before.txt");
        File.WriteAllText(source, "keep");

        var result = FileOperations.RenameItem(source, "Before.txt");

        Assert.Equal("Before.txt", Path.GetFileName(result));
        Assert.Equal("keep", File.ReadAllText(result));
    }

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
    public void CopyItem_PreservesSourceLastWriteTime()
    {
        using var temp = new TemporaryDirectory();
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(temp.Path, "source")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(temp.Path, "destination")).FullName;
        var source = Path.Combine(sourceDirectory, "dated.txt");
        File.WriteAllText(source, "timestamped");
        var expected = DateTime.UtcNow.AddDays(-3);
        File.SetLastWriteTimeUtc(source, expected);

        var result = FileOperations.CopyItem(source, destination);

        Assert.Equal(expected, File.GetLastWriteTimeUtc(result));
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

    [Fact]
    public void MoveItem_MovesFileAndPreservesContents()
    {
        using var temp = new TemporaryDirectory();
        var sourceFolder = Directory.CreateDirectory(Path.Combine(temp.Path, "source")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(temp.Path, "destination")).FullName;
        var source = Path.Combine(sourceFolder, "note.txt");
        File.WriteAllText(source, "keep this");

        var result = FileOperations.MoveItem(source, destination);

        Assert.Equal(Path.Combine(destination, "note.txt"), result);
        Assert.False(File.Exists(source));
        Assert.Equal("keep this", File.ReadAllText(result));
    }

    [Fact]
    public void MoveItem_MovesDirectoryWithChildren()
    {
        using var temp = new TemporaryDirectory();
        var sourceParent = Directory.CreateDirectory(Path.Combine(temp.Path, "source-parent")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(temp.Path, "destination")).FullName;
        var source = Directory.CreateDirectory(Path.Combine(sourceParent, "bundle")).FullName;
        File.WriteAllText(Path.Combine(source, "inside.txt"), "keep");

        var result = FileOperations.MoveItem(source, destination);

        Assert.False(Directory.Exists(source));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(result, "inside.txt")));
    }

    [Fact]
    public void MoveItem_RefusesToOverwriteExistingFile()
    {
        using var temp = new TemporaryDirectory();
        var sourceFolder = Directory.CreateDirectory(Path.Combine(temp.Path, "source")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(temp.Path, "destination")).FullName;
        var source = Path.Combine(sourceFolder, "same.txt");
        var existing = Path.Combine(destination, "same.txt");
        File.WriteAllText(source, "source");
        File.WriteAllText(existing, "destination");

        Assert.Throws<IOException>(() => FileOperations.MoveItem(source, destination));
        Assert.Equal("source", File.ReadAllText(source));
        Assert.Equal("destination", File.ReadAllText(existing));
    }

    [Fact]
    public void MoveItem_RefusesToOverwriteExistingDirectory()
    {
        using var temp = new TemporaryDirectory();
        var sourceParent = Directory.CreateDirectory(Path.Combine(temp.Path, "source-parent")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(temp.Path, "destination")).FullName;
        var source = Directory.CreateDirectory(Path.Combine(sourceParent, "bundle")).FullName;
        var existing = Directory.CreateDirectory(Path.Combine(destination, "bundle")).FullName;
        File.WriteAllText(Path.Combine(source, "new.txt"), "new");
        File.WriteAllText(Path.Combine(existing, "keep.txt"), "keep");

        Assert.Throws<IOException>(() => FileOperations.MoveItem(source, destination));
        Assert.True(File.Exists(Path.Combine(source, "new.txt")));
        Assert.True(File.Exists(Path.Combine(existing, "keep.txt")));
        Assert.False(File.Exists(Path.Combine(existing, "new.txt")));
    }

    [Fact]
    public void MoveItem_RejectsMovingDirectoryIntoDescendant()
    {
        using var temp = new TemporaryDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "folder")).FullName;
        var descendant = Directory.CreateDirectory(Path.Combine(source, "child")).FullName;
        File.WriteAllText(Path.Combine(source, "note.txt"), "keep");

        Assert.Throws<IOException>(() => FileOperations.MoveItem(source, descendant));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(source, "note.txt")));
        Assert.Empty(Directory.GetDirectories(descendant));
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
