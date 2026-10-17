using System.Formats.Tar;
using System.IO.Compression;
using Meridian.Core.Archives;
using Xunit;

namespace Meridian.Core.Tests;

public sealed class TarTreeExtractorTests
{
    [Fact]
    public async Task RejectsTraversalWithoutPublishing()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-traversal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "traversal.tar");
            using (var output = File.Create(archive))
            using (var writer = new TarWriter(output))
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "../outside.txt")
                {
                    DataStream = new MemoryStream(new byte[] { 5 })
                });
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                TarTreeExtractor.ExtractAsync(archive, root, "output"));
            Assert.False(Directory.Exists(Path.Combine(root, "output")));
            Assert.False(File.Exists(Path.Combine(root, "outside.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ReportsCompletionAfterPublishedDirectoryExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "sample.tar");
            using (var output = File.Create(archive))
            using (var writer = new TarWriter(output))
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "one.txt")
                {
                    DataStream = new MemoryStream(new byte[] { 1 })
                });
            var percentages = new List<int>();
            var completionObserved = false;
            var progress = new InlineProgress(value =>
            {
                percentages.Add(value);
                if (value == 100)
                    completionObserved = File.Exists(Path.Combine(root, "output", "one.txt"));
            });
            await TarTreeExtractor.ExtractAsync(archive, root, "output", progress: progress);
            Assert.Equal(new[] { 99, 100 }, percentages);
            Assert.True(completionObserved);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class InlineProgress(Action<int> callback) : IProgress<int>
    {
        public void Report(int value) => callback(value);
    }

    [Fact]
    public async Task CancelledExtractionLeavesNoPublishedOrStagedTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "sample.tar");
            using (var output = File.Create(archive))
            using (var writer = new TarWriter(output))
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "one.txt")
                {
                    DataStream = new MemoryStream(new byte[] { 1 })
                });
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "two.txt")
                {
                    DataStream = new MemoryStream(new byte[] { 2 })
                });
            }
            using var cancellation = new CancellationTokenSource();
            var progress = new InlineProgress(value =>
            {
                if (value > 0) cancellation.Cancel();
            });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                TarTreeExtractor.ExtractAsync(archive, root, "output",
                    cancellationToken: cancellation.Token, progress: progress));
            Assert.False(Directory.Exists(Path.Combine(root, "output")));
            Assert.Empty(Directory.GetDirectories(root, ".meridian-tar-extract-*"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(".tar")]
    [InlineData(".tar.gz")]
    public async Task ExtractsStagedTarTree(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-tree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "source" + extension);
            using (var output = File.Create(archive))
            {
                var compressed = extension == ".tar.gz";
                using Stream stream = compressed ? new GZipStream(output, CompressionLevel.Fastest, true) : output;
                using var writer = new TarWriter(stream, leaveOpen: true);
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "nested/readme.txt")
                {
                    DataStream = new MemoryStream(new byte[] { 1, 2, 3, 4 })
                });
            }
            var receipt = await TarTreeExtractor.ExtractAsync(archive, root, "output");
            Assert.Equal(1, receipt.FileCount);
            Assert.Equal(4, receipt.BytesWritten);
            Assert.Equal(new byte[] { 1, 2, 3, 4 },
                await File.ReadAllBytesAsync(Path.Combine(root, "output", "nested", "readme.txt")));
            Assert.Empty(Directory.GetDirectories(root, ".meridian-tar-extract-*"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RefusesExistingDestination()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-conflict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "source.tar");
            using (var output = File.Create(archive))
            using (var writer = new TarWriter(output))
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "one.txt")
                {
                    DataStream = new MemoryStream(new byte[] { 9 })
                });
            Directory.CreateDirectory(Path.Combine(root, "output"));
            await Assert.ThrowsAsync<IOException>(() =>
                TarTreeExtractor.ExtractAsync(archive, root, "output"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
