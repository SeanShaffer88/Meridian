namespace Meridian.Services;

/// <summary>Copy, move and rename without intentional replacement of existing destinations.</summary>
/// <remarks>Path checks are not race-free. Moves require a Windows rename without copy fallback.</remarks>
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

        if (caseOnlyRename)
        {
            // Windows paths are case-insensitive; use a unique temporary sibling for case-only changes.
            var temporary = Path.Combine(parent, $".meridian-rename-{Guid.NewGuid():N}.tmp");
            MoveWithoutCopy(source, temporary);
            try { MoveWithoutCopy(temporary, destination); }
            catch
            {
                try { MoveWithoutCopy(temporary, source); }
                catch { /* Preserve the original failure if rollback is blocked. */ }
                throw;
            }
        }
        else MoveWithoutCopy(source, destination);

        return destination;
    }

    // File.Move can emulate cross-volume moves with copy/delete. Do not enable
    // MOVEFILE_COPY_ALLOWED or MOVEFILE_REPLACE_EXISTING: an unsupported rename
    // must fail without staging a copy or removing the source.
    private static void MoveWithoutCopy(string source, string destination)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Move and rename require Windows.");
        if (MoveFileEx(ForNativeMove(source), ForNativeMove(destination), 0)) return;
        var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        var detail = new System.ComponentModel.Win32Exception(error);
        if (error == 17) // ERROR_NOT_SAME_DEVICE
            throw new IOException("Move between volumes is not supported. No copy was made and the source was not removed. Use Copy to keep the original.", detail);
        if (error == 5) // ERROR_ACCESS_DENIED
            throw new UnauthorizedAccessException("The item could not be moved. " + detail.Message, detail);
        throw new IOException("The item could not be moved. " + detail.Message, detail);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "MoveFileExW",
        CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingName, string newName, uint flags);

    private static string ForNativeMove(string path)
    {
        if (path.Length < 260 || path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        return path.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    }

    /// <summary>Moves a file or directory to an existing destination folder without overwriting.</summary>
    public static string MoveItem(string sourcePath, string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        var source = Path.GetFullPath(sourcePath);
        var destinationRoot = Path.GetFullPath(destinationDirectory);
        if (!File.Exists(source) && !Directory.Exists(source))
            throw new FileNotFoundException("The selected source no longer exists.", source);
        if (!Directory.Exists(destinationRoot))
            throw new DirectoryNotFoundException($"The destination folder does not exist: {destinationRoot}");
        RejectReparsePoint(destinationRoot);
        RejectReparsePoint(source);

        var isDirectory = Directory.Exists(source);
        if (isDirectory)
        {
            var relativeDestination = Path.GetRelativePath(source, destinationRoot);
            var isSelfOrDescendant = !Path.IsPathRooted(relativeDestination) &&
                (relativeDestination == "." ||
                 (relativeDestination != ".." &&
                  !relativeDestination.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)));
            if (isSelfOrDescendant)
                throw new IOException("A folder cannot be moved into itself or one of its descendants.");
        }

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
        if (string.IsNullOrWhiteSpace(name))
            throw new IOException("The selected item cannot be moved from its current location.");
        var finalPath = Path.Combine(destinationRoot, name);
        if (string.Equals(source, finalPath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The source and destination are the same item.");
        if (File.Exists(finalPath) || Directory.Exists(finalPath))
            throw new IOException($"'{name}' already exists in the destination. Nothing was overwritten.");

        MoveWithoutCopy(source, finalPath);
        return finalPath;
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
        RejectReparsePoint(destinationRoot);
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
        // A regular leaf can still resolve through a linked ancestor. Check the
        // complete existing path before reading, renaming, or staging writes.
        // These checks are conservative; they do not prevent concurrent path swaps.
        string? current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        while (current is not null)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"File operations through symbolic links and other reparse points are not supported: {current}");
            current = Path.GetDirectoryName(current);
        }
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
