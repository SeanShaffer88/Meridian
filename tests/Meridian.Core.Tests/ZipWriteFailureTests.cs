using System.IO.Compression;
using Meridian.Core.Archives;

namespace Meridian.Core.Tests;

public sealed class ZipWriteFailureTests
{
    public static IEnumerable<object[]> FailureCases()
    {
        foreach (var extractor in new[] { "direct", "staged", "tree" })
        foreach (var failure in new[] { "before", "partial", "later", "flush", "close", "denied" })
            yield return new object[] { extractor, failure };
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task WriteFlushOrCloseFailureCleansOutputAndAllowsRetry(string extractor, string mode)
    {
        using var fixture = new Fixture();
        Exception injected = mode == "denied"
            ? new UnauthorizedAccessException("Injected output access denial")
            : new IOException("Injected ZIP output failure", unchecked((int)0x80070070));
        var outputs = new List<ControlledOutput>();
        var observed = await Record.ExceptionAsync(() => Extract(extractor, fixture, path =>
        {
            if (mode == "denied") throw injected;
            var stream = new ControlledOutput(path, mode, injected);
            outputs.Add(stream);
            return stream;
        }));

        Assert.Same(injected, observed);
        Assert.All(outputs, stream => Assert.True(stream.Closed));
        if (mode is "partial" or "later" or "flush" or "close")
            Assert.True(outputs.Sum(stream => stream.BytesWritten) > 0);
        fixture.AssertNoOutput();
        fixture.AssertArchiveIntact();

        await ExtractPublic(extractor, fixture);
        Assert.Equal(fixture.Payload, await File.ReadAllBytesAsync(fixture.Result(extractor)));
        fixture.AssertArchiveIntact();
    }

    [Theory]
    [InlineData("direct", "write")]
    [InlineData("direct", "flush")]
    [InlineData("direct", "close")]
    [InlineData("staged", "write")]
    [InlineData("staged", "flush")]
    [InlineData("staged", "close")]
    [InlineData("tree", "write")]
    [InlineData("tree", "flush")]
    [InlineData("tree", "close")]
    public async Task CancellationDuringOutputFinalizationCannotSucceed(string extractor, string cancelAt)
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        ControlledOutput? output = null;
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Extract(extractor, fixture, path => output = new ControlledOutput(path, "none", null,
                phase => { if (phase == cancelAt) cancellation.Cancel(); }), cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.NotNull(output);
        Assert.True(output.Closed);
        fixture.AssertNoOutput();
        fixture.AssertArchiveIntact();
        await ExtractPublic(extractor, fixture);
        Assert.Equal(fixture.Payload, await File.ReadAllBytesAsync(fixture.Result(extractor)));
    }

    [Theory]
    [InlineData("before")]
    [InlineData("partial")]
    [InlineData("later")]
    [InlineData("flush")]
    [InlineData("close")]
    [InlineData("denied")]
    public async Task LaterTreeEntryFailureDiscardsPreviouslyCompletedFiles(string mode)
    {
        using var fixture = new Fixture(multiEntry: true);
        var injected = new IOException("Injected second-entry output failure");
        var outputs = new List<ControlledOutput>();
        var opened = 0;
        var error = await Record.ExceptionAsync(() => Extract("tree", fixture, path =>
        {
            opened++;
            if (opened == 2 && mode == "denied") throw injected;
            var output = new ControlledOutput(path, opened == 2 ? mode : "none", injected);
            outputs.Add(output);
            return output;
        }));
        Assert.Same(injected, error);
        Assert.Equal(2, opened);
        Assert.Equal(fixture.Payload.LongLength, outputs[0].BytesWritten);
        Assert.All(outputs, output => Assert.True(output.Closed));
        fixture.AssertNoOutput();
        fixture.AssertArchiveIntact();
        await ExtractPublic("tree", fixture);
        Assert.Equal(fixture.Payload, await File.ReadAllBytesAsync(fixture.Result("tree")));
        Assert.Equal(fixture.Payload, await File.ReadAllBytesAsync(Path.Combine(fixture.Destination, "output", "nested", "first.bin")));
    }

    [Theory]
    [InlineData("staged")]
    [InlineData("tree")]
    public async Task CollisionIntroducedWhileClosingOutputPreservesConcurrentDestination(string extractor)
    {
        using var fixture = new Fixture();
        var collision = extractor == "tree" ? Path.Combine(fixture.Destination, "output") : fixture.Result(extractor);
        await Assert.ThrowsAsync<IOException>(() => Extract(extractor, fixture, path =>
            new ControlledOutput(path, "none", null, phase =>
            {
                if (phase != "close") return;
                if (extractor == "tree")
                {
                    Directory.CreateDirectory(collision);
                    File.WriteAllText(Path.Combine(collision, "keep.txt"), "keep");
                }
                else File.WriteAllText(collision, "keep");
            })));
        Assert.Equal("keep", await File.ReadAllTextAsync(extractor == "tree"
            ? Path.Combine(collision, "keep.txt") : collision));
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Destination, ".meridian-*"));
        fixture.AssertArchiveIntact();
    }

    private static async Task Extract(string extractor, Fixture fixture, Func<string, Stream> factory,
        CancellationToken token = default)
    {
        switch (extractor)
        {
            case "direct":
                await ZipSafeExtractor.ExtractSingleCoreAsync(fixture.Archive, "data.bin", fixture.Destination,
                    factory, cancellationToken: token);
                break;
            case "staged":
                await ZipStagedExtractor.ExtractSingleCoreAsync(fixture.Archive, "data.bin", fixture.Destination,
                    factory, cancellationToken: token);
                break;
            case "tree":
                await ZipTreeExtractor.ExtractCoreAsync(fixture.Archive, fixture.Destination, "output",
                    factory, cancellationToken: token);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(extractor));
        }
    }

    private static async Task ExtractPublic(string extractor, Fixture fixture)
    {
        switch (extractor)
        {
            case "direct": await ZipSafeExtractor.ExtractSingleAsync(fixture.Archive, "data.bin", fixture.Destination); break;
            case "staged": await ZipStagedExtractor.ExtractSingleAsync(fixture.Archive, "data.bin", fixture.Destination); break;
            case "tree": await ZipTreeExtractor.ExtractAsync(fixture.Archive, fixture.Destination, "output"); break;
            default: throw new ArgumentOutOfRangeException(nameof(extractor));
        }
    }

    private sealed class ControlledOutput(string path, string mode, Exception? failure,
        Action<string>? onPhase = null) : Stream
    {
        private readonly FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        private int writes;
        public bool Closed { get; private set; }
        public long BytesWritten { get; private set; }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (mode == "before" || (mode == "later" && writes > 0)) throw failure!;
            var length = mode == "partial" ? Math.Min(17, buffer.Length) : buffer.Length;
            await file.WriteAsync(buffer[..length], cancellationToken);
            BytesWritten += length;
            writes++;
            onPhase?.Invoke("write");
            if (mode == "partial") throw failure!;
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (mode == "flush") throw failure!;
            await file.FlushAsync(cancellationToken);
            onPhase?.Invoke("flush");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !Closed)
            {
                file.Dispose();
                Closed = true;
                onPhase?.Invoke("close");
                if (mode == "close") throw failure!;
            }
            base.Dispose(disposing);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !Closed;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => file.Flush();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "meridian-zip-write-" + Guid.NewGuid().ToString("N"));
        private readonly byte[] archiveBytes;
        public string Archive { get; }
        public string Destination { get; }
        public byte[] Payload { get; } = new byte[32769];

        public Fixture(bool multiEntry = false)
        {
            Directory.CreateDirectory(root);
            Destination = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;
            Archive = Path.Combine(root, "bundle.zip");
            new Random(42).NextBytes(Payload);
            using (var zip = ZipFile.Open(Archive, ZipArchiveMode.Create))
            {
                if (multiEntry)
                {
                    using var first = zip.CreateEntry("nested/first.bin", CompressionLevel.NoCompression).Open();
                    first.Write(Payload);
                }
                using var output = zip.CreateEntry("data.bin", CompressionLevel.NoCompression).Open();
                output.Write(Payload);
            }
            archiveBytes = File.ReadAllBytes(Archive);
        }

        public string Result(string extractor) => extractor == "tree"
            ? Path.Combine(Destination, "output", "data.bin") : Path.Combine(Destination, "data.bin");
        public void AssertNoOutput() => Assert.Empty(Directory.GetFileSystemEntries(Destination));
        public void AssertArchiveIntact()
        {
            Assert.Equal(archiveBytes, File.ReadAllBytes(Archive));
            using var exclusive = File.Open(Archive, FileMode.Open, FileAccess.Read, FileShare.None);
            Assert.Equal(archiveBytes.LongLength, exclusive.Length);
        }
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
