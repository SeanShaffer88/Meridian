using Meridian.Core.Foresight;

namespace Meridian.Core.Tests;

public class RelationshipAnalyzerTests
{
    [Fact]
    public void DetectsMatchingStemWithoutClaimingDependency()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "meridian-foresight"));
        var found = RelationshipAnalyzer.Analyze(new[]
        {
            Path.Combine(root, "design.psd"),
            Path.Combine(root, "design.png")
        });
        Assert.Single(found);
        Assert.Equal(RelationshipEvidence.FilenameStem, found[0].Evidence);
        Assert.Contains("does not establish a dependency", found[0].Reason);
    }

    [Fact]
    public void DetectsSharedDirectoryAsWeakEvidence()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "meridian-foresight"));
        var found = RelationshipAnalyzer.Analyze(new[]
        {
            Path.Combine(root, "first.txt"),
            Path.Combine(root, "second.jpg")
        });
        Assert.Single(found);
        Assert.Equal(RelationshipEvidence.SameDirectory, found[0].Evidence);
    }

    [Fact]
    public void RejectsDuplicatePaths()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "same.txt"));
        Assert.Throws<ArgumentException>(() => RelationshipAnalyzer.Analyze(new[] { path, path }));
    }

    [Fact]
    public void RejectsPathLimit()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "meridian-foresight"));
        Assert.Throws<ArgumentException>(() => RelationshipAnalyzer.Analyze(new[]
        {
            Path.Combine(root, "a"),
            Path.Combine(root, "b")
        }, maxPaths: 1));
    }

    [Fact]
    public void EmptyInputProducesNoFindings()
    {
        Assert.Empty(RelationshipAnalyzer.Analyze(Array.Empty<string>()));
    }

    [Fact]
    public void RejectsRelationshipLimitBeforeGeneratingUnboundedOutput()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "meridian-foresight"));
        var paths = Enumerable.Range(0, 500)
            .Select(i => Path.Combine(root, $"item-{i}.txt"));
        Assert.Throws<InvalidOperationException>(() =>
            RelationshipAnalyzer.Analyze(paths, maxRelationships: 50));
    }

    [Fact]
    public void AllowsExactRelationshipLimit()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "meridian-foresight"));
        var paths = Enumerable.Range(0, 5)
            .Select(i => Path.Combine(root, $"item-{i}.txt"));
        Assert.Equal(10, RelationshipAnalyzer.Analyze(paths, maxRelationships: 10).Count);
    }

    [Fact]
    public void RejectsInvalidRelationshipLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RelationshipAnalyzer.Analyze(Array.Empty<string>(), maxRelationships: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RelationshipAnalyzer.Analyze(Array.Empty<string>(), maxRelationships: 100001));
    }
}
