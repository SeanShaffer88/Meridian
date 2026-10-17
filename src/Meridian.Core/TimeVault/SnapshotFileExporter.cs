namespace Meridian.Core.TimeVault;

/// <summary>Explicit, no-overwrite export of a single regular file from a VSS snapshot.</summary>
public sealed class SnapshotFileExporter
{
    public async Task<long> ExportAsync(
        ShadowCopy snapshot, string originalFile, string destinationFile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFile);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        cancellationToken.ThrowIfCancellationRequested();

        if (!Path.IsPathFullyQualified(destinationFile) || destinationFile.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Destination must be an absolute local drive path.", nameof(destinationFile));
        // Validate the raw path before normalization, including traversal components.
        if (destinationFile.Length < 3 || !char.IsLetter(destinationFile[0]) ||
            destinationFile[1] != ':' || destinationFile[2] != '\\')
            throw new ArgumentException("Destination must use a local drive letter.", nameof(destinationFile));
        if (destinationFile[3..].Split('\\').Any(part => part is "." or ".."))
            throw new ArgumentException("Destination cannot contain traversal components.", nameof(destinationFile));
        if (destinationFile[3..].Contains("\\\\", StringComparison.Ordinal))
            throw new ArgumentException("Destination cannot contain empty path components.", nameof(destinationFile));
        var destination = Path.GetFullPath(destinationFile);
        // Restrict initial exports to ordinary local drive-letter destinations.
        if (destination.Length < 3 || !char.IsLetter(destination[0]) ||
            destination[1] != ':' || destination[2] != '\\')
            throw new ArgumentException("Destination must be a local drive-letter path.", nameof(destinationFile));
        // Reject NTFS alternate data streams and Windows device-name aliases.
        // A recovery destination must be a normal file, not a stream or device.
        var destinationParts = destinationFile[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (destinationParts.Length == 0 || destinationFile.EndsWith('\\'))
            throw new ArgumentException("Destination must name a file.", nameof(destinationFile));
        foreach (var part in destinationParts)
        {
            if (part.Contains(':') || part.EndsWith(' ') || part.EndsWith('.'))
                throw new ArgumentException("Destination contains an unsafe path component.", nameof(destinationFile));
            var stem = part.Split('.')[0].TrimEnd(' ');
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                    stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                    stem[3] >= '1' && stem[3] <= '9'))
                throw new ArgumentException("Destination uses a reserved device name.", nameof(destinationFile));
        }
        if (!Path.IsPathFullyQualified(originalFile) || originalFile.StartsWith(@"\\", StringComparison.Ordinal) ||
            originalFile.Length < 3 || !char.IsLetter(originalFile[0]) ||
            originalFile[1] != ':' || originalFile[2] != '\\')
            throw new ArgumentException("Source must be an absolute local drive path.", nameof(originalFile));
        if (originalFile[3..].Split('\\').Any(part => part is "." or ".."))
            throw new ArgumentException("Source cannot contain traversal components.", nameof(originalFile));
        if (originalFile[3..].Contains("\\\\", StringComparison.Ordinal))
            throw new ArgumentException("Source cannot contain empty path components.", nameof(originalFile));
        if (originalFile.EndsWith('\\'))
            throw new ArgumentException("Source must name a file.", nameof(originalFile));
        foreach (var part in originalFile[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Contains(':') || part.EndsWith(' ') || part.EndsWith('.'))
                throw new ArgumentException("Source contains an unsafe path component.", nameof(originalFile));
        }
        var original = Path.GetFullPath(originalFile);
        if (string.Equals(original, destination, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Source and destination must differ.", nameof(destinationFile));
        var root = Path.GetPathRoot(original)
            ?? throw new ArgumentException("A local drive path is required.", nameof(originalFile));
        var source = ShadowPath.ResolveForDrive(snapshot, original, root);

        SnapshotPathGuard.RejectReparsePoints(snapshot, source);
        if ((File.GetAttributes(source) & FileAttributes.Directory) != 0)
            throw new ArgumentException("Only regular files can be exported.", nameof(originalFile));

        var parent = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException("Destination must have a parent directory.", nameof(destinationFile));
        var driveRoot = Path.GetPathRoot(destination)!;
        var relativeParent = parent[driveRoot.Length..];
        var cursor = driveRoot;
        foreach (var part in relativeParent.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            cursor = Path.Combine(cursor, part);
            if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Destination path contains a reparse point.");
        }

        // Do not allow a caller to publish over an existing recovery file.
        // File.Move below enforces this again atomically against races.
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException("Recovery destination already exists.");

        // Stage in the destination directory, then publish atomically with no overwrite.
        // Failures leave the requested destination untouched.
        var staging = Path.Combine(parent, ".meridian-recovery-" + Guid.NewGuid().ToString("N") + ".tmp");
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read,
            FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            long copied;
            await using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 128 * 1024, FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, 128 * 1024, cancellationToken);
                await output.FlushAsync(cancellationToken);
                copied = output.Length;
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(staging, destination, overwrite: false);
            return copied;
        }
        finally
        {
            // Cleanup only our unique staging name, never the requested destination.
            try { File.Delete(staging); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
