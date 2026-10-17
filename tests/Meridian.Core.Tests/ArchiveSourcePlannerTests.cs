using Meridian.Core.Archives;
using Xunit;

namespace Meridian.Core.Tests;

public sealed class ArchiveSourcePlannerTests
{
    [Fact]
    public void PlansFilesAndNestedFolderContents()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-plan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var standalone = Path.Combine(root, "one.txt");
            File.WriteAllText(standalone, "one");
            var folder = Path.Combine(root, "nested");
            Directory.CreateDirectory(Path.Combine(folder, "deeper"));
            File.WriteAllText(Path.Combine(folder, "deeper", "two.txt"), "two");
            var result = ArchiveSourcePlanner.Plan(new[] { standalone, folder });
            Assert.Equal(2, result.Count);
            Assert.Contains("one.txt", result.Keys);
            Assert.Contains("nested/deeper/two.txt", result.Keys);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RejectsFileSelectedBothDirectlyAndThroughFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-overlap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var folder = Path.Combine(root, "docs");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, "readme.txt");
            File.WriteAllText(file, "hello");
            Assert.Throws<InvalidDataException>(() =>
                ArchiveSourcePlanner.Plan(new[] { folder, file }));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RejectsMissingSources()
    {
        Assert.Throws<FileNotFoundException>(() =>
            ArchiveSourcePlanner.Plan(new[] { Path.Combine(Path.GetTempPath(),
                "missing-" + Guid.NewGuid().ToString("N")) }));
    }

    [Fact]
    public void RejectsDuplicateNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "one.txt");
            File.WriteAllText(file, "one");
            Assert.Throws<InvalidDataException>(() => ArchiveSourcePlanner.Plan(new[] { file, file }));
        }
        finally { Directory.Delete(root, true); }
    }
}
