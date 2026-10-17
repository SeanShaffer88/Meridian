using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class BatchCopyTests
{
    [Fact]
    public void Execute_CopiesMultipleItemsWithoutDeletingSources()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-batch-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        try
        {
            var a = Path.Combine(source, "a.txt");
            var b = Path.Combine(source, "b.txt");
            File.WriteAllText(a, "alpha");
            File.WriteAllText(b, "beta");
            var result = BatchCopy.Execute([a, b], destination);
            Assert.True(result.Succeeded);
            Assert.Equal(2, result.CompletedTargets.Count);
            Assert.Equal("alpha", File.ReadAllText(Path.Combine(destination, "a.txt")));
            Assert.Equal("beta", File.ReadAllText(Path.Combine(destination, "b.txt")));
            Assert.True(File.Exists(a));
            Assert.True(File.Exists(b));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Execute_RefusesEntireBatchWhenAnyTargetExists()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-batch-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        try
        {
            var a = Path.Combine(source, "a.txt");
            var b = Path.Combine(source, "b.txt");
            File.WriteAllText(a, "alpha");
            File.WriteAllText(b, "beta");
            File.WriteAllText(Path.Combine(destination, "b.txt"), "keep");
            Assert.Throws<InvalidOperationException>(() => BatchCopy.Execute([a, b], destination));
            Assert.False(File.Exists(Path.Combine(destination, "a.txt")));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(destination, "b.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Execute_CancelledBeforePreflight_DoesNotCopy()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-batch-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "a.txt");
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(destination);
        try
        {
            File.WriteAllText(source, "source");
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() => BatchCopy.Execute([source], destination, cts.Token));
            Assert.False(File.Exists(Path.Combine(destination, "a.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    [Fact]
    public void Execute_ReportsCompletedItemCounts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-progress-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "one.txt");
        var destination = Path.Combine(root, "target");
        Directory.CreateDirectory(destination);
        try
        {
            File.WriteAllText(source, "one");
            var updates = new List<BatchCopy.Progress>();
            var progress = new SynchronousProgress<BatchCopy.Progress>(updates.Add);
            var result = BatchCopy.Execute([source], destination, progress: progress);
            Assert.True(result.Succeeded);
            Assert.Contains(updates, x => x.Completed == 0 && x.Total == 1);
            Assert.Contains(updates, x => x.Completed == 1 && x.Total == 1);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class SynchronousProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
