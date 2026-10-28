namespace Meridian.Services;

/// <summary>
/// A read-only task context across directories. Captured paths are references, not files owned by Meridian.
/// No member of this type moves, copies, deletes or creates filesystem objects.
/// </summary>
public sealed class ContextCapsule
{
    public const int MaximumReferences = 1000;
    public const int MaximumNameLength = 120;

    private readonly string[] _paths;

    private ContextCapsule(string name, string[] paths)
    {
        Name = name;
        _paths = paths;
        Paths = Array.AsReadOnly(_paths);
    }

    public string Name { get; }
    public IReadOnlyList<string> Paths { get; }

    /// <summary>
    /// Capture a bounded snapshot. Missing and offline paths are deliberately retained.
    /// Do not trim the input enumeration silently: callers must know if their task was too large.
    /// </summary>
    public static ContextCapsule Capture(string name, IEnumerable<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > MaximumNameLength || name.Any(char.IsControl))
            throw new ArgumentException("Capsule name is too long or contains control characters.", nameof(name));
        ArgumentNullException.ThrowIfNull(paths);

        var captured = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var consumed = 0;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++consumed > MaximumReferences)
                throw new ArgumentException($"A capsule supports at most {MaximumReferences} input references.", nameof(paths));
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            var fullPath = Path.GetFullPath(path);
            if (seen.Add(fullPath))
                captured.Add(fullPath);
        }

        return new ContextCapsule(name.Trim(), captured.ToArray());
    }

    /// <summary>
    /// Re-check presence without changing or removing offline references.
    /// Results are advisory and cannot reserve filesystem objects for an operation.
    /// </summary>
    public IReadOnlyList<ReferenceState> Inspect(CancellationToken cancellationToken = default)
    {
        var results = new List<ReferenceState>(_paths.Length);
        foreach (var path in _paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Some inaccessible/network locations report false rather than throwing.
                var present = File.Exists(path) || Directory.Exists(path);
                results.Add(new ReferenceState(path, present ? Presence.Present : Presence.Unavailable));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                results.Add(new ReferenceState(path, Presence.Unavailable));
            }
        }
        return results.AsReadOnly();
    }

    public enum Presence { Present, Unavailable }
    public sealed record ReferenceState(string Path, Presence State);
}
