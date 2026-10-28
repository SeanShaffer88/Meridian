using System.Text.Json;

namespace Meridian.Services;

/// <summary>Best-effort, bounded local diagnostics; never changes exception handling.</summary>
internal static class FailureLog
{
    internal const int MaximumBytes = 128 * 1024;
    private const int MaximumExceptions = 4;

    internal static bool TryRecord(Exception exception, string? directory = null)
    {
        string? temporary = null;
        try
        {
            ArgumentNullException.ThrowIfNull(exception);
            var details = new List<Detail>(MaximumExceptions);
            Exception? current = exception;
            while (current is not null && details.Count < MaximumExceptions)
            {
                var type = current.GetType().FullName ?? "Exception";
                var message = current.Message;
                var stack = current.StackTrace;
                details.Add(new Detail(Limit(type, 256)!, current.HResult, Limit(message, 512)!, Limit(stack, 4096),
                    type.Length > 256 || message.Length > 512 || stack?.Length > 4096));
                current = current.InnerException;
            }
            // Do not call exception.ToString or format arbitrary exception.Data objects.
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new Record(1, DateTimeOffset.UtcNow, details, current is not null));
            if (bytes.Length > MaximumBytes) return false;
            var folder = Path.GetFullPath(directory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meridian"));
            Directory.CreateDirectory(folder);
            temporary = Path.Combine(folder, ".last-error-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(bytes);
            File.Move(temporary, Path.Combine(folder, "last-error.log"), overwrite: true);
            return true;
        }
        catch { return false; } // A diagnostic failure must not replace the original application failure.
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch { /* Best effort only; an interrupted write may leave a temporary sibling. */ }
            }
        }
    }

    private static string? Limit(string? value, int maximum) => value?.Length > maximum ? value[..maximum] : value;
    private sealed record Detail(string Type, int HResult, string Message, string? StackTrace, bool Truncated);
    private sealed record Record(int Version, DateTimeOffset TimestampUtc, List<Detail> Exceptions, bool ChainTruncated);
}
