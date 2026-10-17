using System.Buffers.Binary;
using System.IO.Compression;
using Meridian.Core.Archives;

namespace Meridian.Core.Tests;

public sealed class ZipTreeFaultToleranceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(99)]
    [InlineData(100)]
    public async Task ObserverFailureDoesNotInvalidateCompletedTree(int failureAt)
    {
        await InTemporaryDirectory(async root =>
        {
            var archive = CreateArchive(root);
            var observed = new List<int>();
            var receipt = await ZipTreeExtractor.ExtractAsync(archive, root, "output",
                progress: new Observer(value =>
                {
                    observed.Add(value);
                    if (value >= failureAt) throw new IOException("Observer unavailable.");
                }));
            Assert.Equal(1, receipt.FileCount);
            Assert.Equal(4, receipt.BytesWritten);
            Assert.Equal("data", await File.ReadAllTextAsync(Path.Combine(receipt.Directory, "folder", "file.txt")));
            Assert.Equal(new[] { 50, 99, 100 }, observed);
            AssertNoStage(root);
        });
    }

    [Theory]
    [InlineData(50)]
    [InlineData(99)]
    public async Task CancellationFromThrowingObserverRemovesStage(int cancelAt)
    {
        await InTemporaryDirectory(async root =>
        {
            var archive = CreateArchive(root);
            using var cancellation = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                ZipTreeExtractor.ExtractAsync(archive, root, "output", cancellationToken: cancellation.Token,
                    progress: new Observer(value =>
                    {
                        if (value != cancelAt) return;
                        cancellation.Cancel();
                        throw new IOException("Observer failed after cancellation.");
                    })));
            Assert.False(Directory.Exists(Path.Combine(root, "output")));
            AssertNoStage(root);
            Assert.Equal(2, ZipArchiveCatalog.List(archive).Count);
        });
    }

    [Fact]
    public async Task PublicationCollisionPreservesConcurrentDestination()
    {
        await InTemporaryDirectory(async root =>
        {
            var archive = CreateArchive(root);
            var destination = Path.Combine(root, "output");
            await Assert.ThrowsAsync<IOException>(() => ZipTreeExtractor.ExtractAsync(archive, root, "output",
                progress: new Observer(value =>
                {
                    if (value != 99) return;
                    Directory.CreateDirectory(destination);
                    File.WriteAllText(Path.Combine(destination, "keep.txt"), "keep");
                    throw new IOException("Observer failed after collision.");
                })));
            Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(destination, "keep.txt")));
            Assert.Single(Directory.GetFiles(destination));
            AssertNoStage(root);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectoryEntryWithInvalidCrcCannotPublish(bool compressed)
    {
        await InTemporaryDirectory(async root =>
        {
            var archive = Path.Combine(root, "bundle.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                zip.CreateEntry("folder/", compressed ? CompressionLevel.Optimal : CompressionLevel.NoCompression);
            var bytes = await File.ReadAllBytesAsync(archive);
            var central = FindCentralHeader(bytes);
            bytes[central + 16] ^= 0xff;
            await File.WriteAllBytesAsync(archive, bytes);
            Assert.Single(ZipArchiveCatalog.List(archive));
            await Assert.ThrowsAsync<InvalidDataException>(() => ZipTreeExtractor.ExtractAsync(archive, root, "output"));
            Assert.Empty(Directory.GetDirectories(root));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(archive));
        });
    }

    [Fact]
    public async Task DirectoryWithHiddenStoredPayloadCannotPublish()
    {
        await InTemporaryDirectory(async root =>
        {
            var archive = Path.Combine(root, "bundle.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            using (var output = zip.CreateEntry("folder/", CompressionLevel.NoCompression).Open())
                output.WriteByte(42);
            var bytes = await File.ReadAllBytesAsync(archive);
            var central = FindCentralHeader(bytes);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 24), 0);
            await File.WriteAllBytesAsync(archive, bytes);
            Assert.Single(ZipArchiveCatalog.List(archive));
            await Assert.ThrowsAsync<InvalidDataException>(() => ZipTreeExtractor.ExtractAsync(archive, root, "output"));
            Assert.Empty(Directory.GetDirectories(root));
        });
    }

    private static string CreateArchive(string root)
    {
        var path = Path.Combine(root, "bundle.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        archive.CreateEntry("folder/");
        using var writer = new StreamWriter(archive.CreateEntry("folder/file.txt").Open());
        writer.Write("data");
        return path;
    }

    private static int FindCentralHeader(byte[] bytes)
    {
        for (var i = 0; i <= bytes.Length - 4; i++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i)) == 0x02014b50) return i;
        throw new InvalidDataException("Missing central directory in fixture.");
    }

    private static void AssertNoStage(string root) =>
        Assert.Empty(Directory.GetDirectories(root, ".meridian-extract-*"));

    private static async Task InTemporaryDirectory(Func<string, Task> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-zip-fault-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { await test(root); }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Observer(Action<int> callback) : IProgress<int>
    {
        public void Report(int value) => callback(value);
    }
}
