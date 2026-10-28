using Meridian.Core.Foresight;

namespace Meridian.Core.Tests;

public class DependencyImpactAnalyzerTests
{
    private static string Root => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "meridian-dependency"));

    [Fact]
    public void ReportsIncomingReferencesWithProvenance()
    {
        var target = Path.Combine(Root, "asset.png");
        var source = Path.Combine(Root, "project.json");
        var result = DependencyImpactAnalyzer.Preview(target,
            new[] { new DependencyEdge(source, target, "Manifest reference at line 7") });
        Assert.Single(result.IncomingReferences);
        Assert.Contains("line 7", result.IncomingReferences[0].Evidence);
    }

    [Fact]
    public void IgnoresOutgoingReferencesAndDeduplicatesIncoming()
    {
        var target = Path.Combine(Root, "asset.png");
        var source = Path.Combine(Root, "project.json");
        var result = DependencyImpactAnalyzer.Preview(target, new[]
        {
            new DependencyEdge(source, target, "First"),
            new DependencyEdge(source, target, "Second"),
            new DependencyEdge(target, source, "Outgoing")
        });
        Assert.Single(result.IncomingReferences);
    }

    [Fact]
    public void EmptyEvidenceNeverClaimsSafety()
    {
        var result = DependencyImpactAnalyzer.Preview(Path.Combine(Root, "asset.png"), Array.Empty<DependencyEdge>());
        Assert.Empty(result.IncomingReferences);
        Assert.Contains("incomplete", result.Warning);
    }

    [Fact]
    public void RejectsUnboundedEvidence()
    {
        var target = Path.Combine(Root, "asset.png");
        var edges = new[]
        {
            new DependencyEdge(Path.Combine(Root, "one"), target, "one"),
            new DependencyEdge(Path.Combine(Root, "two"), target, "two")
        };
        Assert.Throws<ArgumentException>(() => DependencyImpactAnalyzer.Preview(target, edges, maxEdges: 1));
    }
}
