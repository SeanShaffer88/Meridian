using Meridian.Core.Transfers;

namespace Meridian.Core.Tests;

public sealed class CollisionResolverTests
{
    private static readonly DateTimeOffset Older = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Newer = new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
    private static FileFacts Facts(DateTimeOffset write) => new(100, write);

    [Theory]
    [InlineData(CollisionPolicy.Ask)]
    [InlineData(CollisionPolicy.Skip)]
    [InlineData(CollisionPolicy.KeepBoth)]
    [InlineData(CollisionPolicy.Replace)]
    [InlineData(CollisionPolicy.ReplaceIfNewer)]
    public void NoDestination_AlwaysProceeds(CollisionPolicy policy)
    {
        var decision = CollisionResolver.Resolve(policy, "dest/file.txt", Facts(Newer), null, false, _ => false);
        Assert.Equal(CollisionDecisionKind.Proceed, decision.Kind);
    }

    [Fact]
    public void Ask_WhenDestinationExists_NeedsTheUser()
    {
        var decision = CollisionResolver.Resolve(CollisionPolicy.Ask, "dest/file.txt", Facts(Newer), Facts(Older), false, _ => false);
        Assert.Equal(CollisionDecisionKind.NeedsUserChoice, decision.Kind);
    }

    [Fact]
    public void Skip_WhenDestinationExists_Skips()
    {
        var decision = CollisionResolver.Resolve(CollisionPolicy.Skip, "dest/file.txt", Facts(Newer), Facts(Older), false, _ => false);
        Assert.Equal(CollisionDecisionKind.Skip, decision.Kind);
    }

    [Fact]
    public void Replace_WhenDestinationExists_Replaces()
    {
        var decision = CollisionResolver.Resolve(CollisionPolicy.Replace, "dest/file.txt", Facts(Newer), Facts(Older), false, _ => false);
        Assert.Equal(CollisionDecisionKind.Replace, decision.Kind);
    }

    [Fact]
    public void ReplaceIfNewer_SourceNewer_Replaces()
    {
        var decision = CollisionResolver.Resolve(CollisionPolicy.ReplaceIfNewer, "dest/file.txt", Facts(Newer), Facts(Older), false, _ => false);
        Assert.Equal(CollisionDecisionKind.Replace, decision.Kind);
    }

    [Fact]
    public void ReplaceIfNewer_SourceOlder_Skips()
    {
        var decision = CollisionResolver.Resolve(CollisionPolicy.ReplaceIfNewer, "dest/file.txt", Facts(Older), Facts(Newer), false, _ => false);
        Assert.Equal(CollisionDecisionKind.Skip, decision.Kind);
    }

    [Fact]
    public void ReplaceIfNewer_SameAge_Skips()
    {
        var decision = CollisionResolver.Resolve(CollisionPolicy.ReplaceIfNewer, "dest/file.txt", Facts(Older), Facts(Older), false, _ => false);
        Assert.Equal(CollisionDecisionKind.Skip, decision.Kind);
    }

    [Fact]
    public void DirectoryCollision_MergesUnderEveryPolicy()
    {
        foreach (var policy in Enum.GetValues<CollisionPolicy>())
        {
            var decision = CollisionResolver.Resolve(policy, "dest/folder", Facts(Newer), Facts(Older), true, _ => false);
            Assert.Equal(CollisionDecisionKind.Proceed, decision.Kind);
        }
    }

    [Fact]
    public void KeepBoth_FirstFreeNumberedName()
    {
        var decision = CollisionResolver.Resolve(CollisionPolicy.KeepBoth, "dest/report.txt", Facts(Newer), Facts(Older), false, _ => false);
        Assert.Equal(CollisionDecisionKind.Rename, decision.Kind);
        Assert.Equal(Path.Combine("dest", "report (2).txt"), decision.NewDestinationPath);
    }

    [Fact]
    public void KeepBoth_SkipsTakenNumbers()
    {
        var taken = new HashSet<string> { Path.Combine("dest", "report (2).txt"), Path.Combine("dest", "report (3).txt") };
        var name = CollisionResolver.FirstFreeName(Path.Combine("dest", "report.txt"), taken.Contains);
        Assert.Equal(Path.Combine("dest", "report (4).txt"), name);
    }

    [Fact]
    public void FirstFreeName_ExtensionlessAndDotfileAndMultiDotNames()
    {
        Assert.Equal("notes (2)", CollisionResolver.FirstFreeName("notes", _ => false));
        Assert.Equal(".gitignore (2)", CollisionResolver.FirstFreeName(".gitignore", _ => false));
        Assert.Equal("archive.tar (2).gz", CollisionResolver.FirstFreeName("archive.tar.gz", _ => false));
    }
}
