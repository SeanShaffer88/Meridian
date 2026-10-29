using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class DriveSummaryTests
{
    [Fact]
    public void UsedFraction_HalfFullDrive_IsOneHalf()
    {
        Assert.Equal(0.5, DriveSummary.UsedFraction(totalBytes: 1000, freeBytes: 500), precision: 10);
    }

    [Fact]
    public void UsedFraction_EmptyAndFullDrives_AreZeroAndOne()
    {
        Assert.Equal(0.0, DriveSummary.UsedFraction(totalBytes: 1000, freeBytes: 1000), precision: 10);
        Assert.Equal(1.0, DriveSummary.UsedFraction(totalBytes: 1000, freeBytes: 0), precision: 10);
    }

    [Fact]
    public void UsedFraction_UnknownOrImpossibleSizes_AreClamped()
    {
        Assert.Equal(0.0, DriveSummary.UsedFraction(totalBytes: 0, freeBytes: 0), precision: 10);
        Assert.Equal(0.0, DriveSummary.UsedFraction(totalBytes: -5, freeBytes: 0), precision: 10);
        // More free space than the total (a stale read) reads as empty, never negative.
        Assert.Equal(0.0, DriveSummary.UsedFraction(totalBytes: 1000, freeBytes: 1500), precision: 10);
    }

    [Fact]
    public void FreeSpaceLabel_FormatsBothSizes()
    {
        var label = DriveSummary.FreeSpaceLabel(totalBytes: 500L * 1024 * 1024 * 1024, freeBytes: 125L * 1024 * 1024 * 1024);
        Assert.Equal("125 GB free of 500 GB", label);
    }

    [Fact]
    public void FreeSpaceLabel_UnknownTotal_SaysSoPlainly()
    {
        Assert.Equal("Size not available", DriveSummary.FreeSpaceLabel(totalBytes: 0, freeBytes: 0));
    }
}

public sealed class DriveSummaryUsedPercentTests
{
    [Fact]
    public void UsedPercent_HalfFullDrive_Is50()
    {
        Assert.Equal(50, DriveSummary.UsedPercent(totalBytes: 1000, freeBytes: 500));
    }

    [Fact]
    public void UsedPercent_RoundsToTheNearestWholePercent()
    {
        Assert.Equal(33, DriveSummary.UsedPercent(totalBytes: 300, freeBytes: 200));
        Assert.Equal(80, DriveSummary.UsedPercent(totalBytes: 1000, freeBytes: 199));
    }

    [Fact]
    public void UsedPercent_UnknownSizes_AreZero()
    {
        Assert.Equal(0, DriveSummary.UsedPercent(totalBytes: 0, freeBytes: 0));
        Assert.Equal(0, DriveSummary.UsedPercent(totalBytes: -5, freeBytes: 0));
    }
}
