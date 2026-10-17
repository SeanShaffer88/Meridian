using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class OperationPreviewTests
{
    [Fact]
    public void PreviewCopy_ReportsReadyAndExistingDestinationWithoutModifyingFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-preview-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        try
        {
            var ready = Path.Combine(source, "ready.txt");
            var collision = Path.Combine(source, "collision.txt");
            File.WriteAllText(ready, "ready");
            File.WriteAllText(collision, "source");
            File.WriteAllText(Path.Combine(destination, "collision.txt"), "existing");

            var plan = OperationPreview.PreviewCopy([ready, collision], destination);
            Assert.Equal(1, plan.ReadyCount);
            Assert.True(plan.HasBlockers);
            Assert.Equal(OperationPreview.Status.Ready, plan.Items[0].Result);
            Assert.Equal(OperationPreview.Status.DestinationExists, plan.Items[1].Result);
            Assert.False(File.Exists(Path.Combine(destination, "ready.txt")));
            Assert.Equal("existing", File.ReadAllText(Path.Combine(destination, "collision.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void PreviewCopy_RejectsMissingSourceAndNestedDestination()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-preview-{Guid.NewGuid():N}");
        var nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);
        try
        {
            var missing = Path.Combine(root, "missing.txt");
            var plan = OperationPreview.PreviewCopy([missing, root], nested);
            Assert.Equal(OperationPreview.Status.MissingSource, plan.Items[0].Result);
            Assert.Equal(OperationPreview.Status.NestedDestination, plan.Items[1].Result);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void PreviewCopy_DetectsDuplicateTargetsInBatch()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-preview-{Guid.NewGuid():N}");
        var a = Path.Combine(root, "a");
        var b = Path.Combine(root, "b");
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        Directory.CreateDirectory(destination);
        try
        {
            var first = Path.Combine(a, "same.txt");
            var second = Path.Combine(b, "same.txt");
            File.WriteAllText(first, "one");
            File.WriteAllText(second, "two");
            var plan = OperationPreview.PreviewCopy([first, second], destination);
            Assert.Equal(OperationPreview.Status.Ready, plan.Items[0].Result);
            Assert.Equal(OperationPreview.Status.DestinationExists, plan.Items[1].Result);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void PreviewCopy_RespectsCancellation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-preview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                OperationPreview.PreviewCopy([Path.Combine(root, "missing")], root, cancellation.Token));
        }
        finally { Directory.Delete(root); }
    }
}
