namespace Meridian.Core.Consequences;

/// <summary>Records observed outcomes; does not imply rollback or universal undo.</summary>
public enum OperationOutcome { Completed, Failed, Cancelled, Skipped }

public sealed record OperationReceiptItem(
    PlannedAction Action,
    string Source,
    string? Destination,
    OperationOutcome Outcome,
    string? Detail);

public sealed record OperationReceipt(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyList<OperationReceiptItem> Items)
{
    public int CompletedCount => Items.Count(item => item.Outcome == OperationOutcome.Completed);
    public int FailedCount => Items.Count(item => item.Outcome == OperationOutcome.Failed);
}
