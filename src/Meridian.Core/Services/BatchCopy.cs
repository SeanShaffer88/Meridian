namespace Meridian.Services;

/// <summary>
/// Explicit, sequential batch-copy execution. Never overwrites and never silently skips
/// a blocked preflight. Successful earlier copies are NOT rolled back if a later copy fails.
/// Call only after the user has confirmed the operation in the UI.
/// </summary>
public static class BatchCopy
{
    public sealed record Result(IReadOnlyList<string> CompletedTargets, string? FailedSource,
        string? Failure, bool Cancelled)
    {
        public bool Succeeded => FailedSource is null && !Cancelled;
    }

    public static Result Execute(IEnumerable<string> sourcePaths, string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        // Materialize once so the same exact set is previewed and executed.
        var sources = sourcePaths.ToArray();
        var plan = OperationPreview.PreviewCopy(sources, destinationDirectory, cancellationToken);
        if (plan.HasBlockers)
            throw new InvalidOperationException(
                "Batch copy refused: preflight found one or more blocked items. No files were copied.");

        var completed = new List<string>();
        foreach (var item in plan.Items)
        {
            if (cancellationToken.IsCancellationRequested)
                return new Result(completed.AsReadOnly(), item.Source, null, Cancelled: true);

            try
            {
                // FileOperations rechecks paths and refuses collisions at execution time.
                var target = FileOperations.CopyItem(item.Source, plan.DestinationDirectory, cancellationToken);
                completed.Add(target);
            }
            catch (OperationCanceledException)
            {
                return new Result(completed.AsReadOnly(), item.Source, null, Cancelled: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException or System.Security.SecurityException)
            {
                return new Result(completed.AsReadOnly(), item.Source, ex.Message, Cancelled: false);
            }
        }
        return new Result(completed.AsReadOnly(), null, null, Cancelled: false);
    }
}
