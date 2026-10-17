using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class CopyExecutionTests
{
    [Fact]
    public void TreeCopyPreservesDifferentPayloadsAcrossBufferBoundaries()
    {
        using var fixture = new Fixture(tree: true);
        int[] lengths = [0, 1, 4095, 4096, 4097, 1024 * 1024 - 1, 1024 * 1024, 1024 * 1024 + 1];
        var expected = lengths.Select((length, index) =>
        {
            var bytes = new byte[length];
            new Random(index + 100).NextBytes(bytes);
            var name = $"boundary-{index}.bin";
            File.WriteAllBytes(Path.Combine(fixture.Source, "nested", name), bytes);
            return (name, bytes);
        }).ToArray();

        var output = FileOperations.CopyItem(fixture.Source, fixture.Destination);

        foreach (var (name, bytes) in expected)
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(output, "nested", name)));
        fixture.AssertSourceIntact();
    }

    [Fact]
    public async Task ConcurrentCopiesKeepIndependentPayloads()
    {
        var fixtures = Enumerable.Range(0, 4).Select(_ => new Fixture(tree: false)).ToArray();
        try
        {
            for (var index = 0; index < fixtures.Length; index++)
            {
                new Random(index + 100).NextBytes(fixtures[index].Payload);
                File.WriteAllBytes(fixtures[index].Source, fixtures[index].Payload);
            }
            var outputs = await Task.WhenAll(fixtures.Select(fixture => Task.Run(() =>
                FileOperations.CopyItem(fixture.Source, fixture.Destination))));

            for (var index = 0; index < fixtures.Length; index++)
            {
                Assert.Equal(fixtures[index].Payload, File.ReadAllBytes(outputs[index]));
                fixtures[index].AssertSourceIntact();
            }
        }
        finally { foreach (var fixture in fixtures) fixture.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationAfterBytesAreWrittenRemovesAllStaging(bool tree)
    {
        using var fixture = new Fixture(tree);
        using var cancellation = new CancellationTokenSource();
        var sawWrittenData = false;
        var progress = new Observer<FileOperations.CopyProgress>(update =>
        {
            Assert.True(update.BytesCopied > 0);
            Assert.False(File.Exists(Path.Combine(fixture.Destination, Path.GetFileName(fixture.Source))));
            Assert.False(Directory.Exists(Path.Combine(fixture.Destination, Path.GetFileName(fixture.Source))));
            sawWrittenData = Directory.EnumerateFiles(fixture.Destination, "*", SearchOption.AllDirectories).Any();
            cancellation.Cancel();
        });

        Assert.Throws<OperationCanceledException>(() => FileOperations.CopyItem(
            fixture.Source, fixture.Destination, cancellation.Token, progress));

        Assert.True(sawWrittenData);
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Destination));
        fixture.AssertSourceIntact();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ByteProgressIsCumulativeAndOutputIsComplete(bool tree)
    {
        using var fixture = new Fixture(tree);
        var updates = new List<FileOperations.CopyProgress>();
        var output = FileOperations.CopyItem(fixture.Source, fixture.Destination,
            progress: new Observer<FileOperations.CopyProgress>(updates.Add));
        Assert.True(updates.Count >= 3);
        Assert.Equal(fixture.Payload.Length + (tree ? 4 : 0), updates[^1].BytesCopied);
        Assert.All(updates.Zip(updates.Skip(1)), pair => Assert.True(pair.Second.BytesCopied > pair.First.BytesCopied));
        Assert.All(updates, update => Assert.StartsWith(fixture.Root, update.Source));
        Assert.Equal(fixture.Payload, File.ReadAllBytes(tree ? Path.Combine(output, "data.bin") : output));
        if (tree) Assert.Equal("tail", File.ReadAllText(Path.Combine(output, "nested", "tail.txt")));
        Assert.Single(Directory.GetFileSystemEntries(fixture.Destination));
        fixture.AssertSourceIntact();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObserverExceptionDoesNotCancelOrInvalidateCopy(bool tree)
    {
        using var fixture = new Fixture(tree);
        var calls = 0;
        var output = FileOperations.CopyItem(fixture.Source, fixture.Destination,
            progress: new Observer<FileOperations.CopyProgress>(_ =>
            {
                calls++;
                throw new OperationCanceledException("Observer failure, not token cancellation");
            }));
        Assert.True(calls > 0);
        Assert.Equal(fixture.Payload, File.ReadAllBytes(tree ? Path.Combine(output, "data.bin") : output));
        Assert.Single(Directory.GetFileSystemEntries(fixture.Destination));
        fixture.AssertSourceIntact();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollisionDuringCopyPreservesExistingTargetAndCleansStaging(bool tree)
    {
        using var fixture = new Fixture(tree);
        var final = Path.Combine(fixture.Destination, Path.GetFileName(fixture.Source));
        var marker = tree ? Path.Combine(final, "existing.txt") : final;
        var injected = false;
        var observer = new Observer<FileOperations.CopyProgress>(_ =>
        {
            if (injected) return;
            if (tree) Directory.CreateDirectory(final);
            File.WriteAllText(marker, "existing target");
            injected = true;
        });
        Assert.Throws<IOException>(() => FileOperations.CopyItem(fixture.Source,
            fixture.Destination, progress: observer));
        Assert.True(injected);
        Assert.Equal("existing target", File.ReadAllText(marker));
        Assert.Equal(new[] { final }, Directory.GetFileSystemEntries(fixture.Destination));
        if (tree) Assert.Equal(new[] { marker }, Directory.GetFileSystemEntries(final));
        fixture.AssertSourceIntact();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BatchObserverFailuresPreserveCompletedResults(bool throwAfterCopy)
    {
        using var fixture = new Fixture(false);
        var second = Path.Combine(fixture.Root, "second.txt");
        File.WriteAllText(second, "second");
        var calls = 0;
        var result = BatchCopy.Execute([fixture.Source, second], fixture.Destination,
            progress: new Observer<BatchCopy.Progress>(update =>
            {
                if ((update.Completed > 0) == throwAfterCopy)
                {
                    calls++;
                    throw new IOException("Observer failure");
                }
            }));
        Assert.True(calls > 0);
        Assert.True(result.Succeeded);
        Assert.Null(result.FailedSource);
        Assert.Null(result.Failure);
        Assert.Equal(2, result.CompletedTargets.Count);
        Assert.Equal(fixture.Payload, File.ReadAllBytes(Path.Combine(fixture.Destination, "data.bin")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(fixture.Destination, "second.txt")));
        fixture.AssertSourceIntact();
    }

    private sealed class Observer<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "meridian-copy-run-" + Guid.NewGuid().ToString("N"));
        public string Destination { get; }
        public string Source { get; }
        public byte[] Payload { get; } = new byte[2 * 1024 * 1024 + 17];
        private readonly string _sourceFile;

        public Fixture(bool tree)
        {
            Directory.CreateDirectory(Root);
            Destination = Directory.CreateDirectory(Path.Combine(Root, "destination")).FullName;
            Source = Path.Combine(Root, tree ? "tree" : "data.bin");
            if (tree)
            {
                Directory.CreateDirectory(Path.Combine(Source, "nested"));
                File.WriteAllText(Path.Combine(Source, "nested", "tail.txt"), "tail");
            }
            _sourceFile = tree ? Path.Combine(Source, "data.bin") : Source;
            new Random(42).NextBytes(Payload);
            File.WriteAllBytes(_sourceFile, Payload);
        }

        public void AssertSourceIntact()
        {
            Assert.Equal(Payload, File.ReadAllBytes(_sourceFile));
            if (Directory.Exists(Source))
                Assert.Equal("tail", File.ReadAllText(Path.Combine(Source, "nested", "tail.txt")));
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
