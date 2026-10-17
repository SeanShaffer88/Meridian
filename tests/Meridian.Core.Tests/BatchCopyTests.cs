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
}
