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
        var destination = Path.GetFullPath(destinationFile);
        var original = Path.GetFullPath(originalFile);
        if (string.Equals(original, destination, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Source and destination must differ.", nameof(destinationFile));
        var root = Path.GetPathRoot(original)
            ?? throw new ArgumentException("A local drive path is required.", nameof(originalFile));
        var source = ShadowPath.ResolveForDrive(snapshot, original, root);

        // Reject reparse points at every ancestor of the snapshot source.
        var deviceRoot = snapshot.DeviceObject.TrimEnd('\\');
        var relative = source[deviceRoot.Length..].TrimStart('\\');
        var current = deviceRoot;
        foreach (var part in relative.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Snapshot path contains a reparse point.");
        }
        if ((File.GetAttributes(source) & FileAttributes.Directory) != 0)
            throw new ArgumentException("Only regular files can be exported.", nameof(originalFile));

        // Restrict initial exports to ordinary local drive-letter destinations.
        if (destination.Length < 3 || !char.IsLetter(destination[0]) ||
            destination[1] != ':' || destination[2] != '\\')
            throw new ArgumentException("Destination must be a local drive-letter path.", nameof(destinationFile));
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

        // CreateNew is atomic: an existing destination is never overwritten.
        // The caller must choose an explicit destination and confirm the operation.
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read,
            FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        cancellationToken.ThrowIfCancellationRequested();
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 128 * 1024, FileOptions.Asynchronous);
        try
        {
            await input.CopyToAsync(output, 128 * 1024, cancellationToken);
            await output.FlushAsync(cancellationToken);
            return output.Length;
        }
        catch
        {
            await output.DisposeAsync();
            try { File.Delete(destination); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }
}
