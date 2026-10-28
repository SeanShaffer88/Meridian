namespace Meridian.Core.Transfers;

/// <summary>
/// The result of asking for an undo: either an inverse job ready to hand to the executor,
/// or a plain-words reason why no undo is offered. A plan is a proposal, never an action —
/// nothing here touches the filesystem except reading the facts its checks depend on.
/// </summary>
public sealed record UndoPlan(bool IsAvailable, string Reason, TransferJob? InverseJob);

/// <summary>
/// Builds undo plans from history records, extending the RecoveryReadiness honesty rule:
/// a reversal is offered only when every precondition still holds at planning time, and the
/// reason says plainly which check failed when one does. Checks are size + last-write
/// comparisons against the facts recorded at execution time — matching metadata is treated
/// as "unchanged" (it is not a content proof, and the executor re-checks collisions when
/// the inverse job runs).
///
/// Copy → the inverse is a Delete job over exactly the files the copy created (folders the
/// copy created are left in place). Move → the inverse is a Move job returning each file to
/// its original location; an original location that is occupied again gets the next free
/// "name (2)" style name rather than overwriting anything. Delete → never undoable: the
/// transfer engine's deletes are permanent and do not go to the Recycle Bin. Undoing an
/// undo is how redo works — the inverse job's own history record plans back the other way.
/// </summary>
public static class UndoPlanner
{
    public static UndoPlan Plan(TransferHistoryRecord record, ITransferFileOperations files)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(files);

        return record.Kind switch
        {
            TransferJobKind.Copy => PlanCopyUndo(record, files),
            TransferJobKind.Move => PlanMoveUndo(record, files),
            TransferJobKind.Delete => new UndoPlan(false,
                "Deletes cannot be undone from Meridian's records: the transfer engine removes files permanently instead of sending them to the Recycle Bin.",
                null),
            _ => new UndoPlan(false, "This kind of job cannot be undone.", null),
        };
    }

    private static UndoPlan PlanCopyUndo(TransferHistoryRecord record, ITransferFileOperations files)
    {
        var created = CompletedFileItems(record);
        if (created.Count == 0)
            return new UndoPlan(false, "Nothing was copied by this job, so there is nothing to undo.", null);

        foreach (var item in created)
        {
            if (CheckStillUnchanged(item, files) is { } problem)
                return new UndoPlan(false, problem, null);
        }

        var inverse = new TransferJob
        {
            Kind = TransferJobKind.Delete,
            Sources = created.Select(item => item.DestinationPath!).ToList(),
            DestinationFolder = string.Empty,
            CollisionPolicy = CollisionPolicy.Skip,
            Items = created.Select(item => new TransferItem
            {
                SourcePath = item.DestinationPath!,
                DestinationPath = null,
                IsDirectory = false,
                Size = item.DestinationSize ?? item.Size,
            }).ToList(),
        };
        return new UndoPlan(true,
            $"Deletes the {created.Count} file(s) this copy created. Folders the copy created are left in place.",
            inverse);
    }

    private static UndoPlan PlanMoveUndo(TransferHistoryRecord record, ITransferFileOperations files)
    {
        var moved = CompletedFileItems(record);
        if (moved.Count == 0)
            return new UndoPlan(false, "Nothing was moved by this job, so there is nothing to undo.", null);

        foreach (var item in moved)
        {
            if (CheckStillUnchanged(item, files) is { } problem)
                return new UndoPlan(false, problem, null);
        }

        var inverseItems = new List<TransferItem>();

        // Folders first, in the order the job created them, so the files that
        // return have their folders ready.
        var recreatedFolders = new List<string>();
        foreach (var directory in record.Items.Where(
            item => item.IsDirectory && item.FinalState == TransferItemState.Completed && item.DestinationPath is not null))
        {
            recreatedFolders.Add(directory.SourcePath);
            inverseItems.Add(new TransferItem
            {
                SourcePath = directory.DestinationPath!,
                DestinationPath = directory.SourcePath,
                IsDirectory = true,
            });
        }

        foreach (var item in moved)
        {
            var originalFolder = Path.GetDirectoryName(item.SourcePath);
            // A folder the job itself displaced is recreated by the inverse job's
            // folder items above; any other vanished folder makes the undo unsafe
            // to offer, because Meridian would be inventing a location.
            var folderWillExist = !string.IsNullOrEmpty(originalFolder)
                && (files.DirectoryExists(originalFolder)
                    || recreatedFolders.Any(folder => IsSameOrDescendant(folder, originalFolder)));
            if (!folderWillExist)
                return new UndoPlan(false,
                    $"The original folder of '{Path.GetFileName(item.SourcePath)}' no longer exists, so the file cannot be moved back.",
                    null);

            var backDestination = item.SourcePath;
            if (files.FileExists(backDestination) || files.DirectoryExists(backDestination))
            {
                // Renamable counts as available: the file comes back under the
                // next free numbered name instead of replacing anything.
                backDestination = CollisionResolver.FirstFreeName(backDestination,
                    candidate => files.FileExists(candidate) || files.DirectoryExists(candidate));
            }

            inverseItems.Add(new TransferItem
            {
                SourcePath = item.DestinationPath!,
                DestinationPath = backDestination,
                IsDirectory = false,
                Size = item.DestinationSize ?? item.Size,
                SourceLastWriteUtc = item.DestinationLastWriteUtc,
            });
        }

        var inverse = new TransferJob
        {
            Kind = TransferJobKind.Move,
            Sources = inverseItems.Select(item => item.SourcePath).ToList(),
            DestinationFolder = Path.GetDirectoryName(moved[0].SourcePath) ?? string.Empty,
            // The plan already chose free (or numbered) destinations; KeepBoth is
            // the safety net if something lands in the way before the undo runs.
            CollisionPolicy = CollisionPolicy.KeepBoth,
            Items = inverseItems,
        };
        return new UndoPlan(true,
            $"Moves the {moved.Count} file(s) back to where they came from.",
            inverse);
    }

    private static List<TransferHistoryItem> CompletedFileItems(TransferHistoryRecord record) =>
        record.Items
            .Where(item => !item.IsDirectory
                && item.FinalState == TransferItemState.Completed
                && item.DestinationPath is not null)
            .ToList();

    /// <summary>True when candidate is the folder itself or lives underneath it.</summary>
    private static bool IsSameOrDescendant(string folder, string candidate)
    {
        var relative = Path.GetRelativePath(folder, candidate);
        return !Path.IsPathRooted(relative) &&
            (relative == "." || (relative != ".." &&
             !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)));
    }

    /// <summary>Returns a plain-words problem when the created file is gone or changed, else null.</summary>
    private static string? CheckStillUnchanged(TransferHistoryItem item, ITransferFileOperations files)
    {
        var name = Path.GetFileName(item.DestinationPath);
        var current = files.GetFileFacts(item.DestinationPath!);
        if (current is null)
            return files.DirectoryExists(item.DestinationPath!)
                ? $"'{name}' is now a folder, so Meridian cannot undo this safely."
                : $"'{name}' no longer exists at the destination, so there is nothing left to undo.";
        if (item.DestinationSize is null || item.DestinationLastWriteUtc is null)
            return $"Meridian did not record enough detail about '{name}' to check it is unchanged, so undo is not offered.";
        if (current.Size != item.DestinationSize.Value || current.LastWriteUtc != item.DestinationLastWriteUtc.Value)
            return $"'{name}' has changed since the operation, so undo is not offered — reversing could destroy newer work.";
        return null;
    }
}
