using Meridian.Core.Foresight;

namespace Meridian.Core.Tests;

public class ForesightImpactAnalyzerTests
{
    private static string Root => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "meridian-foresight-impact"));

    [Fact]
    public void ShowsHintsRelatedToSelectedTargetOnly()
    {
        var report = ForesightImpactAnalyzer.Preview(
            Path.Combine(Root, "project.psd"),
            new[] { Path.Combine(Root, "project.png"), Path.Combine(Root, "other.txt") });
        Assert.Equal(3, report.ExaminedPathCount);
        Assert.Equal(2, report.RelatedHints.Count);
        Assert.Contains("does not imply safety", report.Limitation);
    }

    [Fact]
    public void NoHintsDoesNotClaimSafety()
    {
        var report = ForesightImpactAnalyzer.Preview(Path.Combine(Root, "alone.txt"), Array.Empty<string>());
        Assert.Empty(report.RelatedHints);
        Assert.Contains("No file contents", report.Limitation);
    }

    [Fact]
    public void RejectsDuplicateContextPaths()
    {
        var path = Path.Combine(Root, "same.txt");
        Assert.Throws<ArgumentException>(() => ForesightImpactAnalyzer.Preview(
            Path.Combine(Root, "target.txt"), new[] { path, path }));
    }

    [Fact]
    public void RejectsRelativeTarget()
    {
        Assert.Throws<ArgumentException>(() =>
            ForesightImpactAnalyzer.Preview("relative.txt", Array.Empty<string>()));
    }
}
