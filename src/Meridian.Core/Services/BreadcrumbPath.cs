namespace Meridian.Services;

/// <summary>One level of a pane's breadcrumb path bar: the label its button
/// shows and the full path clicking it navigates to.</summary>
public sealed record BreadcrumbSegment(string Label, string Path);

/// <summary>
/// Turns a pane's current path into the ordered segments its breadcrumb bar
/// shows: the root first (a drive root such as <c>C:\</c>, or a UNC share
/// root such as <c>\\server\share\</c>), then one segment per folder down
/// to the current folder itself. Kept in Core so the root, UNC and sentinel
/// rules are testable without the window. The Home page is not a filesystem
/// path: it yields a single "Home" segment whose path is the sentinel
/// itself, so clicking it navigates Home like any other segment. Paths that
/// are not fully qualified yield no segments — a pane's path is always
/// fully qualified or the sentinel, and anything else is a bug the bar
/// should not paper over by resolving against the process folder.
/// </summary>
public static class BreadcrumbPath
{
    /// <summary>The label of the Home segment.</summary>
    public const string HomeLabel = "Home";

    public static IReadOnlyList<BreadcrumbSegment> SegmentsFor(string? path, string homeSentinel)
    {
        if (string.IsNullOrWhiteSpace(path)) return [];
        if (!string.IsNullOrEmpty(homeSentinel) &&
            string.Equals(path.Trim(), homeSentinel, StringComparison.OrdinalIgnoreCase))
            return [new BreadcrumbSegment(HomeLabel, homeSentinel)];
        if (!Path.IsPathFullyQualified(path)) return [];

        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return [];
        }

        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root)) return [];

        // The root's label drops the trailing separator ("C:" for a drive,
        // "\\server\share" for a share); its navigation path keeps the root
        // exactly as the platform spells it.
        var rootLabel = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (rootLabel.Length == 0) rootLabel = root;
        var segments = new List<BreadcrumbSegment> { new(rootLabel, root) };

        var remainder = full[root.Length..].Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (remainder.Length == 0) return segments;
        var current = root;
        foreach (var part in remainder.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            segments.Add(new BreadcrumbSegment(part, current));
        }
        return segments;
    }
}
