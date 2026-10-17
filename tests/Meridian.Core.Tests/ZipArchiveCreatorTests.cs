using System.IO.Compression;
using Meridian.Core.Archives;

namespace Meridian.Core.Tests;

public class ZipArchiveCreatorTests
{
    [Fact]
    public async Task RejectsDuplicateSource()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.txt");
            File.WriteAllText(source, "data");
            var target = Path.Combine(root, "result.zip");
            await Assert.ThrowsAsync<InvalidDataException>(() => ZipArchiveCreator.CreateAsync(target,
                new Dictionary<string, string> { ["one.txt"] = source, ["two.txt"] = source }));
            Assert.False(File.Exists(target));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectsParentAndDescendantFileEntries(bool parentFirst)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "first.txt");
            var second = Path.Combine(root, "second.txt");
            await File.WriteAllTextAsync(first, "first");
            await File.WriteAllTextAsync(second, "second");
            var entries = new Dictionary<string, string>();
            if (parentFirst)
            {
                entries.Add("docs", first);
                entries.Add("docs/file.txt", second);
            }
            else
            {
                entries.Add("docs/file.txt", first);
                entries.Add("docs", second);
            }
            var target = Path.Combine(root, "invalid.zip");
            await Assert.ThrowsAsync<InvalidDataException>(() => ZipArchiveCreator.CreateAsync(target, entries));
            Assert.False(File.Exists(target));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CreatesArchiveFromExplicitFileMap()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "input.txt");
            await File.WriteAllTextAsync(input, "hello");
            var archive = Path.Combine(root, "created.zip");
            await ZipArchiveCreator.CreateAsync(archive,
                new Dictionary<string, string> { ["docs/readme.txt"] = input });
            var entries = ZipArchiveCatalog.List(archive);
            Assert.Single(entries);
            Assert.Equal("docs/readme.txt", entries[0].Path);
            using var zip = ZipFile.OpenRead(archive);
            using var reader = new StreamReader(zip.Entries[0].Open());
            Assert.Equal("hello", reader.ReadToEnd());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ExistingArchiveIsNeverReplaced()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "input.txt");
            var target = Path.Combine(root, "archive.zip");
            await File.WriteAllTextAsync(input, "hello");
            await File.WriteAllTextAsync(target, "original");
            await Assert.ThrowsAsync<IOException>(() =>
                ZipArchiveCreator.CreateAsync(target,
                    new Dictionary<string, string> { ["input.txt"] = input }));
            Assert.Equal("original", await File.ReadAllTextAsync(target));
            Assert.Empty(Directory.GetFiles(root, ".meridian-zip-*.tmp"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsDestinationUsedAsSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var target = Path.Combine(root, "archive.zip");
            await File.WriteAllTextAsync(target, "existing");
            await Assert.ThrowsAsync<ArgumentException>(() =>
                ZipArchiveCreator.CreateAsync(target,
                    new Dictionary<string, string> { ["input.txt"] = target }));
            Assert.Equal("existing", await File.ReadAllTextAsync(target));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsSourceBudgetBeforeCreatingArchive()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "input.txt");
            var target = Path.Combine(root, "archive.zip");
            await File.WriteAllTextAsync(input, "larger than limit");
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ZipArchiveCreator.CreateAsync(target,
                    new Dictionary<string, string> { ["input.txt"] = input }, maxTotalBytes: 2));
            Assert.False(File.Exists(target));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
