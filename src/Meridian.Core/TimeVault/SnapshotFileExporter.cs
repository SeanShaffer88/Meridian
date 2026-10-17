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

        var original = Path.GetFullPath(originalFile);
        var destination = Path.GetFullPath(destinationFile);
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

        // CreateNew is atomic: an existing destination is never overwritten.
        // The caller must choose an explicit destination and confirm the operation.
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 128 * 1024, FileOptions.Asynchronous);
        try
        {
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read,
                FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
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
