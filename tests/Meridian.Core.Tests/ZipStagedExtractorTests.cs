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

    [Fact]
    public async Task RejectsEntryExceedingDeclaredExtractionBudget()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "test.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("large.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("more than five bytes");
            }
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ZipStagedExtractor.ExtractSingleAsync(archive, "large.txt", root, maxUncompressedBytes: 5));
            Assert.False(File.Exists(Path.Combine(root, "large.txt")));
            Assert.Empty(Directory.GetFiles(root, ".meridian-*.tmp"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsNestedEntryWithoutCreatingDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await Assert.ThrowsAsync<NotSupportedException>(() =>
                ZipStagedExtractor.ExtractSingleAsync("not-used.zip", "nested/file.txt", root));
            Assert.False(Directory.Exists(Path.Combine(root, "nested")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
