namespace Meridian.Services;

/// <summary>
/// The result of measuring a folder: the total size of the files inside it,
/// how many files and subfolders were counted, and how many entries had to
/// be skipped (inaccessible, vanished mid-measurement, or links, which are
/// never followed).
/// </summary>
public sealed record FolderSizeResult(long TotalBytes, long FileCount, long DirectoryCount, int SkippedCount);

/// <summary>
/// Measures a folder's total size by walking it. The walk runs off the
/// caller's thread, honours cancellation between entries, never follows
/// reparse points (junctions and symlinks can form cycles), and skips
/// entries it cannot read instead of failing the whole measurement.
/// Progress snapshots are reported as files are counted; a cancelled
/// measurement throws <see cref="OperationCanceledException"/> and reports
/// nothing, so a caller can simply discard the attempt.
/// </summary>
public static class FolderSizeCalculator
{
    private const int ProgressInterval = 256;

    public static Task<FolderSizeResult> CalculateAsync(
        string path,
        CancellationToken cancellationToken = default,
        Action<FolderSizeResult>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(() => Calculate(path, cancellationToken, progress), cancellationToken);
    }

    private static FolderSizeResult Calculate(string rootPath, CancellationToken cancellationToken, Action<FolderSizeResult>? progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = new DirectoryInfo(Path.GetFullPath(rootPath));
        if (!root.Exists)
            throw new DirectoryNotFoundException($"The folder no longer exists: {root.FullName}");

        long totalBytes = 0;
        long fileCount = 0;
        long directoryCount = 0;
        var skipped = 0;
        long sinceProgress = 0;

        var pending = new Stack<string>();
        pending.Push(root.FullName);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateFileSystemEntries(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                skipped++;
                continue;
            }

            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(child);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    skipped++;
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    // Links are listed, never followed: a junction can point
                    // back into the measured tree and loop forever.
                    skipped++;
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directoryCount++;
                    pending.Push(child);
                    continue;
                }

                try
                {
                    totalBytes += new FileInfo(child).Length;
                    fileCount++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    skipped++;
                    continue;
                }

                if (progress is not null && ++sinceProgress >= ProgressInterval)
                {
                    sinceProgress = 0;
                    progress(new FolderSizeResult(totalBytes, fileCount, directoryCount, skipped));
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new FolderSizeResult(totalBytes, fileCount, directoryCount, skipped);
    }
}
