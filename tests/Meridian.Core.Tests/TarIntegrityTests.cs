using System.Formats.Tar;
using System.IO.Compression;
using Meridian.Core.Archives;
using Xunit;

namespace Meridian.Core.Tests;

public sealed class TarIntegrityTests
{
    [Theory]
    [InlineData(".tar", false)]
    [InlineData(".tgz", false)]
    [InlineData(".tar", true)]
    [InlineData(".tgz", true)]
    public async Task RejectsHiddenDataAndExcessivePaddingWithoutPublishing(string extension, bool excessivePadding)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "bundle" + extension);
            using (var output = File.Create(archive))
            using (Stream stream = extension == ".tgz" ? new GZipStream(output, CompressionLevel.Fastest) : output)
            {
                using (var writer = new TarWriter(stream, leaveOpen: true))
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "one.txt")
                    { DataStream = new MemoryStream(new byte[] { 42 }) });
                stream.Write(excessivePadding ? new byte[1024 * 1024 + 8192] : new byte[] { 1 });
            }
            Assert.Throws<InvalidDataException>(() => TarArchiveCatalog.List(archive));
            await Assert.ThrowsAsync<InvalidDataException>(() => TarTreeExtractor.ExtractAsync(archive, root, "output"));
            Assert.False(Directory.Exists(Path.Combine(root, "output")));
            Assert.Empty(Directory.GetDirectories(root, ".meridian-tar-extract-*"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsCorruptGzipTrailer(bool corruptSize)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "input");
            await File.WriteAllBytesAsync(source, new byte[128 * 1024]);
            var archive = Path.Combine(root, "bundle.tgz");
            await TarArchiveCreator.CreateAsync(archive, new Dictionary<string, string> { ["input"] = source });
            var bytes = await File.ReadAllBytesAsync(archive);
            bytes[bytes.Length - (corruptSize ? 4 : 8)] ^= 0xff;
            await File.WriteAllBytesAsync(archive, bytes);
            Assert.Throws<InvalidDataException>(() => TarArchiveCatalog.List(archive));
            await Assert.ThrowsAsync<InvalidDataException>(() => TarTreeExtractor.ExtractAsync(archive, root, "output"));
            Assert.False(Directory.Exists(Path.Combine(root, "output")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(".tar")]
    [InlineData(".tgz")]
    public async Task ObserverFailuresPreserveSuccessfulReceipt(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "input");
            await File.WriteAllTextAsync(source, "payload");
            var archive = Path.Combine(root, "bundle" + extension);
            await TarArchiveCreator.CreateAsync(archive, new Dictionary<string, string> { ["input"] = source });
            var updates = new List<int>();
            var receipt = await TarTreeExtractor.ExtractAsync(archive, root, "output", progress: new Observer(value =>
            {
                updates.Add(value);
                throw new InvalidOperationException("observer");
            }));
            Assert.Equal(new[] { 99, 100 }, updates);
            Assert.Equal(1, receipt.FileCount);
            Assert.Equal(7, receipt.BytesWritten);
            Assert.Equal("payload", await File.ReadAllTextAsync(Path.Combine(receipt.Directory, "input")));
            Assert.Empty(Directory.GetDirectories(root, ".meridian-tar-extract-*"));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Observer(Action<int> callback) : IProgress<int>
    {
        public void Report(int value) => callback(value);
    }
}
