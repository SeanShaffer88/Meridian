using System.IO.Compression;
using Meridian.Core.Archives;

namespace Meridian.Core.Tests;

public class ZipArchiveCreatorTests
{
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
}
