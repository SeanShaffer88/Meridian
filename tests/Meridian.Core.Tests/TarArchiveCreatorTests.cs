using System.Formats.Tar;
using Meridian.Core.Archives;
using Xunit;

namespace Meridian.Core.Tests;

public sealed class TarArchiveCreatorTests
{
    [Theory]
    [InlineData(".tar")]
    [InlineData(".tar.gz")]
    [InlineData(".tgz")]
    public async Task CreatesCatalogReadableArchive(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "input.txt");
            await File.WriteAllTextAsync(input, "hello tar");
            var archive = Path.Combine(root, "bundle" + extension);
            await TarArchiveCreator.CreateAsync(archive,
                new Dictionary<string, string> { ["docs/input.txt"] = input });
            var entries = TarArchiveCatalog.List(archive);
            Assert.Single(entries);
            Assert.Equal("docs/input.txt", entries[0].Path);
            Assert.Equal(9, entries[0].Bytes);
            Assert.Empty(Directory.GetFiles(root, ".meridian-tar-*"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RefusesToOverwriteExistingArchive()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-existing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "source.txt");
            var archive = Path.Combine(root, "existing.tar");
            await File.WriteAllTextAsync(input, "source");
            await File.WriteAllTextAsync(archive, "original");
            await Assert.ThrowsAsync<IOException>(() =>
                TarArchiveCreator.CreateAsync(archive,
                    new Dictionary<string, string> { ["source.txt"] = input }));
            Assert.Equal("original", await File.ReadAllTextAsync(archive));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
