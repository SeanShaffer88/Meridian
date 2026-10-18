using System.Formats.Tar;
using System.IO.Compression;
using Meridian.Core.Archives;

namespace Meridian.Core.Tests;

public sealed class TarPayloadValidationTests
{
    [Theory]
    [InlineData(".tar")]
    [InlineData(".tgz")]
    public async Task ChunkedCatalogPreservesFollowingEntriesAndExtraction(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var payload = new byte[32769];
            new Random(37).NextBytes(payload);
            var path = Path.Combine(root, "bundle" + extension);
            using (var file = File.Create(path))
            using (Stream stream = extension == ".tgz"
                ? new GZipStream(file, CompressionLevel.Fastest) : file)
            using (var writer = new TarWriter(stream))
            {
                writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "first")
                { DataStream = new MemoryStream(payload) });
                writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "second")
                { DataStream = new MemoryStream(new byte[] { 42 }) });
            }
            var entries = TarArchiveCatalog.List(path);
            Assert.Equal(new[] { "first", "second" }, entries.Select(entry => entry.Path));
            Assert.Equal(payload.Length, entries[0].Bytes);
            var receipt = await TarTreeExtractor.ExtractAsync(path, root, "output");
            Assert.Equal(2, receipt.FileCount);
            Assert.Equal(payload.Length + 1, receipt.BytesWritten);
            Assert.Equal(payload, await File.ReadAllBytesAsync(Path.Combine(receipt.Directory, "first")));
            Assert.Equal(new byte[] { 42 }, await File.ReadAllBytesAsync(Path.Combine(receipt.Directory, "second")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CancellationStopsBeforeNextPayloadRead()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new CancellingStream(new byte[32768], cancellation);
        Assert.Throws<OperationCanceledException>(() => TarArchiveCatalog.ValidatePayload(
            stream, stream.Length, new byte[8192], cancellation.Token));
        Assert.Equal(8192, stream.Position);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8192)]
    public void RejectsShortPayload(int available)
    {
        using var stream = new MemoryStream(new byte[available]);
        Assert.Throws<InvalidDataException>(() => TarArchiveCatalog.ValidatePayload(
            stream, available + 1, new byte[8192], default));
    }

    [Fact]
    public void CatalogRejectsTruncatedSeekablePayload()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".tar");
        try
        {
            using (var output = File.Create(path))
            using (var writer = new TarWriter(output))
                writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "payload")
                { DataStream = new MemoryStream(new byte[16384]) });
            using (var output = File.OpenWrite(path)) output.SetLength(512 + 8192);
            Assert.Throws<InvalidDataException>(() => TarArchiveCatalog.List(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void EmptyRegularFileNeedsNoDataStream()
    {
        TarArchiveCatalog.ValidatePayload(null, 0, new byte[8192], default);
    }

    private sealed class CancellingStream(byte[] bytes, CancellationTokenSource cancellation)
        : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            cancellation.Cancel();
            return read;
        }
    }
}
