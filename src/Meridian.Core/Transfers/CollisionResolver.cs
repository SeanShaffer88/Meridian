namespace Meridian.Core.Transfers;

/// <summary>What a job does when a destination name is already taken, chosen per job.</summary>
public enum CollisionPolicy
{
    /// <summary>Park the item and ask the user (Replace / Keep both / Skip).</summary>
    Ask,

    /// <summary>Leave the existing destination alone and skip the item.</summary>
    Skip,

    /// <summary>Copy under the next free "name (2).ext" style name.</summary>
    KeepBoth,

    /// <summary>Overwrite the existing destination.</summary>
    Replace,

    /// <summary>Overwrite only when the source was modified more recently; otherwise skip.</summary>
    ReplaceIfNewer,
}

/// <summary>The outcome of resolving one potential collision.</summary>
public enum CollisionDecisionKind
{
    Proceed,
    Skip,
    Rename,
    Replace,
    NeedsUserChoice,
}

/// <summary>One collision decision. NewDestinationPath is set only for Rename.</summary>
public sealed record CollisionDecision(CollisionDecisionKind Kind, string? NewDestinationPath, string Reason);

/// <summary>
/// Pure name-collision resolution for the transfer engine. Given the facts about a source
/// file and whatever already sits at the destination, it decides — without touching the
/// filesystem itself; the caller supplies a name-existence check for KeepBoth numbering.
/// Folder collisions merge: a folder whose destination folder already exists proceeds,
/// and the files inside are resolved one by one.
/// </summary>
public static class CollisionResolver
{
    public static CollisionDecision Resolve(
        CollisionPolicy policy,
        string destinationPath,
        FileFacts source,
        FileFacts? destination,
        bool isDirectory,
        Func<string, bool> destinationNameExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destinationNameExists);

        if (destination is null)
            return new CollisionDecision(CollisionDecisionKind.Proceed, null, "Nothing is in the way at the destination.");
        if (isDirectory)
            return new CollisionDecision(CollisionDecisionKind.Proceed, null,
                "The destination folder already exists; folders merge and the files inside are checked one by one.");

        return policy switch
        {
            CollisionPolicy.Ask => new CollisionDecision(CollisionDecisionKind.NeedsUserChoice, null,
                "A file with this name already exists. Meridian needs your choice before it can continue."),
            CollisionPolicy.Skip => new CollisionDecision(CollisionDecisionKind.Skip, null,
                "A file with this name already exists, and this job skips items that collide."),
            CollisionPolicy.Replace => new CollisionDecision(CollisionDecisionKind.Replace, null,
                "A file with this name already exists, and this job replaces colliding files."),
            CollisionPolicy.ReplaceIfNewer => source.LastWriteUtc > destination.LastWriteUtc
                ? new CollisionDecision(CollisionDecisionKind.Replace, null,
                    "The source file is newer than the one at the destination, so it replaces it.")
                : new CollisionDecision(CollisionDecisionKind.Skip, null,
                    "The file at the destination is the same age or newer, so the source is skipped."),
            CollisionPolicy.KeepBoth => new CollisionDecision(CollisionDecisionKind.Rename,
                FirstFreeName(destinationPath, destinationNameExists),
                "A file with this name already exists, so the copy keeps both under a numbered name."),
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown collision policy."),
        };
    }

    /// <summary>
    /// The first free "name (n).ext" variant of a destination path, starting at (2) and
    /// incrementing past every numbered name that is also taken. The number is inserted
    /// before the last extension; extensionless names and dotfiles keep their whole name
    /// as the stem ("notes" → "notes (2)", ".gitignore" → ".gitignore (2)").
    /// </summary>
    public static string FirstFreeName(string destinationPath, Func<string, bool> destinationNameExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(destinationNameExists);

        var directory = Path.GetDirectoryName(destinationPath);
        var fileName = Path.GetFileName(destinationPath);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(stem))
        {
            // A leading-dot name such as ".gitignore": the whole name is the stem.
            stem = fileName;
            extension = string.Empty;
        }

        for (var n = 2; ; n++)
        {
            var candidateName = $"{stem} ({n}){extension}";
            var candidate = string.IsNullOrEmpty(directory) ? candidateName : Path.Combine(directory, candidateName);
            if (!destinationNameExists(candidate)) return candidate;
        }
    }
}
