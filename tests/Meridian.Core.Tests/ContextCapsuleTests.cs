using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class ContextCapsuleTests
{
    [Fact]
    public void Capture_PreservesReferencesAndOrderWithoutTouchingDisk()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"capsule-{Guid.NewGuid():N}.txt");
        var another = Path.Combine(Path.GetTempPath(), $"capsule-{Guid.NewGuid():N}.bin");
        var capsule = ContextCapsule.Capture("  Work  ", new[] { missing, another, missing });
        Assert.Equal("Work", capsule.Name);
        Assert.Equal(new[] { missing, another }, capsule.Paths);
        Assert.All(capsule.Inspect(), entry => Assert.Equal(ContextCapsule.Presence.Unavailable, entry.State));
        Assert.False(File.Exists(missing));
    }

    [Fact]
    public void Capture_LimitsConsumedEntriesEvenWhenAllAreDuplicates()
    {
        var path = Path.Combine(Path.GetTempPath(), "capsule-repeated");
        var references = Enumerable.Repeat(path, ContextCapsule.MaximumReferences + 1);
        Assert.Throws<ArgumentException>(() => ContextCapsule.Capture("Work", references));
    }

    [Fact]
    public void Capture_CancellationStopsEnumeration()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            ContextCapsule.Capture("Work", Enumerable.Repeat("something", 100), cancellation.Token));
    }

    [Fact]
    public void Inspect_RetainsReferencesWhenFilesGoOffline()
    {
        var root = Path.Combine(Path.GetTempPath(), $"capsule-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "note.txt");
            File.WriteAllText(file, "important");
            var capsule = ContextCapsule.Capture("Work", new[] { file });
            Assert.Equal(ContextCapsule.Presence.Present, Assert.Single(capsule.Inspect()).State);
            File.Delete(file);
            Assert.Equal(ContextCapsule.Presence.Unavailable, Assert.Single(capsule.Inspect()).State);
            Assert.Equal(file, Assert.Single(capsule.Paths));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Capture_RejectsBlankName(string name)
    {
        Assert.Throws<ArgumentException>(() => ContextCapsule.Capture(name, Array.Empty<string>()));
    }
}
