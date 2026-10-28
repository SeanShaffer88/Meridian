using Meridian.Core.Archives;

namespace Meridian.Core.Tests;

public sealed class ArchivePlanningReliabilityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "meridian-plan-reliability-" + Guid.NewGuid().ToString("N"));

    public ArchivePlanningReliabilityTests() => Directory.CreateDirectory(root);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsEmptyFolderAliases(bool trailingFirst)
    {
        var empty = Directory.CreateDirectory(Path.Combine(root, "empty")).FullName;
        var file = Write("data.txt");
        var alias = empty + Path.DirectorySeparatorChar;
        var paths = trailingFirst ? new[] { alias, empty, file } : new[] { empty, alias, file };
        Assert.Throws<InvalidDataException>(() => ArchiveSourcePlanner.Plan(paths));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsParentFileConflictInEitherSelectionOrder(bool fileFirst)
    {
        var standalone = Write("files/docs");
        var nested = Write("docs/nested/data.txt");
        var folder = Path.Combine(root, "docs");
        var paths = fileFirst ? new[] { standalone, folder } : new[] { folder, standalone };
        Assert.Throws<InvalidDataException>(() => ArchiveSourcePlanner.Plan(paths));
        Assert.Equal("payload", File.ReadAllText(standalone));
        Assert.Equal("payload", File.ReadAllText(nested));
    }

    [Fact]
    public void WideTreeBudgetFailsBeforeRequestingAnotherSelection()
    {
        // Invalid entry names in every child distinguish queue-budget rejection
        // from walking a child before accounting for its queued siblings.
        for (var i = 0; i < 32; i++) Write("child" + i + "/ bad.txt");
        var error = Assert.Throws<InvalidDataException>(() => ArchiveSourcePlanner.Plan(Selections(), maxDirectories: 4));
        Assert.Contains("budget", error.Message, StringComparison.OrdinalIgnoreCase);

        IEnumerable<string> Selections()
        {
            yield return root;
            throw new InvalidOperationException("Planner consumed beyond the over-budget tree.");
        }
    }

    [Fact]
    public void DirectoryBudgetIsSharedAcrossIndependentRoots()
    {
        var first = Directory.CreateDirectory(Path.Combine(root, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
        Write("first/data.txt");
        Write("second/data.txt");
        Assert.Throws<InvalidDataException>(() => ArchiveSourcePlanner.Plan(new[] { first, second }, maxDirectories: 1));
        Assert.Equal(2, ArchiveSourcePlanner.Plan(new[] { first, second }, maxDirectories: 2).Count);
    }

    [Fact]
    public void WideTreeSucceedsAtExactDiscoveredDirectoryBudget()
    {
        for (var i = 0; i < 32; i++) Write("child" + i + "/data.txt");
        Assert.Equal(32, ArchiveSourcePlanner.Plan(new[] { root }, maxDirectories: 33).Count);
        Assert.Throws<InvalidDataException>(() => ArchiveSourcePlanner.Plan(new[] { root }, maxDirectories: 32));
    }

    [Fact]
    public void SimilarEntryPrefixesAreNotParentFileConflicts()
    {
        var file = Write("files/docs");
        Write("docs-extra/data.txt");
        var plan = ArchiveSourcePlanner.Plan(new[] { file, Path.Combine(root, "docs-extra") });
        Assert.Equal(2, plan.Count);
    }

    [Fact]
    public void DotSegmentAliasesOfEmptyFoldersAreRejected()
    {
        var empty = Directory.CreateDirectory(Path.Combine(root, "empty")).FullName;
        var file = Write("data.txt");
        Assert.Throws<InvalidDataException>(() => ArchiveSourcePlanner.Plan(new[] { empty, Path.Combine(empty, "."), file }));
    }

    [Fact]
    public void CancellationBetweenSelectedRootsStopsPlanning()
    {
        var file = Write("data.txt");
        using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => ArchiveSourcePlanner.Plan(Selections(), cancellation.Token));

        IEnumerable<string> Selections()
        {
            yield return file;
            cancellation.Cancel();
            yield return Path.Combine(root, "missing");
        }
    }

    [Theory]
    [InlineData(".zip")]
    [InlineData(".tar")]
    [InlineData(".tgz")]
    public async Task ValidPlanWithTrailingSeparatorRoundTrips(string extension)
    {
        Write("docs/nested/data.txt");
        var plan = ArchiveSourcePlanner.Plan(new[] { Path.Combine(root, "docs") + Path.DirectorySeparatorChar });
        Assert.Contains("docs/nested/data.txt", plan.Keys);
        var archive = Path.Combine(root, "archive" + extension);
        if (extension == ".zip")
        {
            await ZipArchiveCreator.CreateAsync(archive, plan);
            await ZipTreeExtractor.ExtractAsync(archive, root, "output");
        }
        else
        {
            await TarArchiveCreator.CreateAsync(archive, plan);
            await TarTreeExtractor.ExtractAsync(archive, root, "output");
        }
        Assert.Equal("payload", File.ReadAllText(Path.Combine(root, "output", "docs", "nested", "data.txt")));
    }

    private string Write(string relative)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "payload");
        return path;
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
