using Meridian.Services;

namespace Meridian.Tests;

public sealed class WatchDebouncerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private static WatchDebouncer NewDebouncer() =>
        new(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(2));

    [Fact]
    public void NothingPendingIsNeverDue()
    {
        var debouncer = NewDebouncer();
        Assert.False(debouncer.HasPending);
        Assert.False(debouncer.IsDue(Start));
    }

    [Fact]
    public void AChangeIsDueOnlyAfterTheQuietPeriod()
    {
        var debouncer = NewDebouncer();
        debouncer.NoteChange(Start);
        Assert.True(debouncer.HasPending);
        Assert.False(debouncer.IsDue(Start + TimeSpan.FromMilliseconds(299)));
        Assert.True(debouncer.IsDue(Start + TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public void EachNewChangeRestartsTheQuietGap()
    {
        var debouncer = NewDebouncer();
        debouncer.NoteChange(Start);
        debouncer.NoteChange(Start + TimeSpan.FromMilliseconds(250));
        Assert.False(debouncer.IsDue(Start + TimeSpan.FromMilliseconds(549)));
        Assert.True(debouncer.IsDue(Start + TimeSpan.FromMilliseconds(550)));
    }

    [Fact]
    public void AContinuousBurstStillFiresAtTheMaximumWait()
    {
        var debouncer = NewDebouncer();
        // A change every 100 ms: the quiet gap never arrives, so only the
        // maximum wait can make the refresh due.
        for (var step = 0; step < 20; step++)
            debouncer.NoteChange(Start + TimeSpan.FromMilliseconds(100 * step));
        Assert.False(debouncer.IsDue(Start + TimeSpan.FromMilliseconds(1999)));
        Assert.True(debouncer.IsDue(Start + TimeSpan.FromMilliseconds(2000)));
    }

    [Fact]
    public void MarkFiredClearsThePendingWindow()
    {
        var debouncer = NewDebouncer();
        debouncer.NoteChange(Start);
        debouncer.MarkFired();
        Assert.False(debouncer.HasPending);
        Assert.False(debouncer.IsDue(Start + TimeSpan.FromSeconds(10)));
        // And the next change starts a fresh window.
        debouncer.NoteChange(Start + TimeSpan.FromSeconds(11));
        Assert.False(debouncer.IsDue(Start + TimeSpan.FromSeconds(11) + TimeSpan.FromMilliseconds(100)));
        Assert.True(debouncer.IsDue(Start + TimeSpan.FromSeconds(11) + TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public void TheDefaultWindowsMatchTheAppContract()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(300), WatchDebouncer.DefaultQuietPeriod);
        Assert.Equal(TimeSpan.FromSeconds(2), WatchDebouncer.DefaultMaximumWait);
        var debouncer = new WatchDebouncer();
        debouncer.NoteChange(Start);
        Assert.True(debouncer.IsDue(Start + WatchDebouncer.DefaultQuietPeriod));
    }

    [Fact]
    public void AMaximumWaitBelowTheQuietPeriodIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WatchDebouncer(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
}
