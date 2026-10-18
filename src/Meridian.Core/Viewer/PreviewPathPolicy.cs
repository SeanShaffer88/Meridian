namespace Meridian.Core.Viewer;

/// <summary>
/// Conservative pre-open policy for preview sources. Does not provide race-free
/// filesystem identity guarantees; callers must not treat it as a security boundary.
/// </summary>
public static class PreviewPathPolicy
{
    public static string ValidateRegularFile(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("Preview requires an absolute path.", nameof(path));

        cancellationToken.ThrowIfCancellationRequested();
        var full = Path.GetFullPath(path);
        var ancestor = Path.GetDirectoryName(full);
        while (!string.IsNullOrEmpty(ancestor))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Preview cannot traverse reparse-point directories.");
            var parent = Path.GetDirectoryName(ancestor);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, ancestor, StringComparison.Ordinal))
                break;
            ancestor = parent;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if ((File.GetAttributes(full) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException("Preview requires a regular file.");
        return full;
    }
}
