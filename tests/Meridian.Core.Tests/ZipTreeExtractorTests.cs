using System.IO.Compression;
using Meridian.Core.Archives;

namespace Meridian.Core.Tests;

public class ZipTreeExtractorTests
{
    [Fact]
    public async Task ReportsProgressForCompletedEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "progress.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                zip.CreateEntry("folder/");
                using var writer = new StreamWriter(zip.CreateEntry("folder/file.txt").Open());
                writer.Write("data");
            }
            var values = new List<int>();
            var observer = new InlineProgress(value => values.Add(value));
            await ZipTreeExtractor.ExtractAsync(archive, root, "output", progress: observer);
            Assert.Equal(new[] { 50, 100 }, values);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class InlineProgress(Action<int> callback) : IProgress<int>
    {
        public void Report(int value) => callback(value);
    }

    [Fact]
    public async Task ExtractsNestedTreeAndPublishesCompletedFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "test.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                zip.CreateEntry("docs/");
                var entry = zip.CreateEntry("docs/readme.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("hello");
            }
            var receipt = await ZipTreeExtractor.ExtractAsync(archive, root, "extracted");
            Assert.Equal(1, receipt.FileCount);
            Assert.Equal(5, receipt.BytesWritten);
            Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(root, "extracted", "docs", "readme.txt")));
            Assert.Empty(Directory.GetDirectories(root, ".meridian-extract-*"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RefusesToMergeWithExistingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "test.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                zip.CreateEntry("a.txt");
            var destination = Path.Combine(root, "existing");
            Directory.CreateDirectory(destination);
            await File.WriteAllTextAsync(Path.Combine(destination, "keep.txt"), "keep");
            await Assert.ThrowsAsync<IOException>(() =>
                ZipTreeExtractor.ExtractAsync(archive, root, "existing"));
            Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(destination, "keep.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsArchiveExceedingBudgetWithoutPublishing()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "test.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("a.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("too long");
            }
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ZipTreeExtractor.ExtractAsync(archive, root, "out", maxTotalBytes: 2));
            Assert.False(Directory.Exists(Path.Combine(root, "out")));
            Assert.Empty(Directory.GetDirectories(root, ".meridian-extract-*"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
