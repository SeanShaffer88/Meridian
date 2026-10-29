namespace Meridian.Services;

/// <summary>
/// The burst-coalescing clock behind live folder refresh. WatchKit already
/// coalesces per path; this debouncer coalesces over time: while changes
/// keep arriving, the refresh waits for a short quiet gap so a burst (an
/// unzip, a build, a bulk copy) costs one listing reload instead of dozens —
/// but a maximum wait caps the delay so a folder under continuous churn
/// still refreshes regularly instead of starving. Pure and clock-injected,
/// so the timing rules are unit-tested without sleeping threads.
/// </summary>
public sealed class WatchDebouncer
{
    /// <summary>The quiet gap the app waits for before refreshing.</summary>
    public static readonly TimeSpan DefaultQuietPeriod = TimeSpan.FromMilliseconds(300);

    /// <summary>The longest a refresh can be postponed by a continuous burst.</summary>
    public static readonly TimeSpan DefaultMaximumWait = TimeSpan.FromSeconds(2);

    private readonly TimeSpan _quietPeriod;
    private readonly TimeSpan _maximumWait;
    private DateTimeOffset? _firstPendingAt;
    private DateTimeOffset? _lastChangeAt;

    public WatchDebouncer()
        : this(DefaultQuietPeriod, DefaultMaximumWait)
    {
    }

    public WatchDebouncer(TimeSpan quietPeriod, TimeSpan maximumWait)
    {
        if (quietPeriod < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(quietPeriod), quietPeriod, "The quiet period cannot be negative.");
        if (maximumWait < quietPeriod)
            throw new ArgumentOutOfRangeException(nameof(maximumWait), maximumWait, "The maximum wait must be at least the quiet period.");
        _quietPeriod = quietPeriod;
        _maximumWait = maximumWait;
    }

    /// <summary>True while a noted change has not yet been fired.</summary>
    public bool HasPending => _firstPendingAt is not null;

    /// <summary>Records a relevant change at <paramref name="now"/>: the
    /// first one opens the pending window, every later one only extends the
    /// quiet-gap measurement.</summary>
    public void NoteChange(DateTimeOffset now)
    {
        _firstPendingAt ??= now;
        _lastChangeAt = now;
    }

    /// <summary>True when the pending refresh should fire at
    /// <paramref name="now"/>: the burst has been quiet for the quiet
    /// period, or the pending window has reached the maximum wait.</summary>
    public bool IsDue(DateTimeOffset now)
    {
        if (_firstPendingAt is not { } first || _lastChangeAt is not { } last) return false;
        return now - last >= _quietPeriod || now - first >= _maximumWait;
    }

    /// <summary>Clears the pending window after the refresh fires (or when
    /// the watch moves to a different folder and old news no longer
    /// applies).</summary>
    public void MarkFired()
    {
        _firstPendingAt = null;
        _lastChangeAt = null;
    }
}
