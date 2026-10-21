using System.Text;

namespace Meridian.Services;

/// <summary>
/// Best-effort local Capsule persistence. Uses a same-directory temporary file and atomic
/// rename on supported filesystems; never silently replaces malformed data during a load.
/// Concurrent writers, disk durability and backup recovery require additional integration tests.
/// </summary>
public sealed class ContextCapsuleStore(string filePath)
{
    private readonly string _path = Path.GetFullPath(filePath);

    public async Task<IReadOnlyList<ContextCapsule>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return Array.Empty<ContextCapsule>();
        await using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 8192, options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length > ContextCapsuleFormat.MaximumDocumentCharacters * 4L)
            throw new InvalidDataException("Capsule file exceeds the input byte limit.");
        using var reader = new StreamReader(input, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return ContextCapsuleFormat.Deserialize(text);
    }

    public async Task SaveAsync(IEnumerable<ContextCapsule> capsules, CancellationToken cancellationToken = default)
    {
        var json = ContextCapsuleFormat.Serialize(capsules);
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".meridian-capsules-{Guid.NewGuid():N}.tmp");
        var ownsTemporary = false;
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, bufferSize: 8192, options: FileOptions.Asynchronous))
            {
                ownsTemporary = true;
                await using var writer = new StreamWriter(output, new UTF8Encoding(false));
                await writer.WriteAsync(json.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, _path, overwrite: true);
            ownsTemporary = false;
        }
        finally
        {
            if (ownsTemporary)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
