using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Core.Transfers;

/// <summary>
/// The durable record of one item of a finished job: the exact source → destination pair
/// that was acted on, plus the destination facts observed right after publication — the
/// evidence an undo plan later checks before it offers to reverse anything.
/// </summary>
public sealed record TransferHistoryItem(
    string SourcePath,
    string? DestinationPath,
    bool IsDirectory,
    long Size,
    DateTimeOffset? SourceLastWriteUtc,
    long? DestinationSize,
    DateTimeOffset? DestinationLastWriteUtc,
    TransferItemState FinalState,
    string? Error);

/// <summary>The durable record of one finished job, kept in the queue store's history.</summary>
public sealed record TransferHistoryRecord(
    Guid Id,
    Guid JobId,
    TransferJobKind Kind,
    TransferJobState FinalState,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? StartedUtc,
    DateTimeOffset FinishedUtc,
    string DestinationFolder,
    IReadOnlyList<TransferHistoryItem> Items)
{
    /// <summary>Snapshots a terminal job into its history record.</summary>
    public static TransferHistoryRecord FromJob(TransferJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!job.IsTerminal)
            throw new InvalidOperationException("Only a finished job can be recorded in history.");
        return new TransferHistoryRecord(
            Guid.NewGuid(),
            job.Id,
            job.Kind,
            job.State,
            job.CreatedUtc,
            job.StartedUtc,
            job.FinishedUtc ?? DateTimeOffset.UtcNow,
            job.DestinationFolder,
            job.Items.Select(item => new TransferHistoryItem(
                item.SourcePath,
                item.DestinationPath,
                item.IsDirectory,
                item.Size,
                item.SourceLastWriteUtc,
                item.DestinationSize,
                item.DestinationLastWriteUtc,
                item.State,
                item.Error)).ToList());
    }
}

/// <summary>
/// The durable transfer queue: jobs that still owe work (queued, paused, waiting on a
/// conflict) plus the history of finished jobs, stored as two JSON files in a caller-supplied
/// folder. Writes follow the established store pattern — serialize to a temporary file, then
/// atomically move it over the real one — and damaged or missing files load as empty rather
/// than failing, because a broken queue file must never stop the app from opening.
///
/// A job that was Running when saved reloads as Paused (its in-progress item returns to
/// Pending): file IO never auto-resumes after a restart; the user restarts it deliberately.
/// History is capped at <see cref="MaximumHistoryEntries"/> most recent records; older
/// records are dropped and their jobs can no longer be undone from Meridian's records.
/// </summary>
public sealed class TransferQueueStore
{
    /// <summary>The most finished-job records kept; older ones fall off the end.</summary>
    public const int MaximumHistoryEntries = 100;

    private const string QueueFileName = "transfer-queue.json";
    private const string HistoryFileName = "transfer-history.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _queuePath;
    private readonly string _historyPath;
    private readonly List<TransferJob> _jobs = [];
    private readonly List<TransferHistoryRecord> _history = []; // most recent first

    public TransferQueueStore(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var folder = Path.GetFullPath(folderPath);
        _queuePath = Path.Combine(folder, QueueFileName);
        _historyPath = Path.Combine(folder, HistoryFileName);
    }

    /// <summary>Jobs that still owe work, in the order they were queued.</summary>
    public IReadOnlyList<TransferJob> Jobs => _jobs;

    /// <summary>Finished jobs, most recent first, capped at <see cref="MaximumHistoryEntries"/>.</summary>
    public IReadOnlyList<TransferHistoryRecord> History => _history;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _jobs.Clear();
        _history.Clear();

        var savedJobs = await ReadAsync<List<TransferJob>>(_queuePath, cancellationToken);
        if (savedJobs is not null)
        {
            foreach (var job in savedJobs)
            {
                if (job is null) continue;
                job.Items ??= [];
                job.Sources ??= [];
                NormalizeAfterRestart(job);
                if (!job.IsTerminal) _jobs.Add(job);
            }
        }

        var savedHistory = await ReadAsync<List<TransferHistoryRecord>>(_historyPath, cancellationToken);
        if (savedHistory is not null)
        {
            foreach (var record in savedHistory)
            {
                if (record is null) continue;
                _history.Add(record);
                if (_history.Count >= MaximumHistoryEntries) break;
            }
        }
    }

    /// <summary>Persists the queue and history on a best-effort basis (see class remarks).</summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await WriteAsync(_queuePath, _jobs, cancellationToken);
        await WriteAsync(_historyPath, _history, cancellationToken);
    }

    /// <summary>Adds a job to the queue and saves.</summary>
    public async Task EnqueueAsync(TransferJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.IsTerminal)
            throw new InvalidOperationException("A finished job cannot be queued.");
        _jobs.Add(job);
        await SaveAsync(cancellationToken);
    }

    /// <summary>Drops a queued (not running) job without recording it in history, and saves.</summary>
    public async Task<bool> RemoveAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var removed = _jobs.RemoveAll(job => job.Id == jobId) > 0;
        if (removed) await SaveAsync(cancellationToken);
        return removed;
    }

    /// <summary>
    /// Moves a terminal job out of the queue into history (most recent first, capped) and saves.
    /// Returns the record created from the job.
    /// </summary>
    public async Task<TransferHistoryRecord> RecordCompletionAsync(
        TransferJob job, CancellationToken cancellationToken = default)
    {
        var record = TransferHistoryRecord.FromJob(job);
        _jobs.RemoveAll(existing => existing.Id == job.Id);
        _history.Insert(0, record);
        while (_history.Count > MaximumHistoryEntries)
            _history.RemoveAt(_history.Count - 1);
        await SaveAsync(cancellationToken);
        return record;
    }

    private static void NormalizeAfterRestart(TransferJob job)
    {
        // A job saved mid-run must not pretend it is still running: park it as
        // Paused and hand its interrupted item back to Pending so a deliberate
        // resume re-copies that item from scratch (the staged partial is gone).
        if (job.State == TransferJobState.Running)
            job.State = TransferJobState.Paused;
        foreach (var item in job.Items)
        {
            if (item.State == TransferItemState.InProgress)
            {
                item.State = TransferItemState.Pending;
                item.BytesCopied = 0;
            }
        }
    }

    private static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null; // damaged store data reads as empty, never as a crash
        }
    }

    private static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
            temporaryPath = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // Best-effort like the other Meridian stores: the in-memory queue stays
            // correct for this session even when the disk write fails.
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
