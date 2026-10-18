using System.Formats.Tar;
using Meridian.Core.Archives;
using Xunit;

namespace Meridian.Core.Tests;

public sealed class TarArchiveCreatorTests
{
    [Theory]
    [InlineData(".tar")]
    [InlineData(".tgz")]
    public async Task PreCancelledCreationLeavesNoArchiveOrStagingFile(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "input.txt");
            await File.WriteAllTextAsync(input, "content");
            var archive = Path.Combine(root, "bundle" + extension);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                TarArchiveCreator.CreateAsync(archive,
                    new Dictionary<string, string> { ["input.txt"] = input }, cts.Token));
            Assert.False(File.Exists(archive));
            Assert.Empty(Directory.GetFiles(root, ".meridian-tar-*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(".tar")]
    [InlineData(".tar.gz")]
    [InlineData(".tgz")]
    public async Task RejectsDestinationAsSource(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-self-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var target = Path.Combine(root, "bundle" + extension);
            await File.WriteAllTextAsync(target, "original");
            await Assert.ThrowsAsync<IOException>(() => TarArchiveCreator.CreateAsync(target,
                new Dictionary<string, string> { ["self.txt"] = target }));
            Assert.Equal("original", await File.ReadAllTextAsync(target));
            Assert.Empty(Directory.GetFiles(root, ".meridian-tar-*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RejectsDuplicatePhysicalSourceUnderDifferentEntryNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-duplicate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "input.txt");
            await File.WriteAllTextAsync(input, "content");
            var archive = Path.Combine(root, "bundle.tar");
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                TarArchiveCreator.CreateAsync(archive, new Dictionary<string, string>
                {
                    ["first.txt"] = input,
                    ["second.txt"] = input
                }));
            Assert.False(File.Exists(archive));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsFileAndDescendantEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var one = Path.Combine(root, "one.txt");
            var two = Path.Combine(root, "two.txt");
            await File.WriteAllTextAsync(one, "one");
            await File.WriteAllTextAsync(two, "two");
            var archive = Path.Combine(root, "invalid.tar");
            await Assert.ThrowsAsync<InvalidDataException>(() => TarArchiveCreator.CreateAsync(archive,
                new Dictionary<string, string> { ["folder/file.txt"] = one, ["folder"] = two }));
            Assert.False(File.Exists(archive));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(".tar")]
    [InlineData(".tgz")]
    public async Task RejectsCaseInsensitiveDuplicateEntryNames(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-case-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "first.txt");
            var second = Path.Combine(root, "second.txt");
            await File.WriteAllTextAsync(first, "first");
            await File.WriteAllTextAsync(second, "second");
            var archive = Path.Combine(root, "bundle" + extension);
            await Assert.ThrowsAsync<InvalidDataException>(() => TarArchiveCreator.CreateAsync(archive,
                new Dictionary<string, string> { ["Readme.txt"] = first, ["README.TXT"] = second }));
            Assert.False(File.Exists(archive));
            Assert.Empty(Directory.GetFiles(root, ".meridian-tar-*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RejectsParentFileBeforeDescendant()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var one = Path.Combine(root, "one.txt");
            var two = Path.Combine(root, "two.txt");
            await File.WriteAllTextAsync(one, "one");
            await File.WriteAllTextAsync(two, "two");
            var archive = Path.Combine(root, "invalid.tar");
            await Assert.ThrowsAsync<InvalidDataException>(() => TarArchiveCreator.CreateAsync(archive,
                new Dictionary<string, string> { ["folder"] = one, ["folder/file.txt"] = two }));
            Assert.False(File.Exists(archive));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(".tar")]
    [InlineData(".tgz")]
    public async Task RejectsMissingSourceWithoutPublishingArchive(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var missing = Path.Combine(root, "missing.txt");
            var archive = Path.Combine(root, "bundle" + extension);
            await Assert.ThrowsAnyAsync<IOException>(() => TarArchiveCreator.CreateAsync(archive,
                new Dictionary<string, string> { ["missing.txt"] = missing }));
            Assert.False(File.Exists(archive));
            Assert.Empty(Directory.GetFiles(root, ".meridian-tar-*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(".tar")]
    [InlineData(".tgz")]
    public async Task RejectsDirectoryAsFileSource(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-directory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var directory = Path.Combine(root, "source-folder");
            Directory.CreateDirectory(directory);
            var archive = Path.Combine(root, "bundle" + extension);
            await Assert.ThrowsAnyAsync<Exception>(() => TarArchiveCreator.CreateAsync(archive,
                new Dictionary<string, string> { ["source.txt"] = directory }));
            Assert.False(File.Exists(archive));
            Assert.Empty(Directory.GetFiles(root, ".meridian-tar-*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(".tar")]
    [InlineData(".tgz")]
    public async Task ProgressReportsCompletionAfterPublishing(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "input.bin");
            await File.WriteAllBytesAsync(source, new byte[2 * 1024 * 1024]);
            var archive = Path.Combine(root, "bundle" + extension);
            var updates = new List<(long CompletedBytes, long TotalBytes)>();
            var progress = new InlineProgress<(long CompletedBytes, long TotalBytes)>(value =>
            {
                updates.Add(value);
                if (value.CompletedBytes == value.TotalBytes)
                    Assert.True(File.Exists(archive));
            });
            await TarArchiveCreator.CreateAsync(archive,
                new Dictionary<string, string> { ["input.bin"] = source }, progress: progress);
            Assert.Equal((0L, 2L * 1024 * 1024), updates[0]);
            Assert.Equal((2L * 1024 * 1024, 2L * 1024 * 1024), updates[^1]);
            Assert.Contains(updates, update => update.CompletedBytes > 0 && update.CompletedBytes < update.TotalBytes);
            Assert.Single(TarArchiveCatalog.List(archive));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(".tar")]
    [InlineData(".tgz")]
    public async Task CancellationDuringStreamingRemovesStaging(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-cancel-stream-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "large.bin");
            await File.WriteAllBytesAsync(source, new byte[4 * 1024 * 1024]);
            var archive = Path.Combine(root, "bundle" + extension);
            using var cts = new CancellationTokenSource();
            var progress = new InlineProgress<(long CompletedBytes, long TotalBytes)>(value =>
            {
                if (value.CompletedBytes > 0)
                    cts.Cancel();
            });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                TarArchiveCreator.CreateAsync(archive,
                    new Dictionary<string, string> { ["large.bin"] = source },
                    cancellationToken: cts.Token, progress: progress));
            Assert.False(File.Exists(archive));
            Assert.Empty(Directory.GetFiles(root, ".meridian-tar-*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(".tar")]
    [InlineData(".tgz")]
    public async Task ProgressObserverExceptionsDoNotAbortCreation(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-observer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "input.bin");
            await File.WriteAllBytesAsync(source, new byte[2 * 1024 * 1024]);
            var archive = Path.Combine(root, "bundle" + extension);
            var calls = 0;
            var progress = new InlineProgress<(long CompletedBytes, long TotalBytes)>(_ =>
            {
                calls++;
                throw new InvalidOperationException("Observer failure");
            });
            await TarArchiveCreator.CreateAsync(archive,
                new Dictionary<string, string> { ["input.bin"] = source }, progress: progress);
            Assert.True(calls >= 2);
            Assert.Single(TarArchiveCatalog.List(archive));
            Assert.Empty(Directory.GetFiles(root, ".meridian-tar-*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(".tar")]
    [InlineData(".tar.gz")]
    [InlineData(".tgz")]
    public async Task EmptySourceReportsCompletionOnlyAfterPublication(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-empty-progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "empty.txt");
            await File.WriteAllBytesAsync(source, Array.Empty<byte>());
            var archive = Path.Combine(root, "bundle" + extension);
            var updates = new List<(long CompletedBytes, long TotalBytes)>();
            var progress = new InlineProgress<(long CompletedBytes, long TotalBytes)>(value =>
            {
                updates.Add(value);
                if (updates.Count == 1)
                    Assert.False(File.Exists(archive));
                else
                    Assert.True(File.Exists(archive));
            });
            await TarArchiveCreator.CreateAsync(archive,
                new Dictionary<string, string> { ["empty.txt"] = source }, progress: progress);
            Assert.Equal(2, updates.Count);
            Assert.All(updates, update => Assert.Equal((0L, 0L), update));
            Assert.Single(TarArchiveCatalog.List(archive));
            Assert.Empty(Directory.GetFiles(root, ".meridian-tar-*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(".tar")]
    [InlineData(".tgz")]
    public async Task PreCancelledCreationNeverNotifiesProgress(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-cancel-progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "input.txt");
            await File.WriteAllTextAsync(source, "data");
            var archive = Path.Combine(root, "bundle" + extension);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var notifications = 0;
            var progress = new InlineProgress<(long CompletedBytes, long TotalBytes)>(_ => notifications++);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                TarArchiveCreator.CreateAsync(archive,
                    new Dictionary<string, string> { ["input.txt"] = source },
                    cancellationToken: cts.Token, progress: progress));
            Assert.Equal(0, notifications);
            Assert.False(File.Exists(archive));
            Assert.Empty(Directory.GetFiles(root, ".meridian-tar-*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(".tar")]
    [InlineData(".tgz")]
    public async Task CancellingFromInitialProgressDoesNotCreateArchive(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-cancel-initial-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "input.txt");
            await File.WriteAllTextAsync(source, "data");
            var archive = Path.Combine(root, "bundle" + extension);
            using var cts = new CancellationTokenSource();
            var updates = 0;
            var progress = new InlineProgress<(long CompletedBytes, long TotalBytes)>(_ =>
            {
                updates++;
                cts.Cancel();
            });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                TarArchiveCreator.CreateAsync(archive,
                    new Dictionary<string, string> { ["input.txt"] = source },
                    cancellationToken: cts.Token, progress: progress));
            Assert.Equal(1, updates);
            Assert.False(File.Exists(archive));
            Assert.Empty(Directory.GetFiles(root, ".meridian-tar-*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(".tar")]
    [InlineData(".tgz")]
    public async Task CancellingOnFinalProgressDoesNotUndoPublishedArchive(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-final-progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "input.txt");
            await File.WriteAllTextAsync(source, "published");
            var archive = Path.Combine(root, "bundle" + extension);
            using var cts = new CancellationTokenSource();
            var progress = new InlineProgress<(long CompletedBytes, long TotalBytes)>(value =>
            {
                if (value.CompletedBytes == value.TotalBytes)
                    cts.Cancel();
            });
            await TarArchiveCreator.CreateAsync(archive,
                new Dictionary<string, string> { ["input.txt"] = source },
                cancellationToken: cts.Token, progress: progress);
            Assert.True(cts.IsCancellationRequested);
            Assert.True(File.Exists(archive));
            Assert.Single(TarArchiveCatalog.List(archive));
            Assert.Empty(Directory.GetFiles(root, ".meridian-tar-*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    [Theory]
    [InlineData(".tar")]
    [InlineData(".tar.gz")]
    [InlineData(".tgz")]
    public async Task CreatesCatalogReadableArchive(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "input.txt");
            await File.WriteAllTextAsync(input, "hello tar");
            var archive = Path.Combine(root, "bundle" + extension);
            await TarArchiveCreator.CreateAsync(archive,
                new Dictionary<string, string> { ["docs/input.txt"] = input });
            var entries = TarArchiveCatalog.List(archive);
            Assert.Single(entries);
            Assert.Equal("docs/input.txt", entries[0].Path);
            Assert.Equal(9, entries[0].Bytes);
            Assert.Empty(Directory.GetFiles(root, ".meridian-tar-*"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(".tar")]
    [InlineData(".tar.gz")]
    [InlineData(".tgz")]
    public async Task CreatedArchiveCanBeExtractedWithOriginalContents(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-roundtrip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "original.txt");
            await File.WriteAllTextAsync(source, "round-trip contents");
            var archive = Path.Combine(root, "bundle" + extension);
            await TarArchiveCreator.CreateAsync(archive,
                new Dictionary<string, string> { ["docs/original.txt"] = source });
            var receipt = await TarTreeExtractor.ExtractAsync(archive, root, "restored");
            Assert.Equal(1, receipt.FileCount);
            Assert.Equal("round-trip contents",
                await File.ReadAllTextAsync(Path.Combine(root, "restored", "docs", "original.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(".tar")]
    [InlineData(".tar.gz")]
    [InlineData(".tgz")]
    public async Task RefusesToOverwriteExistingArchive(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-existing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "source.txt");
            var archive = Path.Combine(root, "existing" + extension);
            await File.WriteAllTextAsync(input, "source");
            await File.WriteAllTextAsync(archive, "original");
            await Assert.ThrowsAsync<IOException>(() =>
                TarArchiveCreator.CreateAsync(archive,
                    new Dictionary<string, string> { ["source.txt"] = input }));
            Assert.Equal("original", await File.ReadAllTextAsync(archive));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
