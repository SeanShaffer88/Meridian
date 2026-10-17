namespace Meridian.Core.StorageReclaimer;

/// <summary>
/// Bounded, read-only inventory of one explicitly selected temporary directory.
/// It never recursively descends, follows links, deletes files or elevates privileges.
/// </summary>
public sealed class TemporaryFileInventory
{
    public async Task<CleanupPlan> ScanAsync(
        string directory, int maxEntries = 10000, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (maxEntries < 1 || maxEntries > 100000)
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var full = Path.GetFullPath(directory);
            var directoryAttributes = File.GetAttributes(full);
            if ((directoryAttributes & FileAttributes.Directory) == 0 ||
                (directoryAttributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Inventory root must be a regular directory, not a link.");
            var findings = new List<CleanupCandidate>();
            var seen = 0;
            foreach (var path in Directory.EnumerateFileSystemEntries(full))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++seen > maxEntries)
                    throw new IOException("Inventory entry limit exceeded.");
                FileAttributes attributes;
                try { attributes = File.GetAttributes(path); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                // Never inspect a link target or recurse into a child directory.
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    continue;
                try
                {
                    var size = new FileInfo(path).Length;
                    findings.Add(new CleanupCandidate(
                        path, size, "Selected directory", CleanupRisk.ReviewRequired,
                        "Read-only estimate. Confirm ownership and file use before any cleanup."));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return new CleanupPlan(findings);
        }, cancellationToken);
    }
}
