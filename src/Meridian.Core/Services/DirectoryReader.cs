using Meridian.Models;

namespace Meridian.Services;

/// <summary>Reads one directory without recursively calculating folder sizes.</summary>
public static class DirectoryReader
{
    public static IReadOnlyList<FileEntry> Read(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
            throw new DirectoryNotFoundException($"The folder no longer exists: {path}");

        var entries = new List<FileEntry>();
        foreach (var childPath in Directory.EnumerateFileSystemEntries(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var attributes = File.GetAttributes(childPath);
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                var info = isDirectory ? (FileSystemInfo)new DirectoryInfo(childPath) : new FileInfo(childPath);
                info.Refresh();
                var size = info is FileInfo file ? FormatSize(file.Length) : "—";
                entries.Add(new FileEntry
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    IsDirectory = isDirectory,
                    TypeLabel = isDirectory ? "Folder" : GetTypeLabel(info.Name),
                    SizeLabel = size,
                    ModifiedLabel = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // An inaccessible or disappearing item should not block the rest of the listing.
            }
        }

        return entries.OrderByDescending(item => item.IsDirectory)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Filters a previously-read directory listing without touching the filesystem again.</summary>
    public static IReadOnlyList<FileEntry> Filter(IReadOnlyList<FileEntry> entries, string? query)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var normalizedQuery = query?.Trim();
        IEnumerable<FileEntry> filtered = entries;
        if (!string.IsNullOrEmpty(normalizedQuery))
        {
            filtered = entries.Where(item =>
                item.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                item.FullPath.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                item.TypeLabel.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase));
        }

        return filtered.OrderByDescending(item => item.IsDirectory)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{bytes:N0} B" : $"{size:0.##} {units[unit]}";
    }

    private static string GetTypeLabel(string name)
    {
        var extension = Path.GetExtension(name);
        return string.IsNullOrEmpty(extension) ? "File" : $"{extension.TrimStart('.').ToUpperInvariant()} file";
    }
}
