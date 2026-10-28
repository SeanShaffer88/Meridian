namespace Meridian.Services;

/// <summary>
/// The kinds of access one access rule can grant, in plain terms. These are
/// Meridian's own summary flags: the app translates the Windows access rule
/// into them, and the details strip speaks only in these terms.
/// </summary>
[Flags]
public enum AccessRights
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4,
    Delete = 8,
    ChangePermissions = 16,
    TakeOwnership = 32,
    FullControl = 64
}

/// <summary>One access rule, reduced to who it names, whether it allows or denies, and what it covers.</summary>
public sealed record AccessEntry(string Identity, bool IsAllow, bool IsInherited, AccessRights Rights);

/// <summary>
/// A read-only summary of who owns an item and who can open it. When Windows
/// would not show the access list at all, <see cref="Problem"/> explains why
/// in plain words and the entry list is empty. Meridian never changes
/// permissions; this summary exists so the user can see them without
/// opening three system dialogs.
/// </summary>
public sealed record AccessControlSummary(string? Owner, IReadOnlyList<AccessEntry> Entries, string? Problem);

/// <summary>Turns an <see cref="AccessControlSummary"/> into the plain-word lines the details strip shows.</summary>
public static class AccessControlSummaryFormatter
{
    public static string RightsLabel(AccessRights rights)
    {
        if (rights == AccessRights.None) return "No access";
        if ((rights & AccessRights.FullControl) != 0) return "Full control";
        var parts = new List<string>();
        if ((rights & AccessRights.Read) != 0) parts.Add("Read");
        if ((rights & AccessRights.Write) != 0) parts.Add("Write");
        if ((rights & AccessRights.Execute) != 0) parts.Add("Open / run");
        if ((rights & AccessRights.Delete) != 0) parts.Add("Delete");
        if ((rights & AccessRights.ChangePermissions) != 0) parts.Add("Change permissions");
        if ((rights & AccessRights.TakeOwnership) != 0) parts.Add("Take ownership");
        return parts.Count == 0 ? "No access" : string.Join(", ", parts);
    }

    public static IReadOnlyList<string> DescribeLines(AccessControlSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var lines = new List<string>
        {
            "Owner: " + (string.IsNullOrWhiteSpace(summary.Owner) ? "could not be read" : summary.Owner)
        };
        if (summary.Problem is not null)
        {
            lines.Add(summary.Problem);
            return lines;
        }

        if (summary.Entries.Count == 0)
        {
            lines.Add("Windows did not report any access rules for this item.");
            return lines;
        }

        foreach (var entry in summary.Entries)
        {
            var line = $"{entry.Identity} — {(entry.IsAllow ? "Allow" : "Deny")} — {RightsLabel(entry.Rights)}";
            if (entry.IsInherited) line += " (inherited)";
            lines.Add(line);
        }

        return lines;
    }
}
