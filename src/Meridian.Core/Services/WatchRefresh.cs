namespace Meridian.Services;

/// <summary>
/// Decides whether a batch of WatchKit events can change what a pane shows
/// for its current folder. WatchKit watches recursively, but a standard pane
/// listing shows only direct children — so a change two levels down cannot
/// alter it, and refreshing for one would be pure churn. The Miller-columns
/// layout does show descendant folders, so it opts into descendant matches.
/// Pure path logic (no filesystem access), so the boundary cases are
/// unit-tested.
///
/// Comparisons are Windows-flavoured: case-insensitive, and tolerant of the
/// separator a path arrives with, because the kit echoes paths in the form
/// its snapshot walk produced and the pane's path comes from Windows APIs.
/// </summary>
public static class WatchRefresh
{
    /// <summary>True when at least one event touches the pane's own folder,
    /// a direct child of it, or — only with
    /// <paramref name="includeDescendants"/> — anything beneath it. A rename
    /// counts on both sides: the name that left and the name that arrived.</summary>
    public static bool AffectsListing(string? paneFolder, IEnumerable<WatchEvent> events, bool includeDescendants)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (string.IsNullOrWhiteSpace(paneFolder)) return false;
        var folder = Normalize(paneFolder);
        if (folder.Length == 0) return false;

        foreach (var change in events)
        {
            if (Touches(change.Path) || (change.OldPath is not null && Touches(change.OldPath)))
                return true;
        }

        return false;

        bool Touches(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            var path = Normalize(candidate);
            if (path.Length == 0) return false;
            if (string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)) return true;
            var parent = ParentOf(path);
            if (parent is not null && string.Equals(parent, folder, StringComparison.OrdinalIgnoreCase)) return true;
            return includeDescendants
                && path.Length > folder.Length
                && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
                && path[folder.Length] == '\\';
        }
    }

    /// <summary>One comparable form for a path: forward slashes become
    /// backslashes and trailing separators are dropped, so "C:\Data",
    /// "C:/Data/" and "C:\Data\" all compare equal. A drive root keeps its
    /// identity as "C:" (from "C:\"); a UNC share root likewise reduces to
    /// "\\server\share".</summary>
    public static string Normalize(string path)
    {
        var normalized = path.Trim().Replace('/', '\\');
        while (normalized.Length > 1 && normalized[^1] == '\\' && !IsBareRoot(normalized))
            normalized = normalized[..^1];
        return normalized;
    }

    /// <summary>True for strings that ARE a root with the separator kept:
    /// "C:\" (length 3) — trimming it would produce "C:", which is still the
    /// root's normalized form, so the loop in <see cref="Normalize"/> stops
    /// one step earlier via the length check alone; this guard exists for
    /// the UNC prefix "\\", which must never be trimmed away.</summary>
    private static bool IsBareRoot(string path) => path == "\\" || path == "\\\\";

    /// <summary>The parent folder of a normalized path, or null when the
    /// path is a root (a drive root "C:" or a UNC share "\\server\share"
    /// has no parent inside the watched world).</summary>
    private static string? ParentOf(string normalizedPath)
    {
        if (IsShareRoot(normalizedPath)) return null;
        var cut = normalizedPath.LastIndexOf('\\');
        if (cut < 0) return null;
        if (cut == 0) return null; // "\name" — parent is the root, nameless here
        var parent = normalizedPath[..cut];
        // "C:\name" cuts to "C:" — that IS the drive root, keep it.
        // "\\server\share\name" cuts to "\\server\share" — the share root, keep it.
        return parent.Length == 0 ? null : parent;
    }

    /// <summary>True for a UNC share root in normalized form
    /// ("\\server\share": the leading double separator plus exactly one
    /// more separator between server and share).</summary>
    private static bool IsShareRoot(string normalizedPath)
    {
        if (!normalizedPath.StartsWith("\\\\", StringComparison.Ordinal)) return false;
        var rest = normalizedPath.AsSpan(2);
        var cut = rest.IndexOf('\\');
        return cut > 0 && rest[(cut + 1)..].IndexOf('\\') < 0 && cut + 1 < rest.Length;
    }
}
