using System.Text.Json;
using Meridian.Services;

namespace Meridian.Tests;

public sealed class WatchEventsTests
{
    // The exact shape watchkit.h documents for wa_poll_events.
    private const string HeaderExample = """
        {"events":[{"seq":1,"type":"created","path":"/data/a.txt","old_path":null,"coalesced_count":2}],
         "next_seq":1,"dropped":0,"queue_len":0}
        """;

    [Fact]
    public void ParsesTheDocumentedPollShape()
    {
        var batch = WatchEventBatch.Parse(HeaderExample);
        Assert.Equal(1UL, batch.NextSeq);
        Assert.Equal(0L, batch.Dropped);
        Assert.Equal(0L, batch.QueueLength);
        var change = Assert.Single(batch.Events);
        Assert.Equal(WatchEventKind.Created, change.Kind);
        Assert.Equal("/data/a.txt", change.Path);
        Assert.Null(change.OldPath);
        Assert.Equal(2L, change.CoalescedCount);
        Assert.Equal(1UL, change.Seq);
    }

    [Fact]
    public void ParsesARenameWithBothPaths()
    {
        var batch = WatchEventBatch.Parse("""
            {"events":[{"seq":7,"type":"renamed","path":"C:\\Data\\new.txt","old_path":"C:\\Data\\old.txt","coalesced_count":1}],
             "next_seq":8,"dropped":3,"queue_len":5}
            """);
        var change = Assert.Single(batch.Events);
        Assert.Equal(WatchEventKind.Renamed, change.Kind);
        Assert.Equal(@"C:\Data\new.txt", change.Path);
        Assert.Equal(@"C:\Data\old.txt", change.OldPath);
        Assert.Equal(8UL, batch.NextSeq);
        Assert.Equal(3L, batch.Dropped);
        Assert.Equal(5L, batch.QueueLength);
    }

    [Fact]
    public void ParsesAnEmptyBatch()
    {
        var batch = WatchEventBatch.Parse("""{"events":[],"next_seq":42,"dropped":0,"queue_len":0}""");
        Assert.Empty(batch.Events);
        Assert.Equal(42UL, batch.NextSeq);
    }

    [Fact]
    public void AnUnknownEventTypeReadsAsUnknownNotAnError()
    {
        var batch = WatchEventBatch.Parse("""
            {"events":[{"seq":1,"type":"teleported","path":"/x","old_path":null,"coalesced_count":1}],
             "next_seq":2,"dropped":0,"queue_len":0}
            """);
        Assert.Equal(WatchEventKind.Unknown, Assert.Single(batch.Events).Kind);
    }

    [Fact]
    public void MalformedJsonThrows() =>
        Assert.ThrowsAny<JsonException>(() => WatchEventBatch.Parse("this is not json"));

    [Fact]
    public void ParsesTheStatusBackendAndCounters()
    {
        var status = WatchStatus.Parse("""
            {"roots":["C:\\Data"],"poll_interval_ms":250,"backend":"native","files_tracked":128,
             "events_emitted":9,"queue_len":0,"dropped":2,"next_seq":10,"scans":4,
             "cache_entries":0,"cache_hits":0,"cache_misses":0,"invalidations":0}
            """);
        Assert.Equal("native", status.Backend);
        Assert.Equal(128L, status.FilesTracked);
        Assert.Equal(2L, status.Dropped);
    }

    [Fact]
    public void StatusToleratesMissingFields()
    {
        var status = WatchStatus.Parse("""{"backend":"polling"}""");
        Assert.Equal("polling", status.Backend);
        Assert.Equal(0L, status.FilesTracked);
    }
}
