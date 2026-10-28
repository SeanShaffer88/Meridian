namespace Meridian.Core.Transfers;

/// <summary>The kind of work a <see cref="TransferJob"/> performs.</summary>
public enum TransferJobKind
{
    Copy,
    Move,

    /// <summary>
    /// Deletes the items named by the job. The transfer engine's deletes are
    /// permanent (they do not go to the Recycle Bin), and a finished Delete job
    /// can never be undone from Meridian's records — see <see cref="UndoPlanner"/>.
    /// </summary>
    Delete,
}

/// <summary>Lifecycle of a <see cref="TransferJob"/>. Terminal states are Completed,
/// CompletedWithErrors, Cancelled and Failed; no transition leaves a terminal state.</summary>
public enum TransferJobState
{
    Queued,
    Running,
    Paused,
    WaitingOnConflict,
    Completed,
    CompletedWithErrors,
    Cancelled,
    Failed,
}

/// <summary>Lifecycle of one <see cref="TransferItem"/> inside a job.</summary>
public enum TransferItemState
{
    Pending,
    InProgress,
    WaitingOnConflict,
    Completed,
    Skipped,
    Failed,
    Cancelled,
}

/// <summary>The facts about a file that collision decisions and undo checks rely on.</summary>
public sealed record FileFacts(long Size, DateTimeOffset LastWriteUtc);

/// <summary>
/// One file or folder a job will act on. Jobs are usually created with sources and a
/// destination folder and the executor expands folders into items; undo plans carry
/// pre-built items with explicit destination paths instead.
/// </summary>
public sealed class TransferItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The file or folder acted on. For a Delete job this is the item removed.</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Where the item lands. Null for Delete jobs.</summary>
    public string? DestinationPath { get; set; }

    public bool IsDirectory { get; set; }

    /// <summary>Source size in bytes captured when the item was planned (0 for folders).</summary>
    public long Size { get; set; }

    public DateTimeOffset? SourceLastWriteUtc { get; set; }

    /// <summary>Bytes written so far for the current copy attempt; reset when an item restarts.</summary>
    public long BytesCopied { get; set; }

    public TransferItemState State { get; set; } = TransferItemState.Pending;

    /// <summary>Plain-words reason when the item failed, or null.</summary>
    public string? Error { get; set; }

    /// <summary>Size of the destination file actually produced, recorded after publication.</summary>
    public long? DestinationSize { get; set; }

    /// <summary>Last-write time of the destination file actually produced, recorded after publication.</summary>
    public DateTimeOffset? DestinationLastWriteUtc { get; set; }

    /// <summary>
    /// The user's answer to an Ask conflict for this item, applied on the next executor
    /// pass instead of asking again. Only Replace, Skip and Rename are meaningful here.
    /// </summary>
    public CollisionDecisionKind? CollisionOverride { get; set; }

    /// <summary>
    /// Records the user's decision for a conflicted item and returns it to Pending so the
    /// executor picks it up on the next pass. A Rename decision must carry the new
    /// destination path (see <see cref="CollisionResolver.FirstFreeName"/>).
    /// </summary>
    public void ResolveConflict(CollisionDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (State != TransferItemState.WaitingOnConflict)
            throw new InvalidOperationException("This item is not waiting on a conflict decision.");
        switch (decision.Kind)
        {
            case CollisionDecisionKind.Skip:
                CollisionOverride = CollisionDecisionKind.Skip;
                State = TransferItemState.Skipped;
                Error = null;
                break;
            case CollisionDecisionKind.Replace:
                CollisionOverride = CollisionDecisionKind.Replace;
                State = TransferItemState.Pending;
                Error = null;
                break;
            case CollisionDecisionKind.Rename:
                if (string.IsNullOrWhiteSpace(decision.NewDestinationPath))
                    throw new ArgumentException("A rename decision must name the new destination path.", nameof(decision));
                DestinationPath = decision.NewDestinationPath;
                CollisionOverride = CollisionDecisionKind.Rename;
                State = TransferItemState.Pending;
                Error = null;
                break;
            default:
                throw new ArgumentException(
                    $"A conflict can only be resolved with Replace, Skip or Rename, not {decision.Kind}.", nameof(decision));
        }
    }
}

/// <summary>
/// One durable unit of transfer work: a set of sources copied, moved or deleted into a
/// destination folder under one collision policy. The state machine is explicit —
/// illegal transitions throw instead of silently corrupting the queue's view of the job.
/// </summary>
public sealed class TransferJob
{
    private static readonly IReadOnlyDictionary<TransferJobState, TransferJobState[]> LegalTransitions =
        new Dictionary<TransferJobState, TransferJobState[]>
        {
            [TransferJobState.Queued] = [TransferJobState.Running, TransferJobState.Cancelled],
            [TransferJobState.Running] =
            [
                TransferJobState.Paused, TransferJobState.WaitingOnConflict, TransferJobState.Completed,
                TransferJobState.CompletedWithErrors, TransferJobState.Cancelled, TransferJobState.Failed,
            ],
            [TransferJobState.Paused] = [TransferJobState.Running, TransferJobState.Cancelled],
            [TransferJobState.WaitingOnConflict] =
                [TransferJobState.Running, TransferJobState.Paused, TransferJobState.Cancelled],
            [TransferJobState.Completed] = [],
            [TransferJobState.CompletedWithErrors] = [],
            [TransferJobState.Cancelled] = [],
            [TransferJobState.Failed] = [],
        };

    public Guid Id { get; set; } = Guid.NewGuid();
    public TransferJobKind Kind { get; set; }

    /// <summary>The top-level files and folders the job was asked to act on.</summary>
    public List<string> Sources { get; set; } = [];

    /// <summary>The folder items land in. Empty for Delete jobs and for undo jobs whose items carry their own destinations.</summary>
    public string DestinationFolder { get; set; } = string.Empty;

    public TransferJobState State { get; set; } = TransferJobState.Queued;
    public CollisionPolicy CollisionPolicy { get; set; } = CollisionPolicy.Ask;

    /// <summary>When true, every copied file's staged bytes are SHA-256 compared with the source before publication.</summary>
    public bool VerifyAfterCopy { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }

    /// <summary>The expanded work list. Empty until the executor expands the sources.</summary>
    public List<TransferItem> Items { get; set; } = [];

    /// <summary>Total bytes across the job's file items (folders contribute structure, not bytes).</summary>
    public long TotalBytes => Items.Where(item => !item.IsDirectory).Sum(item => item.Size);

    /// <summary>Bytes copied so far across all items, including a partially copied current item.</summary>
    public long BytesCopied => Items.Sum(item => item.BytesCopied);

    public int TotalItems => Items.Count;
    public int CompletedItems => Items.Count(item => item.State == TransferItemState.Completed);

    public bool IsTerminal => State is TransferJobState.Completed or TransferJobState.CompletedWithErrors
        or TransferJobState.Cancelled or TransferJobState.Failed;

    public static TransferJob Create(
        TransferJobKind kind,
        IEnumerable<string> sources,
        string destinationFolder,
        CollisionPolicy collisionPolicy = CollisionPolicy.Ask,
        bool verifyAfterCopy = false)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return new TransferJob
        {
            Kind = kind,
            Sources = [.. sources],
            DestinationFolder = destinationFolder ?? string.Empty,
            CollisionPolicy = collisionPolicy,
            VerifyAfterCopy = verifyAfterCopy,
        };
    }

    public static bool CanTransition(TransferJobState from, TransferJobState to) =>
        LegalTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    /// <summary>
    /// Moves the job to a new state, stamping StartedUtc on the first run and FinishedUtc on
    /// any terminal state. Illegal transitions throw <see cref="InvalidOperationException"/>.
    /// </summary>
    public void TransitionTo(TransferJobState next)
    {
        if (next == State) return;
        if (!CanTransition(State, next))
            throw new InvalidOperationException($"A transfer job cannot move from {State} to {next}.");
        State = next;
        if (next == TransferJobState.Running && StartedUtc is null)
            StartedUtc = DateTimeOffset.UtcNow;
        if (IsTerminal && FinishedUtc is null)
            FinishedUtc = DateTimeOffset.UtcNow;
    }
}
