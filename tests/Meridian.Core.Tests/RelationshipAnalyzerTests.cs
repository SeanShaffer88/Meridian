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

    [Fact]
    public void IndexPreservesInputPairOrderAndStemPriority()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "meridian-indexed"));
        var paths = new[]
        {
            Path.Combine(root, "alpha.txt"),
            Path.Combine(root, "beta.txt"),
            Path.Combine(root, "alpha.png"),
            Path.Combine(root, "sub", "alpha.csv")
        };
        var results = RelationshipAnalyzer.Analyze(paths);
        Assert.Equal(5, results.Count);
        Assert.Equal(new[] { paths[1], paths[2], paths[3], paths[2], paths[3] },
            results.Select(x => x.RelatedPath).ToArray());
        Assert.Equal(new[] {
            RelationshipEvidence.SameDirectory,
            RelationshipEvidence.FilenameStem,
            RelationshipEvidence.FilenameStem,
            RelationshipEvidence.SameDirectory,
            RelationshipEvidence.FilenameStem
        }, results.Select(x => x.Evidence).ToArray());
    }

    [Fact]
    public void DisjointDirectoriesAndStemsProduceNoCandidates()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "meridian-indexed"));
        var paths = Enumerable.Range(0, 1000)
            .Select(i => Path.Combine(root, $"folder-{i}", $"file-{i}.txt"));
        Assert.Empty(RelationshipAnalyzer.Analyze(paths));
    }

    [Fact]
    public void AlreadyCancelledAnalysisThrows()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "foresight-cancel.txt"));
        Assert.Throws<OperationCanceledException>(() =>
            RelationshipAnalyzer.Analyze(new[] { path }, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void CancellationDuringInputEnumerationStopsBeforeIndexing()
    {
        using var cancellation = new CancellationTokenSource();
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "foresight-cancel"));
        IEnumerable<string> Paths()
        {
            yield return Path.Combine(root, "first.txt");
            cancellation.Cancel();
            yield return Path.Combine(root, "second.txt");
        }
        Assert.Throws<OperationCanceledException>(() =>
            RelationshipAnalyzer.Analyze(Paths(), cancellationToken: cancellation.Token));
    }

    [Fact]
    public void CancellationAtEndOfInputEnumerationStopsBeforeReturningResults()
    {
        using var cancellation = new CancellationTokenSource();
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "foresight-cancel"));
        IEnumerable<string> Paths()
        {
            yield return Path.Combine(root, "first.txt");
            yield return Path.Combine(root, "second.txt");
            cancellation.Cancel();
        }
        Assert.Throws<OperationCanceledException>(() =>
            RelationshipAnalyzer.Analyze(Paths(), cancellationToken: cancellation.Token));
    }

    [Fact]
    public void RejectsInvalidTimeBudgets()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RelationshipAnalyzer.Analyze(Array.Empty<string>(), timeBudget: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RelationshipAnalyzer.Analyze(Array.Empty<string>(), timeBudget: TimeSpan.FromMinutes(6)));
    }

    [Fact]
    public void GenerousTimeBudgetPreservesResults()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "foresight-budget"));
        var paths = new[] { Path.Combine(root, "a.txt"), Path.Combine(root, "b.txt") };
        Assert.Single(RelationshipAnalyzer.Analyze(paths, timeBudget: TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void ExpiredTimeBudgetStopsAnalysis()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "foresight-budget"));
        IEnumerable<string> SlowPaths()
        {
            yield return Path.Combine(root, "a.txt");
            Thread.Sleep(50);
            yield return Path.Combine(root, "b.txt");
        }
        Assert.Throws<TimeoutException>(() =>
            RelationshipAnalyzer.Analyze(SlowPaths(), timeBudget: TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void MaximumSparseInputRemainsBounded()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "foresight-sparse"));
        var paths = Enumerable.Range(0, 10000)
            .Select(i => Path.Combine(root, $"dir-{i}", $"file-{i}.txt"));
        Assert.Empty(RelationshipAnalyzer.Analyze(paths, maxPaths: 10000, timeBudget: TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void ExpiredBudgetOnSingleInputThrowsEvenWithoutRelationships()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "foresight-slow-single.txt"));
        IEnumerable<string> SlowPaths()
        {
            Thread.Sleep(50);
            yield return path;
        }
        Assert.Throws<TimeoutException>(() =>
            RelationshipAnalyzer.Analyze(SlowPaths(), timeBudget: TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void CancellationWinsOverExpiredTimeBudget()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            RelationshipAnalyzer.Analyze(Array.Empty<string>(),
                cancellationToken: cancellation.Token, timeBudget: TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void IndexedCandidatesMatchReferencePairwiseAlgorithm()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "foresight-equivalence"));
        var random = new Random(8675309);
        for (var iteration = 0; iteration < 50; iteration++)
        {
            var paths = Enumerable.Range(0, 45)
                .Select(i => Path.Combine(root, $"dir-{random.Next(8)}",
                    $"stem-{random.Next(9)}-{i % 3}.{i}"))
                .ToArray();

            var actual = RelationshipAnalyzer.Analyze(paths);
            var expected = new List<(string Source, string Related, RelationshipEvidence Evidence)>();
            var comparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            for (var i = 0; i < paths.Length; i++)
                for (var j = i + 1; j < paths.Length; j++)
                {
                    var sameStem = comparer.Equals(Path.GetFileNameWithoutExtension(paths[i]),
                        Path.GetFileNameWithoutExtension(paths[j]));
                    if (sameStem)
                        expected.Add((paths[i], paths[j], RelationshipEvidence.FilenameStem));
                    else if (comparer.Equals(Path.GetDirectoryName(paths[i]), Path.GetDirectoryName(paths[j])))
                        expected.Add((paths[i], paths[j], RelationshipEvidence.SameDirectory));
                }

            Assert.Equal(expected, actual.Select(x => (x.SourcePath, x.RelatedPath, x.Evidence)));
        }
    }
}
