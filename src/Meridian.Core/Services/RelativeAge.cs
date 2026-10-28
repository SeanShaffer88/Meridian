namespace Meridian.Services;

/// <summary>Coarse age bands for an item's last-write time, from freshest to oldest.</summary>
public enum AgeBucket
{
    JustNow,
    Minutes,
    Hours,
    Yesterday,
    Days,
    Weeks,
    Months,
    Years,
}

/// <summary>
/// Friendly relative-age text for a last-write time ("just now", "5 minutes ago",
/// "Yesterday", "2 years ago"). Pure and clock-injectable so the boundary rules are
/// unit-tested; the same bucket also drives the restrained text tier a row uses.
/// </summary>
public static class RelativeAge
{
    public static AgeBucket BucketOf(DateTime modifiedUtc, DateTime nowUtc)
    {
        var age = nowUtc - modifiedUtc;
        if (age < TimeSpan.FromMinutes(1))
        {
            return AgeBucket.JustNow; // future write times land here too — never negative text
        }

        if (age < TimeSpan.FromHours(1))
        {
            return AgeBucket.Minutes;
        }

        if (age < TimeSpan.FromHours(24))
        {
            return AgeBucket.Hours;
        }

        if (age < TimeSpan.FromHours(48))
        {
            return AgeBucket.Yesterday;
        }

        if (age < TimeSpan.FromDays(7))
        {
            return AgeBucket.Days;
        }

        if (age < TimeSpan.FromDays(30))
        {
            return AgeBucket.Weeks;
        }

        if (age < TimeSpan.FromDays(365))
        {
            return AgeBucket.Months;
        }

        return AgeBucket.Years;
    }

    public static string Format(DateTime modifiedUtc, DateTime nowUtc)
    {
        var age = nowUtc - modifiedUtc;
        return BucketOf(modifiedUtc, nowUtc) switch
        {
            AgeBucket.JustNow => "just now",
            AgeBucket.Minutes => Plural((int)age.TotalMinutes, "minute"),
            AgeBucket.Hours => Plural((int)age.TotalHours, "hour"),
            AgeBucket.Yesterday => "Yesterday",
            AgeBucket.Days => Plural((int)age.TotalDays, "day"),
            AgeBucket.Weeks => Plural((int)(age.TotalDays / 7), "week"),
            AgeBucket.Months => Plural((int)(age.TotalDays / 30), "month"),
            _ => Plural((int)(age.TotalDays / 365), "year"),
        };
    }

    /// <summary>
    /// True for the buckets that count as recent (under two days old); the window
    /// renders those in the secondary text tier and older items in the tertiary tier.
    /// </summary>
    public static bool IsRecent(AgeBucket bucket) =>
        bucket is AgeBucket.JustNow or AgeBucket.Minutes or AgeBucket.Hours or AgeBucket.Yesterday;

    private static string Plural(int count, string unit) =>
        count == 1 ? $"1 {unit} ago" : $"{count} {unit}s ago";
}
