namespace Meridian.Services;

/// <summary>
/// The facts an inspection report is built from: exactly what
/// <see cref="DriveInfo"/> reports for a drive, no more. DriveInfo has no
/// health or SMART data, so the report has none either — nothing here
/// invents a condition the operating system did not state.
/// </summary>
public sealed record DriveFacts(
    string RootPath,
    string VolumeLabel,
    DriveType DriveType,
    string DriveFormat,
    long TotalBytes,
    long FreeBytes,
    bool IsReady);

/// <summary>Reads the inspection facts for one drive root.</summary>
public interface IDriveFactsSource
{
    DriveFacts Read(string rootPath);
}

/// <summary>The production facts source: a thin, defensive wrap over <see cref="DriveInfo"/>.</summary>
public sealed class DriveInfoFactsSource : IDriveFactsSource
{
    public DriveFacts Read(string rootPath) => DriveInspector.FromDriveInfo(new DriveInfo(rootPath));
}

/// <summary>
/// Turns <see cref="DriveInfo"/> facts into the plain lines the Inspect
/// drive dialog shows. Kept in Core so the wording and the not-ready
/// behaviour are unit-tested: a drive that will not answer (empty card
/// reader, spun-down or disconnected storage) reports "Not ready" and
/// "Not available" figures instead of throwing or guessing.
/// </summary>
public static class DriveInspector
{
    /// <summary>
    /// Reads one <see cref="DriveInfo"/> into facts. The label, root and
    /// type are safe to read on any drive; size and format are only touched
    /// on a ready drive, and a drive that stops answering mid-read degrades
    /// to not-ready facts rather than throwing.
    /// </summary>
    public static DriveFacts FromDriveInfo(DriveInfo drive)
    {
        ArgumentNullException.ThrowIfNull(drive);
        var root = drive.Name;
        var label = string.Empty;
        try { label = drive.VolumeLabel ?? string.Empty; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        bool ready;
        try { ready = drive.IsReady; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ready = false; }
        if (!ready) return new DriveFacts(root, label, drive.DriveType, string.Empty, 0, 0, false);
        try
        {
            return new DriveFacts(
                root,
                label,
                drive.DriveType,
                drive.DriveFormat ?? string.Empty,
                drive.TotalSize,
                drive.AvailableFreeSpace,
                true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DriveFacts(root, label, drive.DriveType, string.Empty, 0, 0, false);
        }
    }

    /// <summary>The short type wording used across the app ("Local disk", "Removable", …).</summary>
    public static string TypeLabel(DriveType type) => type switch
    {
        DriveType.Fixed => "Local disk",
        DriveType.Removable => "Removable",
        DriveType.Network => "Network drive",
        DriveType.CDRom => "Disc drive",
        DriveType.Ram => "RAM disk",
        _ => "Drive"
    };

    /// <summary>The report lines, in display order. Only facts the drive
    /// itself reported appear — there is deliberately no health line,
    /// because Windows reports no health through this route.</summary>
    public static IReadOnlyList<string> Describe(DriveFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var lines = new List<string>
        {
            $"Volume label: {(string.IsNullOrWhiteSpace(facts.VolumeLabel) ? "—" : facts.VolumeLabel)}",
            $"Drive: {facts.RootPath}",
            $"Type: {TypeLabel(facts.DriveType)}"
        };
        if (!facts.IsReady)
        {
            lines.Add("File system: Not available");
            lines.Add("Capacity: Not available");
            lines.Add("Free space: Not available");
            lines.Add("State: Not ready — the drive did not answer. It may be empty, disconnected, or still waking up.");
            return lines;
        }
        lines.Add($"File system: {(string.IsNullOrWhiteSpace(facts.DriveFormat) ? "Not available" : facts.DriveFormat)}");
        lines.Add($"Capacity: {DirectoryReader.FormatSize(facts.TotalBytes)}");
        lines.Add($"Free space: {DriveSummary.FreeSpaceLabel(facts.TotalBytes, facts.FreeBytes)}");
        lines.Add("State: Ready");
        return lines;
    }
}
