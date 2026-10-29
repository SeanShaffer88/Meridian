using Meridian.Models;

namespace Meridian.Services;

/// <summary>
/// The pure path logic behind a pane's folder tree. The tree's root is the
/// drive root of the pane's current folder; nodes are folders only, in the
/// same order the pane's own listing shows them (folders first, then name —
/// <see cref="DirectoryReader"/> applies both, so filtering its result to
/// folders keeps the pane's own ordering). No hidden/system filtering is
/// applied because the pane's listing applies none: the tree shows exactly
/// the folders the pane would.
/// </summary>
public static class FolderTree
{
    /// <summary>The root a tree is built from for <paramref name="path"/> — its
    /// drive root (for example <c>C:\</c>) — or null when the path is not
    /// fully qualified. Relative paths are refused rather than silently
    /// resolved against the process folder: tree paths always come from a
    /// pane, and a pane's path is always fully qualified.</summary>
    public static string? RootOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return null;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrEmpty(root) ? null : root;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// The chain of folders from the root down to <paramref name="path"/>
    /// itself, root first: expanding each entry in turn walks the tree to
    /// the folder. A root path yields just the root. An unrooted or invalid
    /// path yields an empty chain.
    /// </summary>
    public static IReadOnlyList<string> AncestorChain(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return [];
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return [];
        }
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root)) return [];

        var chain = new List<string> { root };
        var remainder = full[root.Length..].Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (remainder.Length == 0) return chain;
        var current = root;
        foreach (var segment in remainder.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            chain.Add(current);
        }
        return chain;
    }

    /// <summary>
    /// The folders directly inside <paramref name="path"/>, in the pane
    /// listing's order. Throws the same exceptions as
    /// <see cref="DirectoryReader.Read"/> when the folder cannot be read;
    /// callers showing a tree should treat that as "no children to show".
    /// </summary>
    public static IReadOnlyList<FileEntry> ChildFolders(string path, CancellationToken cancellationToken = default) =>
        DirectoryReader.Read(path, cancellationToken).Where(entry => entry.IsDirectory).ToArray();

    /// <summary>
    /// Whether <paramref name="path"/> holds at least one folder, probed
    /// with a single early-exit enumeration — the tree uses it to decide
    /// which nodes get an expand arrow before they are opened. A folder
    /// that cannot be read reports false rather than throwing.
    /// </summary>
    public static bool HasChildFolders(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            foreach (var _ in Directory.EnumerateDirectories(path)) return true;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
