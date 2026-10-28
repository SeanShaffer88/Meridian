using Meridian.Core.Transfers;

namespace Meridian.Services;

/// <summary>One throttled progress snapshot for the UI: the executor's own
/// numbers plus the speed and time-left the coordinator derives from the
/// deltas between snapshots.</summary>
internal sealed record TransferProgressUpdate(
    TransferJob Job, TransferProgress Progress, double? BytesPerSecond, TimeSpan? EstimatedRemaining);

/// <summary>
/// The App-side owner of the durable transfer queue. It runs queued jobs one
/// at a time through a single <see cref="TransferExecutor"/>, persists the
/// store on every transition it can observe, appends finished jobs to
/// History, and never auto-runs anything after a restart: a job only runs
/// when it was enqueued during this session or the user explicitly starts,
/// resumes, or resolves it. Jobs the store reloads as Paused (they were
/// Running when the app closed) simply wait in the strip.
///
/// Events can be raised on background threads; subscribers must marshal to
/// the UI thread themselves.
/// </summary>
internal sealed class TransferCoordinator
{
    private readonly TransferQueueStore _store;
    private readonly ITransferFileOperations _files;
    private readonly TransferExecutor _executor;
    private readonly object _sync = new();
    private readonly Queue<Guid> _startQueue = new();
    private readonly HashSet<Guid> _startRequested = new();
    private readonly Dictionary<Guid, CollisionDecisionKind> _applyToAll = new();
    private readonly Dictionary<Guid, ProgressMeter> _meters = new();
    private Task? _pumpTask;

    public TransferCoordinator(string dataFolder)
    {
        _store = new TransferQueueStore(dataFolder);
        _files = new SystemTransferFileOperations();
        _executor = new TransferExecutor(_files);
    }

    /// <summary>Raised whenever the queue, a job's state, or History changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Throttled progress for the job currently running (at most ~4 per second).</summary>
    public event EventHandler<TransferProgressUpdate>? ProgressChanged;

    /// <summary>Raised once per job that reaches a terminal state, after it is recorded in History.</summary>
    public event EventHandler<TransferJob>? JobSettled;

    public IReadOnlyList<TransferJob> Jobs => _store.Jobs;
    public IReadOnlyList<TransferHistoryRecord> History => _store.History;

    /// <summary>The job whose ExecuteAsync is currently in flight (including while it sits paused), or null.</summary>
    public TransferJob? ActiveJob { get; private set; }

    public async Task LoadAsync()
    {
        await _store.LoadAsync();
        RaiseChanged();
    }

    public async Task<TransferJob> EnqueueAsync(TransferJob job)
    {
        await _store.EnqueueAsync(job);
        RaiseChanged();
        RequestStart(job.Id);
        return job;
    }

    /// <summary>Plans an undo for a history record without running anything.</summary>
    public UndoPlan GetUndoPlan(TransferHistoryRecord record) => UndoPlanner.Plan(record, _files);

    /// <summary>Enqueues the inverse job for a history record when a plan is
    /// available at click time; returns null when no undo is offered.</summary>
    public async Task<TransferJob?> EnqueueUndoAsync(TransferHistoryRecord record)
    {
        var plan = UndoPlanner.Plan(record, _files);
        if (!plan.IsAvailable || plan.InverseJob is null) return null;
        return await EnqueueAsync(plan.InverseJob);
    }

    public bool HasPendingStartRequest(Guid jobId)
    {
        lock (_sync) return _startRequested.Contains(jobId);
    }

    public void RequestStart(Guid jobId)
    {
        lock (_sync)
        {
            if (_startRequested.Add(jobId)) _startQueue.Enqueue(jobId);
            _pumpTask ??= Task.Run(PumpAsync);
        }
    }

    /// <summary>Pauses a job. A live job pauses cooperatively at the
    /// executor's next checkpoint (its ExecuteAsync stays parked); a job
    /// waiting on conflicts is simply parked as Paused.</summary>
    public async Task PauseJobAsync(TransferJob job)
    {
        if (ReferenceEquals(ActiveJob, job))
        {
            _executor.Pause();
            for (var i = 0; i < 40 && job.State == TransferJobState.Running; i++)
                await Task.Delay(50);
            await _store.SaveAsync();
            RaiseChanged();
            return;
        }
        if (job.State == TransferJobState.WaitingOnConflict)
        {
            job.TransitionTo(TransferJobState.Paused);
            await _store.SaveAsync();
            RaiseChanged();
        }
    }

    public void ResumeJob(TransferJob job)
    {
        if (ReferenceEquals(ActiveJob, job))
        {
            _executor.Resume();
            _ = PersistAfterResumeAsync();
            return;
        }
        if (job.State is TransferJobState.Queued or TransferJobState.Paused or TransferJobState.WaitingOnConflict)
            RequestStart(job.Id);
    }

    /// <summary>Cancels a job. A live job is cancelled through the executor
    /// (finished files stay; the half-copied staging file is removed); a job
    /// that is not running is settled immediately.</summary>
    public async Task CancelJobAsync(TransferJob job)
    {
        if (ReferenceEquals(ActiveJob, job))
        {
            _executor.Cancel();
            return;
        }
        if (job.IsTerminal) return;
        foreach (var item in job.Items)
        {
            if (item.State is TransferItemState.Pending or TransferItemState.InProgress
                or TransferItemState.WaitingOnConflict)
                item.State = TransferItemState.Cancelled;
        }
        job.TransitionTo(TransferJobState.Cancelled);
        await _store.RecordCompletionAsync(job);
        RaiseChanged();
        JobSettled?.Invoke(this, job);
    }

    /// <summary>
    /// Applies the user's choice to one conflicted item (and, when
    /// <paramref name="applyToAll"/> is set, to every other parked item of the
    /// job — and to any that park later). When no conflicts remain, the job
    /// continues: a live paused run is released, otherwise the job is queued
    /// to run again.
    /// </summary>
    public async Task ResolveConflictAsync(
        TransferJob job, TransferItem item, CollisionDecisionKind action, bool applyToAll)
    {
        if (applyToAll)
        {
            lock (_sync) _applyToAll[job.Id] = action;
        }
        ResolveOne(item, action);
        if (applyToAll)
        {
            foreach (var other in job.Items.Where(candidate =>
                candidate.State == TransferItemState.WaitingOnConflict && !ReferenceEquals(candidate, item)))
                ResolveOne(other, action);
        }
        await _store.SaveAsync();
        RaiseChanged();
        if (job.Items.Any(candidate => candidate.State == TransferItemState.WaitingOnConflict)) return;
        if (ReferenceEquals(ActiveJob, job))
        {
            if (job.State == TransferJobState.Paused) _executor.Resume();
        }
        else if (job.State is TransferJobState.WaitingOnConflict or TransferJobState.Paused)
        {
            RequestStart(job.Id);
        }
    }

    private void ResolveOne(TransferItem item, CollisionDecisionKind action)
    {
        var decision = action switch
        {
            CollisionDecisionKind.Replace => new CollisionDecision(
                CollisionDecisionKind.Replace, null, "You chose to replace the existing file."),
            CollisionDecisionKind.Skip => new CollisionDecision(
                CollisionDecisionKind.Skip, null, "You chose to skip this file."),
            CollisionDecisionKind.Rename => new CollisionDecision(
                CollisionDecisionKind.Rename,
                CollisionResolver.FirstFreeName(item.DestinationPath!,
                    candidate => _files.FileExists(candidate) || _files.DirectoryExists(candidate)),
                "You chose to keep both files under a numbered name."),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Not a conflict choice."),
        };
        item.ResolveConflict(decision);
    }

    private async Task PersistAfterResumeAsync()
    {
        await Task.Delay(80);
        await _store.SaveAsync();
        RaiseChanged();
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            TransferJob? next = null;
            lock (_sync)
            {
                while (_startQueue.Count > 0)
                {
                    var id = _startQueue.Dequeue();
                    _startRequested.Remove(id);
                    next = _store.Jobs.FirstOrDefault(job => job.Id == id && !job.IsTerminal);
                    if (next is not null) break;
                }
                if (next is null)
                {
                    _pumpTask = null;
                    return;
                }
            }
            await RunJobAsync(next);
        }
    }

    private async Task RunJobAsync(TransferJob job)
    {
        ActiveJob = job;
        RaiseChanged();
        try
        {
            await _executor.ExecuteAsync(job, new ProgressSink(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or NotSupportedException or System.Security.SecurityException)
        {
            await FailJobAsync(job, ex);
        }
        finally
        {
            if (ReferenceEquals(ActiveJob, job)) ActiveJob = null;
            lock (_sync) _meters.Remove(job.Id);
        }

        if (job.State == TransferJobState.WaitingOnConflict)
        {
            // A choice the user marked "apply to all" earlier may already
            // answer everything that just parked.
            if (await ApplyAutoChoicesAsync(job)
                && job.Items.All(item => item.State != TransferItemState.WaitingOnConflict))
            {
                RaiseChanged();
                RequestStart(job.Id);
                return;
            }
            await _store.SaveAsync();
            RaiseChanged();
            return;
        }

        if (job.IsTerminal)
        {
            lock (_sync) _applyToAll.Remove(job.Id);
            await _store.RecordCompletionAsync(job);
            RaiseChanged();
            JobSettled?.Invoke(this, job);
            return;
        }

        await _store.SaveAsync();
        RaiseChanged();
    }

    /// <summary>Resolves every parked item of the job with the recorded
    /// apply-to-all choice, when one exists. Returns true when it did.</summary>
    private async Task<bool> ApplyAutoChoicesAsync(TransferJob job)
    {
        CollisionDecisionKind action;
        lock (_sync)
        {
            if (!_applyToAll.TryGetValue(job.Id, out action)) return false;
        }
        foreach (var item in job.Items.Where(item => item.State == TransferItemState.WaitingOnConflict))
            ResolveOne(item, action);
        await _store.SaveAsync();
        return true;
    }

    /// <summary>Turns an executor-level refusal (for example a destination
    /// folder that vanished before the job started) into a recorded Failed
    /// job instead of leaving the job stuck in the queue.</summary>
    private async Task FailJobAsync(TransferJob job, Exception ex)
    {
        if (job.IsTerminal) return;
        if (job.State == TransferJobState.Queued)
        {
            job.Items.Add(new TransferItem
            {
                SourcePath = job.Sources.FirstOrDefault() ?? string.Empty,
                DestinationPath = job.Kind == TransferJobKind.Delete ? null : job.DestinationFolder,
                State = TransferItemState.Failed,
                Error = ex.Message,
            });
            job.TransitionTo(TransferJobState.Running);
        }
        if (job.State is TransferJobState.Paused or TransferJobState.WaitingOnConflict)
            job.TransitionTo(TransferJobState.Running);
        if (job.State == TransferJobState.Running)
            job.TransitionTo(TransferJobState.Failed);
        await _store.SaveAsync();
    }

    private void OnProgress(TransferProgress progress)
    {
        TransferProgressUpdate? update = null;
        lock (_sync)
        {
            if (!_meters.TryGetValue(progress.JobId, out var meter))
            {
                meter = new ProgressMeter();
                _meters[progress.JobId] = meter;
                // First report of a run: the job's items are expanded and its
                // state is Running — persist that transition now.
                _ = _store.SaveAsync();
            }
            var now = DateTimeOffset.UtcNow;
            if (meter.LastReportUtc != default && now - meter.LastReportUtc < TimeSpan.FromMilliseconds(250))
                return;
            if (meter.AnchorUtc == default)
            {
                meter.AnchorUtc = now;
                meter.AnchorBytes = progress.BytesCopied;
            }
            else
            {
                var elapsed = (now - meter.AnchorUtc).TotalSeconds;
                if (elapsed >= 0.4 && progress.BytesCopied >= meter.AnchorBytes)
                {
                    var instant = (progress.BytesCopied - meter.AnchorBytes) / elapsed;
                    meter.Speed = meter.Speed is { } previous ? previous * 0.55 + instant * 0.45 : instant;
                    meter.AnchorUtc = now;
                    meter.AnchorBytes = progress.BytesCopied;
                }
            }
            meter.LastReportUtc = now;
            TimeSpan? remaining = null;
            if (meter.Speed is > 0 && progress.TotalBytes > progress.BytesCopied)
                remaining = TimeSpan.FromSeconds((progress.TotalBytes - progress.BytesCopied) / meter.Speed.Value);
            var job = _store.Jobs.FirstOrDefault(candidate => candidate.Id == progress.JobId);
            if (job is not null)
                update = new TransferProgressUpdate(job, progress, meter.Speed, remaining);
        }
        if (update is not null)
            ProgressChanged?.Invoke(this, update);
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private sealed class ProgressMeter
    {
        public DateTimeOffset AnchorUtc;
        public long AnchorBytes;
        public DateTimeOffset LastReportUtc;
        public double? Speed;
    }

    private sealed class ProgressSink(TransferCoordinator owner) : IProgress<TransferProgress>
    {
        public void Report(TransferProgress value) => owner.OnProgress(value);
    }
}
