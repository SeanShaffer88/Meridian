namespace Meridian.Core.Consequences;

/// <summary>An immutable, advisory description of a proposed file operation.</summary>
public enum PlannedAction { Copy, Move, Rename, Delete }
public enum PlanDisposition { Ready, Conflict, Invalid }

public sealed record PlannedFileAction(
    PlannedAction Action,
    string Source,
    string? Destination,
    PlanDisposition Disposition,
    string Explanation);

public sealed record ConsequencePlan(
    IReadOnlyList<PlannedFileAction> Actions,
    DateTimeOffset CreatedAt)
{
    public int ReadyCount => Actions.Count(a => a.Disposition == PlanDisposition.Ready);
    public int BlockedCount => Actions.Count - ReadyCount;
    public bool CanExecute => Actions.Count > 0 && BlockedCount == 0;
}

/// <summary>
/// Produces advisory copy plans. Does not mutate the filesystem or authorize execution.
/// Callers must revalidate all filesystem state and path security before executing.
/// </summary>
public static class ConsequencePlanner
{
    public static ConsequencePlan PreviewCopies(IEnumerable<string> sources, string destinationFolder)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolder);
        var destination = Path.GetFullPath(destinationFolder);
        var actions = new List<PlannedFileAction>();
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                actions.Add(new(PlannedAction.Copy, source ?? "", null, PlanDisposition.Invalid, "Empty source path."));
                continue;
            }

            string fullSource, target;
            try
            {
                fullSource = Path.GetFullPath(source);
                var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullSource));
                if (string.IsNullOrEmpty(name)) throw new ArgumentException("Source must name a file or folder.");
                target = Path.Combine(destination, name);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                actions.Add(new(PlannedAction.Copy, source, null, PlanDisposition.Invalid, "Invalid path."));
                continue;
            }

            var disposition = PlanDisposition.Ready;
            var explanation = "Candidate only; filesystem state must be rechecked at execution.";
            if (string.Equals(fullSource, target, StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith(Path.TrimEndingDirectorySeparator(fullSource) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                disposition = PlanDisposition.Invalid;
                explanation = "Source equals or contains destination.";
            }
            else if (!reserved.Add(target) || File.Exists(target) || Directory.Exists(target))
            {
                disposition = PlanDisposition.Conflict;
                explanation = "Destination already exists or another planned item uses this name.";
            }

            actions.Add(new(PlannedAction.Copy, fullSource, target, disposition, explanation));
        }

        return new(actions.AsReadOnly(), DateTimeOffset.UtcNow);
    }
}
