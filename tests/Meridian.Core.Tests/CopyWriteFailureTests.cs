using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class CopyWriteFailureTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public void WriteOrCloseFailurePreservesSourceAndPreventsPublication(bool tree, int mode)
    {
        using var fixture = new Fixture(tree);
        var injected = new IOException("Injected disk-full/write-finalization failure", unchecked((int)0x80070070));
        var streams = new List<FailingOutput>();
        var progress = new List<FileOperations.CopyProgress>();
        var failure = Assert.Throws<IOException>(() => FileOperations.CopyItemCore(
            fixture.Source, fixture.Destination, default, new Observer(progress.Add), path =>
            {
                var output = new FailingOutput(path, mode, injected);
                streams.Add(output);
                return output;
            }));

        Assert.Same(injected, failure);
        Assert.NotEmpty(streams);
        Assert.All(streams, stream => Assert.True(stream.Disposed));
        if (mode is 1 or 2 or 3) Assert.True(streams.Sum(stream => stream.BytesWritten) > 0);
        var reportedBytes = progress.LastOrDefault()?.BytesCopied ?? 0;
        Assert.Equal(mode switch { 2 => 1024L * 1024, 3 => fixture.Payload.LongLength, _ => 0L }, reportedBytes);
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Destination));
        fixture.AssertSourceIntact();

        // The same operation can immediately retry: no staged item or open
        // output handle may remain to obstruct publication.
        var result = FileOperations.CopyItem(fixture.Source, fixture.Destination);
        Assert.Equal(fixture.Payload, File.ReadAllBytes(tree ? Path.Combine(result, "nested", "data.bin") : result));
        fixture.AssertSourceIntact();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutputAccessDeniedDoesNotLeaveStagingOrChangeSource(bool tree)
    {
        using var fixture = new Fixture(tree);
        var denied = new UnauthorizedAccessException("Injected destination access denial");
        var failure = Assert.Throws<UnauthorizedAccessException>(() => FileOperations.CopyItemCore(
            fixture.Source, fixture.Destination, default, null, _ => throw denied));
        Assert.Same(denied, failure);
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Destination));
        fixture.AssertSourceIntact();
    }

    private sealed class Observer(Action<FileOperations.CopyProgress> callback) : IProgress<FileOperations.CopyProgress>
    {
        public void Report(FileOperations.CopyProgress value) => callback(value);
    }

    private sealed class FailingOutput(string path, int mode, IOException failure) : Stream
    {
        private readonly FileStream _file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        private int _writes;
        public bool Disposed { get; private set; }
        public long BytesWritten { get; private set; }
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (mode == 0 || (mode == 2 && _writes > 0)) throw failure;
            var written = mode == 1 ? Math.Min(17, count) : count;
            _file.Write(buffer, offset, written);
            BytesWritten += written;
            _writes++;
            if (mode == 1) throw failure;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !Disposed)
            {
                Disposed = true;
                _file.Dispose();
                if (mode == 3) throw failure;
            }
            base.Dispose(disposing);
        }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => _file.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-write-failure-" + Guid.NewGuid().ToString("N"));
        private readonly string _sourceFile;
        public string Source { get; }
        public string Destination { get; }
        public byte[] Payload { get; } = new byte[2 * 1024 * 1024 + 17];
        public Fixture(bool tree)
        {
            Directory.CreateDirectory(_root);
            Destination = Directory.CreateDirectory(Path.Combine(_root, "destination")).FullName;
            Source = Path.Combine(_root, tree ? "tree" : "data.bin");
            if (tree) Directory.CreateDirectory(Path.Combine(Source, "nested"));
            _sourceFile = tree ? Path.Combine(Source, "nested", "data.bin") : Source;
            new Random(42).NextBytes(Payload);
            File.WriteAllBytes(_sourceFile, Payload);
        }
        public void AssertSourceIntact()
        {
            Assert.Equal(Payload, File.ReadAllBytes(_sourceFile));
            using var exclusive = File.Open(_sourceFile, FileMode.Open, FileAccess.Read, FileShare.None);
            Assert.Equal(Payload.LongLength, exclusive.Length);
        }
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
