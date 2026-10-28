using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class ContextCapsuleStoreTests
{
    [Fact]
    public async Task SaveLoad_PreservesOfflineReference()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "capsules.json");
            var missing = Path.Combine(root, "offline.txt");
            var store = new ContextCapsuleStore(path);
            await store.SaveAsync(new[] { ContextCapsule.Capture("Work", new[] { missing }) });
            var restored = Assert.Single(await store.LoadAsync());
            Assert.Equal("Work", restored.Name);
            Assert.Equal(missing, Assert.Single(restored.Paths));
            Assert.False(File.Exists(missing));
            Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CorruptFile_IsReportedNotSilentlyOverwritten()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "capsules.json");
            await File.WriteAllTextAsync(path, "{invalid");
            var store = new ContextCapsuleStore(path);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
            Assert.Equal("{invalid", await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CancelledSave_PreservesPriorDocument()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "capsules.json");
            var store = new ContextCapsuleStore(path);
            await store.SaveAsync(new[] { ContextCapsule.Capture("First", Array.Empty<string>()) });
            var before = await File.ReadAllTextAsync(path);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                store.SaveAsync(new[] { ContextCapsule.Capture("Second", Array.Empty<string>()) }, cts.Token));
            Assert.Equal(before, await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
