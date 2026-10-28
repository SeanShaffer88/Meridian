using Meridian.Services;

namespace Meridian.Core.Tests;

/// <summary>
/// Relative-age text and buckets across every boundary: the minute/hour/day
/// rollovers, the Yesterday band, week/month approximations, and future times.
/// </summary>
public sealed class RelativeAgeTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private static string FormatSecondsAgo(double seconds) =>
        RelativeAge.Format(Now.AddSeconds(-seconds), Now);

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "1 minute ago")]
    [InlineData(119, "1 minute ago")]
    [InlineData(120, "2 minutes ago")]
    [InlineData(59 * 60, "59 minutes ago")]
    [InlineData(60 * 60, "1 hour ago")]
    [InlineData(23 * 3600, "23 hours ago")]
    [InlineData(24 * 3600, "Yesterday")]
    [InlineData(48 * 3600 - 60, "Yesterday")]
    [InlineData(48 * 3600, "2 days ago")]
    [InlineData(6 * 86400, "6 days ago")]
    [InlineData(7 * 86400, "1 week ago")]
    [InlineData(14 * 86400, "2 weeks ago")]
    [InlineData(29 * 86400, "4 weeks ago")]
    [InlineData(30 * 86400, "1 month ago")]
    [InlineData(60 * 86400, "2 months ago")]
    [InlineData(364 * 86400, "12 months ago")]
    [InlineData(365 * 86400, "1 year ago")]
    [InlineData(730 * 86400, "2 years ago")]
    [InlineData(-300, "just now")] // a write time in the future never produces negative text
    public void Format_AcrossBoundaries(double secondsAgo, string expected) =>
        Assert.Equal(expected, FormatSecondsAgo(secondsAgo));

    [Theory]
    [InlineData(30, AgeBucket.JustNow)]
    [InlineData(90, AgeBucket.Minutes)]
    [InlineData(2 * 3600, AgeBucket.Hours)]
    [InlineData(30 * 3600, AgeBucket.Yesterday)]
    [InlineData(3 * 86400, AgeBucket.Days)]
    [InlineData(10 * 86400, AgeBucket.Weeks)]
    [InlineData(90 * 86400, AgeBucket.Months)]
    [InlineData(800 * 86400, AgeBucket.Years)]
    public void BucketOf_AcrossBoundaries(double secondsAgo, AgeBucket expected) =>
        Assert.Equal(expected, RelativeAge.BucketOf(Now.AddSeconds(-secondsAgo), Now));

    [Theory]
    [InlineData(AgeBucket.JustNow, true)]
    [InlineData(AgeBucket.Minutes, true)]
    [InlineData(AgeBucket.Hours, true)]
    [InlineData(AgeBucket.Yesterday, true)]
    [InlineData(AgeBucket.Days, false)]
    [InlineData(AgeBucket.Weeks, false)]
    [InlineData(AgeBucket.Months, false)]
    [InlineData(AgeBucket.Years, false)]
    public void IsRecent_OnlyTheUnderTwoDayBuckets(AgeBucket bucket, bool expected) =>
        Assert.Equal(expected, RelativeAge.IsRecent(bucket));
}
