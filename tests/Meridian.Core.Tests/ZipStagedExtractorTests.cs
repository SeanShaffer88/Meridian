using System.IO.Compression;
using Meridian.Core.Archives;

namespace Meridian.Core.Tests;

public class ZipStagedExtractorTests
{
    [Fact]
    public async Task PublishesCompletedFileWithoutStagingResidue()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "test.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                var item = zip.CreateEntry("example.txt");
                using var writer = new StreamWriter(item.Open());
                writer.Write("hello");
            }
            var size = await ZipStagedExtractor.ExtractSingleAsync(archive, "example.txt", root);
            Assert.Equal(5, size);
            Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(root, "example.txt")));
            Assert.Empty(Directory.GetFiles(root, ".meridian-*.tmp"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task DoesNotReplaceExistingFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "test.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                zip.CreateEntry("example.txt");
            await File.WriteAllTextAsync(Path.Combine(root, "example.txt"), "keep");
            await Assert.ThrowsAsync<IOException>(() =>
                ZipStagedExtractor.ExtractSingleAsync(archive, "example.txt", root));
            Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(root, "example.txt")));
            Assert.Empty(Directory.GetFiles(root, ".meridian-*.tmp"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
