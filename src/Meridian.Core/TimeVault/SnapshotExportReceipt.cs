namespace Meridian.Core.TimeVault;

/// <summary>Immutable result of a completed snapshot export. Never implies that a backup was created.</summary>
public sealed record SnapshotExportReceipt(
    Guid Id,
    string SnapshotId,
    string OriginalFile,
    string DestinationFile,
    long BytesCopied,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt);

/// <summary>Creates a receipt only after a successful no-overwrite export.</summary>
public sealed class SnapshotExportService(SnapshotFileExporter exporter)
{
    public async Task<SnapshotExportReceipt> ExportAsync(
        ShadowCopy snapshot, string originalFile, string destinationFile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(exporter);
        var started = DateTimeOffset.UtcNow;
        var count = await exporter.ExportAsync(snapshot, originalFile, destinationFile, cancellationToken);
        return new SnapshotExportReceipt(Guid.NewGuid(), snapshot.Id, originalFile, destinationFile,
            count, started, DateTimeOffset.UtcNow);
    }
}
