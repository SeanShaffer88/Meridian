using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class DirectoryReaderTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Read_CancelledRequestDoesNotReturnEmptyListingOrMissingFolderError(bool folderExists)
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-test-{Guid.NewGuid():N}");
        if (folderExists) Directory.CreateDirectory(root);
        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var error = Assert.Throws<OperationCanceledException>(() => DirectoryReader.Read(root, cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root); }
    }

    [Fact]
    public void Read_ReturnsDirectoriesBeforeFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "z-folder"));
            File.WriteAllText(Path.Combine(root, "a.txt"), "hello");
            var entries = DirectoryReader.Read(root);
            Assert.Equal("z-folder", entries[0].Name);
            Assert.Equal("a.txt", entries[1].Name);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Read_CancelledTokenThrowsOperationCanceledException()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "note.txt"), "hello");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() => DirectoryReader.Read(root, cancellation.Token));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Filter_MatchesNameCaseInsensitivelyAndKeepsFoldersFirst()
    {
        var entries = new[]
        {
            new Meridian.Models.FileEntry { Name = "z-note.txt", FullPath = @"C:\work\z-note.txt", IsDirectory = false, TypeLabel = "TXT file", SizeLabel = "1 B", ModifiedLabel = "" },
            new Meridian.Models.FileEntry { Name = "AlphaFolder", FullPath = @"C:\work\AlphaFolder", IsDirectory = true, TypeLabel = "Folder", SizeLabel = "—", ModifiedLabel = "" },
            new Meridian.Models.FileEntry { Name = "ALPHA.txt", FullPath = @"C:\work\ALPHA.txt", IsDirectory = false, TypeLabel = "TXT file", SizeLabel = "1 B", ModifiedLabel = "" }
        };

        var results = DirectoryReader.Filter(entries, "alpha");

        Assert.Equal(new[] { "AlphaFolder", "ALPHA.txt" }, results.Select(item => item.Name));
    }

    [Fact]
    public void Filter_MatchesFullPathAndTypeLabel()
    {
        var entries = new[]
        {
            new Meridian.Models.FileEntry { Name = "report.bin", FullPath = @"C:\archive\report.bin", IsDirectory = false, TypeLabel = "BIN file", SizeLabel = "1 B", ModifiedLabel = "" },
            new Meridian.Models.FileEntry { Name = "notes.txt", FullPath = @"C:\work\notes.txt", IsDirectory = false, TypeLabel = "TXT file", SizeLabel = "1 B", ModifiedLabel = "" }
        };

        Assert.Equal("report.bin", Assert.Single(DirectoryReader.Filter(entries, "ARCHIVE")).Name);
        Assert.Equal("notes.txt", Assert.Single(DirectoryReader.Filter(entries, "txt file")).Name);
    }

    [Fact]
    public void Filter_WithoutFullPath_DoesNotMatchSharedParentDirectory()
    {
        var entries = new[]
        {
            new Meridian.Models.FileEntry { Name = "report.bin", FullPath = @"C:\archive\report.bin", IsDirectory = false, TypeLabel = "BIN file", SizeLabel = "", ModifiedLabel = "" },
            new Meridian.Models.FileEntry { Name = "archive-note.txt", IsDirectory = false, FullPath = @"C:\archive\archive-note.txt", TypeLabel = "TXT file", SizeLabel = "", ModifiedLabel = "" }
        };

        var results = DirectoryReader.Filter(entries, "archive", includeFullPath: false);

        Assert.Equal("archive-note.txt", Assert.Single(results).Name);
        Assert.Equal(2, DirectoryReader.Filter(entries, "archive").Count);
    }

    [Fact]
    public void Filter_EmptyQueryReturnsAllSortedItems()
    {
        var entries = new[]
        {
            new Meridian.Models.FileEntry { Name = "b.txt", FullPath = @"C:\work\b.txt", IsDirectory = false, TypeLabel = "TXT file", SizeLabel = "1 B", ModifiedLabel = "" },
            new Meridian.Models.FileEntry { Name = "Folder", FullPath = @"C:\work\Folder", IsDirectory = true, TypeLabel = "Folder", SizeLabel = "—", ModifiedLabel = "" },
            new Meridian.Models.FileEntry { Name = "a.txt", FullPath = @"C:\work\a.txt", IsDirectory = false, TypeLabel = "TXT file", SizeLabel = "1 B", ModifiedLabel = "" }
        };

        Assert.Equal(new[] { "Folder", "a.txt", "b.txt" }, DirectoryReader.Filter(entries, " ").Select(item => item.Name));
    }

    [Theory]
    [InlineData(DirectorySortField.Name, false, "a.txt", "b.txt")]
    [InlineData(DirectorySortField.Name, true, "b.txt", "a.txt")]
    [InlineData(DirectorySortField.Size, false, "b.txt", "a.txt")]
    [InlineData(DirectorySortField.Size, true, "a.txt", "b.txt")]
    [InlineData(DirectorySortField.Modified, false, "a.txt", "b.txt")]
    [InlineData(DirectorySortField.Modified, true, "b.txt", "a.txt")]
    [InlineData(DirectorySortField.Type, false, "b.txt", "a.txt")]
    [InlineData(DirectorySortField.Type, true, "a.txt", "b.txt")]
    public void Filter_SortsWithoutChangingDirectoryFirstGuarantee(DirectorySortField field, bool descending, string first, string second)
    {
        var entries = new[]
        {
            new Meridian.Models.FileEntry { Name = "a.txt", FullPath = "a.txt", IsDirectory = false, TypeLabel = "TXT", SizeLabel = "", SizeBytes = 100, ModifiedUtc = new DateTime(2024, 1, 1), ModifiedLabel = "" },
            new Meridian.Models.FileEntry { Name = "b.txt", FullPath = "b.txt", IsDirectory = false, TypeLabel = "BIN", SizeLabel = "", SizeBytes = 10, ModifiedUtc = new DateTime(2025, 1, 1), ModifiedLabel = "" },
            new Meridian.Models.FileEntry { Name = "folder", FullPath = "folder", IsDirectory = true, TypeLabel = "Folder", SizeLabel = "", ModifiedLabel = "" }
        };
        Assert.Equal(new[] { "folder", first, second }, DirectoryReader.Filter(entries, null, field, descending).Select(item => item.Name));
    }

    [Fact]
    public void FormatSize_FormatsBytesAndKilobytes()
    {
        Assert.Equal("12 B", DirectoryReader.FormatSize(12));
        Assert.Equal("1 KB", DirectoryReader.FormatSize(1024));
    }
}
