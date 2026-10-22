using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class RecoveryReadinessTests
{
    private static readonly DateTime Modified = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly RecoveryReadiness.RecordedMove Move =
        new("C:\\original.txt", "D:\\moved.txt", 42, Modified);

    [Fact]
    public void RefusesWhenOriginalPathIsOccupied()
    {
        var result = RecoveryReadiness.Evaluate(Move, new(true, true, 42, Modified));
        Assert.Equal(RecoveryReadiness.Verdict.OriginalOccupied, result.Result);
        Assert.False(result.AuthorizesUndo);
    }

    [Fact]
    public void RefusesWhenDestinationIsMissing()
    {
        var result = RecoveryReadiness.Evaluate(Move, new(false, false, null, null));
        Assert.Equal(RecoveryReadiness.Verdict.DestinationMissing, result.Result);
        Assert.False(result.AuthorizesUndo);
    }

    [Fact]
    public void RefusesWhenDestinationMetadataChanged()
    {
        var result = RecoveryReadiness.Evaluate(Move, new(false, true, 43, Modified));
        Assert.Equal(RecoveryReadiness.Verdict.DestinationChanged, result.Result);
        Assert.False(result.AuthorizesUndo);
    }

    [Fact]
    public void MatchingMetadataStillDoesNotAuthorizeUndo()
    {
        var result = RecoveryReadiness.Evaluate(Move, new(false, true, 42, Modified));
        Assert.Equal(RecoveryReadiness.Verdict.CandidateRequiresVerification, result.Result);
        Assert.False(result.AuthorizesUndo);
    }
}
