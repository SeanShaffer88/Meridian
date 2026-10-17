namespace Meridian.Services;

/// <summary>Conservative copy operations. Existing destinations are never overwritten.</summary>
public static class FileOperations
{
    /// <summary>Renames a file or directory in place without replacing an existing item.</summary>
    public static string RenameItem(string sourcePath, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        if (newName is "." or ".." ||
            newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            newName.Contains(Path.DirectorySeparatorChar) ||
            newName.Contains(Path.AltDirectorySeparatorChar) ||
            Path.IsPathRooted(newName) ||
            !string.Equals(newName, newName.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Enter a valid file or folder name without path separators or surrounding whitespace.", nameof(newName));

        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source) && !Directory.Exists(source))
            throw new FileNotFoundException("The selected item no longer exists.", source);
        RejectReparsePoint(source);

        var parent = Path.GetDirectoryName(source);
        if (string.IsNullOrWhiteSpace(parent))
            throw new IOException("The selected item cannot be renamed from its current location.");

        var originalName = Path.GetFileName(source);
        if (string.Equals(originalName, newName, StringComparison.Ordinal))
            return source;

        var destination = Path.Combine(parent, newName);
        var caseOnlyRename = string.Equals(originalName, newName, StringComparison.OrdinalIgnoreCase);
        if (!caseOnlyRename && (File.Exists(destination) || Directory.Exists(destination)))
            throw new IOException($"'{newName}' already exists in this folder. Nothing was overwritten.");

        var isDirectory = Directory.Exists(source);
        if (caseOnlyRename)
        {
            // Windows paths are case-insensitive; use a unique temporary sibling for case-only changes.
            var temporary = Path.Combine(parent, $".meridian-rename-{Guid.NewGuid():N}.tmp");
            MoveItem(source, temporary, isDirectory);
            try { MoveItem(temporary, destination, isDirectory); }
            catch
            {
                try { MoveItem(temporary, source, isDirectory); }
                catch { /* Preserve the original failure if rollback is blocked. */ }
                throw;
            }
        }
        else MoveItem(source, destination, isDirectory);

        return destination;
    }

    private static void MoveItem(string source, string destination, bool isDirectory)
    {
        if (isDirectory) Directory.Move(source, destination);
        else File.Move(source, destination);
    }

    public static string CopyItem(string sourcePath, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        var source = Path.GetFullPath(sourcePath);
        var destinationRoot = Path.GetFullPath(destinationDirectory);
        if (!File.Exists(source) && !Directory.Exists(source))
            throw new FileNotFoundException("The selected source no longer exists.", source);
        if (!Directory.Exists(destinationRoot))
            throw new DirectoryNotFoundException($"The destination folder does not exist: {destinationRoot}");
        RejectReparsePoint(source);
        if (Directory.Exists(source))
        {
            var relativeDestination = Path.GetRelativePath(source, destinationRoot);
            var isSelfOrDescendant = !Path.IsPathRooted(relativeDestination) &&
                (relativeDestination == "." ||
                 (relativeDestination != ".." &&
                  !relativeDestination.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)));
            if (isSelfOrDescendant)
                throw new IOException("A folder cannot be copied into itself or one of its descendants.");
        }
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
        var finalPath = Path.Combine(destinationRoot, name);
        if (File.Exists(finalPath) || Directory.Exists(finalPath))
            throw new IOException($"'{name}' already exists in the destination. Nothing was overwritten.");
        var stagingPath = Path.Combine(destinationRoot, $".meridian-{Guid.NewGuid():N}.partial");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(source))
            {
                Directory.CreateDirectory(stagingPath);
                CopyDirectoryContents(source, stagingPath, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Move(stagingPath, finalPath);
            }
            else
            {
                CopyFileWithCancellation(source, stagingPath, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(stagingPath, finalPath);
            }
            return finalPath;
        }
        catch { TryDeleteStaging(stagingPath); throw; }
    }

    private static void CopyDirectoryContents(string source, string destination, CancellationToken cancellationToken)
    {
        foreach (var sourcePath in Directory.EnumerateFileSystemEntries(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePoint(sourcePath);
            var targetPath = Path.Combine(destination, Path.GetFileName(sourcePath));
            var attributes = File.GetAttributes(sourcePath);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                Directory.CreateDirectory(targetPath);
                CopyDirectoryContents(sourcePath, targetPath, cancellationToken);
            }
            else CopyFileWithCancellation(sourcePath, targetPath, cancellationToken);
        }
    }

    private static void CopyFileWithCancellation(string source, string destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var creationTime = File.GetCreationTimeUtc(source);
        var lastWriteTime = File.GetLastWriteTimeUtc(source);
        var lastAccessTime = File.GetLastAccessTimeUtc(source);
        var buffer = new byte[1024 * 1024];
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, FileOptions.SequentialScan))
        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.SequentialScan))
        {
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        File.SetCreationTimeUtc(destination, creationTime);
        File.SetLastWriteTimeUtc(destination, lastWriteTime);
        File.SetLastAccessTimeUtc(destination, lastAccessTime);
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Copying symbolic links and other reparse points is not supported: {path}");
    }

    private static void TryDeleteStaging(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch { /* Preserve the original failure; leftover staging recovery is documented. */ }
    }
}
