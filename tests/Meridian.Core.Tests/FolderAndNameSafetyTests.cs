using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class FolderAndNameSafetyTests
{
    public static IEnumerable<object[]> UnsafeNames()
    {
        foreach (var name in new[] { "after.", "CON", "con.txt", "NUL.tar.gz", "NUL .txt",
            "PRN", "AUX.log", "COM9", "COM¹.log", "LPT²", "LPT³.txt", "name:stream",
            "folder/child", "folder\\child", " leading", "trailing ", "bad\0name", new string('a', 256) })
        foreach (var rename in new[] { false, true })
            yield return new object[] { name, rename };
    }

    [Theory]
    [MemberData(nameof(UnsafeNames))]
    public void InvalidNamesLeaveSourceAndParentUnchanged(string name, bool rename)
    {
        using var temp = new Fixture();
        var source = Path.Combine(temp.Root, "before.txt");
        File.WriteAllText(source, "keep");
        Assert.Throws<ArgumentException>(() =>
        {
            if (rename) FileOperations.RenameItem(source, name);
            else FileOperations.CreateFolder(temp.Root, name);
        });
        Assert.Equal("keep", File.ReadAllText(source));
        Assert.Equal(new[] { source }, Directory.GetFileSystemEntries(temp.Root));
    }

    [Theory]
    [InlineData("résumé-文件")]
    [InlineData(".settings")]
    [InlineData("CONfig.data")]
    public void CreatesExactlyOneFolder(string name)
    {
        using var temp = new Fixture();
        var result = FileOperations.CreateFolder(temp.Root, name);
        Assert.Equal(Path.Combine(temp.Root, name), result);
        Assert.True(Directory.Exists(result));
        Assert.Equal(new[] { result }, Directory.GetFileSystemEntries(temp.Root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingItemIsNotReportedAsCreatedOrModified(bool folder)
    {
        using var temp = new Fixture();
        var existing = Path.Combine(temp.Root, "existing");
        if (folder) Directory.CreateDirectory(existing);
        var content = folder ? Path.Combine(existing, "keep.txt") : existing;
        File.WriteAllText(content, "original");
        Assert.Throws<IOException>(() => FileOperations.CreateFolder(temp.Root, "existing"));
        Assert.Equal("original", File.ReadAllText(content));
        Assert.Equal(new[] { existing }, Directory.GetFileSystemEntries(temp.Root));
    }

    [Fact]
    public async Task ConcurrentCreationHasExactlyOneWinner()
    {
        using var temp = new Fixture();
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            try { FileOperations.CreateFolder(temp.Root, "new"); return true; }
            catch (IOException ex) when (ex.InnerException is System.ComponentModel.Win32Exception { NativeErrorCode: 183 })
            { return false; }
        })));
        Assert.Equal(1, results.Count(created => created));
        Assert.Equal(15, results.Count(created => !created));
        Assert.Equal(new[] { Path.Combine(temp.Root, "new") }, Directory.GetFileSystemEntries(temp.Root));
    }

    [Fact]
    public void MissingParentIsNotCreatedRecursively()
    {
        using var temp = new Fixture();
        Assert.Throws<DirectoryNotFoundException>(() =>
            FileOperations.CreateFolder(Path.Combine(temp.Root, "missing"), "child"));
        Assert.Empty(Directory.GetFileSystemEntries(temp.Root));
    }

    [Fact]
    public void SupportsLongParentPaths()
    {
        using var temp = new Fixture();
        var parent = temp.Root;
        while (parent.Length < 280) parent = Path.Combine(parent, new string('a', 50));
        Directory.CreateDirectory(parent);
        var result = FileOperations.CreateFolder(parent, "文件");
        Assert.True(Directory.Exists(result));
        Assert.Single(Directory.GetFileSystemEntries(parent));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "meridian-name-" + Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
