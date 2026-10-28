namespace Meridian.Core.Transfers;

/// <summary>
/// The filesystem surface the transfer executor runs over. The default implementation is the
/// real filesystem; tests substitute wrappers to inject failures (a corrupting read, a delete
/// that refuses) without changing the executor's code path. All paths are full paths.
/// </summary>
public interface ITransferFileOperations
{
    bool FileExists(string path);
    bool DirectoryExists(string path);

    /// <summary>Facts about a file, or null when the path is missing or is a folder.</summary>
    FileFacts? GetFileFacts(string path);

    /// <summary>Full paths of the files directly inside a folder (not recursive).</summary>
    IEnumerable<string> EnumerateFiles(string directoryPath);

    /// <summary>Full paths of the folders directly inside a folder (not recursive).</summary>
    IEnumerable<string> EnumerateDirectories(string directoryPath);

    Stream OpenRead(string path);

    /// <summary>Creates a new file, failing if it already exists. Used for staging files.</summary>
    Stream CreateFile(string path);

    /// <summary>Creates a folder, including any missing parents; harmless when it already exists.</summary>
    void CreateDirectory(string path);

    /// <summary>Moves a fully written staging file to its final destination, optionally replacing it.</summary>
    void PublishFile(string stagingPath, string destinationPath, bool overwrite);

    void DeleteFile(string path);

    /// <summary>Deletes a folder; recursive only when asked (empty-folder cleanup passes false).</summary>
    void DeleteDirectory(string path, bool recursive);

    void SetLastWriteUtc(string path, DateTimeOffset lastWriteUtc);
}

/// <summary>The real-filesystem <see cref="ITransferFileOperations"/>.</summary>
public sealed class SystemTransferFileOperations : ITransferFileOperations
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public FileFacts? GetFileFacts(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return null;
        return new FileFacts(info.Length, new DateTimeOffset(info.LastWriteTimeUtc));
    }

    public IEnumerable<string> EnumerateFiles(string directoryPath) => Directory.EnumerateFiles(directoryPath);

    public IEnumerable<string> EnumerateDirectories(string directoryPath) => Directory.EnumerateDirectories(directoryPath);

    public Stream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read,
        FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

    public Stream CreateFile(string path) => new FileStream(path, FileMode.CreateNew, FileAccess.Write,
        FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void PublishFile(string stagingPath, string destinationPath, bool overwrite) =>
        File.Move(stagingPath, destinationPath, overwrite);

    public void DeleteFile(string path) => File.Delete(path);

    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);

    public void SetLastWriteUtc(string path, DateTimeOffset lastWriteUtc) =>
        File.SetLastWriteTimeUtc(path, lastWriteUtc.UtcDateTime);
}
