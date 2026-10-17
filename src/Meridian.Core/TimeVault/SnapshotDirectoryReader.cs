namespace Meridian.Core.TimeVault;

/// <summary>Metadata from one immediate directory listing inside a shadow copy.</summary>
public sealed record SnapshotEntry(string Name, string FullPath, bool IsDirectory, long? Length, DateTimeOffset? ModifiedAt);

/// <summary>
/// Read-only, nonrecursive snapshot directory listing. Snapshot contents are untrusted.
/// Never launch or execute items from these paths.
/// </summary>
public sealed class SnapshotDirectoryReader
{
    public Task<IReadOnlyList<SnapshotEntry>> ListAsync(
        ShadowCopy snapshot, string liveFolder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(liveFolder);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();

        return Task.Run<IReadOnlyList<SnapshotEntry>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Only inspect ordinary drive-letter paths. Reject aliases and traversal
            // before normalization so the caller cannot obscure the requested folder.
            if (liveFolder.Length < 3 || !char.IsLetter(liveFolder[0]) ||
                liveFolder[1] != ':' || liveFolder[2] != '\\')
                throw new ArgumentException("Folder must be an absolute local drive path.", nameof(liveFolder));
            if (liveFolder[3..].Contains("\\\\", StringComparison.Ordinal) ||
                liveFolder[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries).Any(part =>
                part is "." or ".." || part.Contains(':') || part.EndsWith(' ') || part.EndsWith('.')))
                throw new ArgumentException("Folder contains an unsafe path component.", nameof(liveFolder));
            var root = Path.GetPathRoot(Path.GetFullPath(liveFolder))
                ?? throw new ArgumentException("Invalid local folder.", nameof(liveFolder));
            var snapshotFolder = ShadowPath.ResolveForDrive(snapshot, liveFolder, root);
            SnapshotPathGuard.RejectReparsePoints(snapshot, snapshotFolder);
            var items = new List<SnapshotEntry>();
            // Enumeration is deliberately nonrecursive and capped to bound memory use.
            const int maxEntries = 10000;
            foreach (var path in Directory.EnumerateFileSystemEntries(snapshotFolder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (items.Count >= maxEntries)
                    throw new IOException($"Snapshot folder contains more than {maxEntries:N0} entries. Refine the location.");
                var attributes = File.GetAttributes(path);
                // Reparse points are not followed or exposed for navigation.
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                long? length = null;
                DateTimeOffset? modified = null;
                try
                {
                    if (isDirectory) modified = new DirectoryInfo(path).LastWriteTimeUtc;
                    else
                    {
                        var file = new FileInfo(path);
                        length = file.Length;
                        modified = file.LastWriteTimeUtc;
                    }
                }
                catch (IOException) { /* file may have disappeared */ }
                catch (UnauthorizedAccessException) { /* metadata may be unavailable */ }
                items.Add(new SnapshotEntry(Path.GetFileName(path), path, isDirectory, length, modified));
            }
            return items.AsReadOnly();
        }, cancellationToken);
    }
}
