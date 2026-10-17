namespace Meridian.Services;

/// <summary>Copy, move and rename without intentional replacement of existing destinations.</summary>
/// <remarks>Path checks are not race-free. Moves require a Windows rename without copy fallback.</remarks>
public static class FileOperations
{
    private const int CopyBufferSize = 1024 * 1024;
    /// <summary>Renames a file or directory in place without replacing an existing item.</summary>
    public static string RenameItem(string sourcePath, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ValidateNewName(newName);

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

        // Use one Windows rename for casing changes as well, without a
        // temporary sibling and its second-step/rollback failure paths.
        MoveWithoutCopy(source, destination);

        return destination;
    }

    // File.Move can emulate cross-volume moves with copy/delete. Do not enable
    // MOVEFILE_COPY_ALLOWED or MOVEFILE_REPLACE_EXISTING: an unsupported rename
    // must fail without staging a copy or removing the source.
    private static void MoveWithoutCopy(string source, string destination)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Move and rename require Windows.");
        if (MoveFileEx(ForNativePath(source), ForNativePath(destination), 0)) return;
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

    private static string ForNativePath(string path)
    {
        if (path.Length < 260 || path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        return path.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    }

    /// <summary>Creates exactly one new folder in an existing, checked parent; an existing item is an error.</summary>
    public static string CreateFolder(string parentDirectory, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        ValidateNewName(name);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Folder creation requires Windows.");
        if (!Path.IsPathFullyQualified(parentDirectory))
            throw new ArgumentException("Choose an absolute parent directory.", nameof(parentDirectory));
        var parent = Path.GetFullPath(parentDirectory);
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        RejectReparsePoint(parent);
        var target = Path.Combine(parent, name);
        // Unlike Directory.CreateDirectory, this fails when the final folder
        // already exists, including a collision after our parent inspection.
        if (CreateDirectory(ForNativePath(target), IntPtr.Zero)) return target;
        var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        var detail = new System.ComponentModel.Win32Exception(error);
        if (error is 80 or 183)
            throw new IOException($"'{name}' already exists. No folder was created.", detail);
        if (error == 5)
            throw new UnauthorizedAccessException("The folder could not be created. " + detail.Message, detail);
        throw new IOException("The folder could not be created. " + detail.Message, detail);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW",
        CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CreateDirectory(string path, IntPtr securityAttributes);

    private static void ValidateNewName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is "." or ".." || name.Length > 255 || name.EndsWith('.') ||
            !string.Equals(name, name.Trim(), StringComparison.Ordinal) ||
            name.IndexOfAny(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }) >= 0 ||
            name.Any(char.IsControl))
            throw new ArgumentException("Enter a single valid name without surrounding whitespace, a trailing period or reserved characters.", nameof(name));
        var device = name.Split('.')[0].TrimEnd(' ');
        if (device.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            device.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            device.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            device.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            (device.Length == 4 && (device.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                device.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                (device[3] is >= '1' and <= '9' or '¹' or '²' or '³')))
            throw new ArgumentException("Windows reserves this device name, including when followed by an extension.", nameof(name));
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

    /// <summary>Bytes written to staging across the copy; not a receipt of final publication.</summary>
    public sealed record CopyProgress(string Source, long BytesCopied);

    public static string CopyItem(string sourcePath, string destinationDirectory,
        CancellationToken cancellationToken = default, IProgress<CopyProgress>? progress = null)
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
        var reporting = progress is null ? null : new CopyProgressState(progress);
        byte[]? buffer = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(CopyBufferSize);
            if (Directory.Exists(source))
            {
                Directory.CreateDirectory(stagingPath);
                CopyDirectoryContents(source, stagingPath, buffer, cancellationToken, reporting);
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Move(stagingPath, finalPath);
            }
            else
            {
                CopyFileWithCancellation(source, stagingPath, buffer, cancellationToken, reporting);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(stagingPath, finalPath);
            }
            return finalPath;
        }
        catch { TryDeleteStaging(stagingPath); throw; }
        finally
        {
            if (buffer is not null) System.Buffers.ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static void CopyDirectoryContents(string source, string destination, byte[] buffer,
        CancellationToken cancellationToken, CopyProgressState? reporting)
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
                CopyDirectoryContents(sourcePath, targetPath, buffer, cancellationToken, reporting);
            }
            else CopyFileWithCancellation(sourcePath, targetPath, buffer, cancellationToken, reporting);
        }
    }

    private static void CopyFileWithCancellation(string source, string destination, byte[] buffer,
        CancellationToken cancellationToken, CopyProgressState? reporting)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var creationTime = File.GetCreationTimeUtc(source);
        var lastWriteTime = File.GetLastWriteTimeUtc(source);
        var lastAccessTime = File.GetLastAccessTimeUtc(source);
        // One operation-owned transfer buffer is reused throughout the tree.
        // Small stream buffers avoid another large allocation for every file.
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan))
        {
            int read;
            while ((read = input.Read(buffer, 0, CopyBufferSize)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
                reporting?.Report(source, read);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        File.SetCreationTimeUtc(destination, creationTime);
        File.SetLastWriteTimeUtc(destination, lastWriteTime);
        File.SetLastAccessTimeUtc(destination, lastAccessTime);
    }

    private sealed class CopyProgressState(IProgress<CopyProgress> observer)
    {
        private long _bytes;

        public void Report(string source, int count)
        {
            _bytes = checked(_bytes + count);
            // Reporting is observational. Its exceptions must not change the
            // operation outcome; cancellation is controlled by the token.
            try { observer.Report(new CopyProgress(source, _bytes)); }
            catch { }
        }
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
