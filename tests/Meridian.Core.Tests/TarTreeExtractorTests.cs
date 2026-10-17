using System.Formats.Tar;
using System.IO.Compression;
using Meridian.Core.Archives;
using Xunit;

namespace Meridian.Core.Tests;

public sealed class TarTreeExtractorTests
{
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
