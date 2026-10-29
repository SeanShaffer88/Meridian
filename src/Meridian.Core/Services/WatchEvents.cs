using System.Text.Json;

namespace Meridian.Services;

/// <summary>The kinds of change WatchKit reports. The kit's vocabulary is
/// closed today; a type string this build does not know maps to
/// <see cref="Unknown"/> rather than failing the batch, so a newer kit can
/// never break the parser.</summary>
public enum WatchEventKind
{
    Created,
    Modified,
    Deleted,
    Renamed,
    Unknown,
}

/// <summary>One coalesced change from WatchKit. For a rename,
/// <see cref="Path"/> is the new path and <see cref="OldPath"/> the previous
/// one; for every other kind <see cref="OldPath"/> is null.
/// <see cref="CoalescedCount"/> is how many raw changes this one event
/// stands for.</summary>
public sealed record WatchEvent(WatchEventKind Kind, string Path, string? OldPath, long CoalescedCount, ulong Seq);

/// <summary>
/// One drained batch from <c>wa_poll_events</c>. Polling consumes: the
/// caller passes <see cref="NextSeq"/> back as the since-seq of the next
/// poll. <see cref="Dropped"/> counts events the kit's bounded queue
/// overflowed past — loss is a number, never silent.
/// </summary>
public sealed record WatchEventBatch(IReadOnlyList<WatchEvent> Events, ulong NextSeq, long Dropped, long QueueLength)
{
    /// <summary>Parses one poll result. Throws <see cref="JsonException"/>
    /// on malformed input: the JSON comes from the kit under its documented
    /// contract, so a shape this cannot read is a contract break the caller
    /// should treat as a dead watcher, not as "nothing changed".</summary>
    public static WatchEventBatch Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var events = new List<WatchEvent>();
        if (root.TryGetProperty("events", out var eventsElement) && eventsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in eventsElement.EnumerateArray())
            {
                var kind = element.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                    ? typeElement.GetString() switch
                    {
                        "created" => WatchEventKind.Created,
                        "modified" => WatchEventKind.Modified,
                        "deleted" => WatchEventKind.Deleted,
                        "renamed" => WatchEventKind.Renamed,
                        _ => WatchEventKind.Unknown,
                    }
                    : WatchEventKind.Unknown;
                var path = element.TryGetProperty("path", out var pathElement) && pathElement.ValueKind == JsonValueKind.String
                    ? pathElement.GetString() ?? string.Empty
                    : string.Empty;
                var oldPath = element.TryGetProperty("old_path", out var oldElement) && oldElement.ValueKind == JsonValueKind.String
                    ? oldElement.GetString()
                    : null;
                var coalesced = element.TryGetProperty("coalesced_count", out var countElement) && countElement.TryGetInt64(out var count)
                    ? count
                    : 1;
                var seq = element.TryGetProperty("seq", out var seqElement) && seqElement.TryGetUInt64(out var seqValue)
                    ? seqValue
                    : 0UL;
                events.Add(new WatchEvent(kind, path, oldPath, coalesced, seq));
            }
        }

        var nextSeq = root.TryGetProperty("next_seq", out var nextElement) && nextElement.TryGetUInt64(out var next)
            ? next
            : 0UL;
        var dropped = root.TryGetProperty("dropped", out var droppedElement) && droppedElement.TryGetInt64(out var droppedValue)
            ? droppedValue
            : 0L;
        var queueLength = root.TryGetProperty("queue_len", out var queueElement) && queueElement.TryGetInt64(out var queueValue)
            ? queueValue
            : 0L;
        return new WatchEventBatch(events, nextSeq, dropped, queueLength);
    }
}

/// <summary>The parts of WatchKit's <c>wa_status</c> the app reads: which
/// backend is ACTUALLY running (a native request the kit could not honour
/// reports "polling" — it never claims a backend that is not running), plus
/// the tracked-file and dropped-event counters for diagnostics.</summary>
public sealed record WatchStatus(string? Backend, long FilesTracked, long Dropped)
{
    /// <summary>Parses one status result; missing fields read as null/0.
    /// Throws <see cref="JsonException"/> on malformed input, like
    /// <see cref="WatchEventBatch.Parse"/>.</summary>
    public static WatchStatus Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var backend = root.TryGetProperty("backend", out var backendElement) && backendElement.ValueKind == JsonValueKind.String
            ? backendElement.GetString()
            : null;
        var files = root.TryGetProperty("files_tracked", out var filesElement) && filesElement.TryGetInt64(out var filesValue)
            ? filesValue
            : 0L;
        var dropped = root.TryGetProperty("dropped", out var droppedElement) && droppedElement.TryGetInt64(out var droppedValue)
            ? droppedValue
            : 0L;
        return new WatchStatus(backend, files, dropped);
    }
}
