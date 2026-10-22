namespace Meridian.Services;

/// <summary>
/// Explanation-only recovery screening. It never performs undo or accesses the filesystem.
/// Callers must separately verify real filesystem object identity and content before any reversal.
/// </summary>
public static class RecoveryReadiness
{
    public enum Verdict
    {
        OriginalOccupied,
        DestinationMissing,
        DestinationChanged,
        CandidateRequiresVerification
    }

    public sealed record RecordedMove(string OriginalPath, string DestinationPath,
        long DestinationLength, DateTime DestinationModifiedUtc);

    public sealed record CurrentObservation(bool OriginalExists, bool DestinationExists,
        long? DestinationLength, DateTime? DestinationModifiedUtc);

    public sealed record Assessment(Verdict Result, string Explanation, bool AuthorizesUndo);

    /// <summary>
    /// Pure conservative screening of caller-provided observations. Not an atomic filesystem check.
    /// Size and timestamp equality are not proof of file identity or content integrity.
    /// </summary>
    public static Assessment Evaluate(RecordedMove record, CurrentObservation observed)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.OriginalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.DestinationPath);
        if (record.DestinationLength < 0)
            throw new ArgumentOutOfRangeException(nameof(record), "Recorded length cannot be negative.");

        if (observed.OriginalExists)
            return new(Verdict.OriginalOccupied,
                "The original path is occupied; reversing could replace unrelated data.", false);
        if (!observed.DestinationExists)
            return new(Verdict.DestinationMissing,
                "The recorded destination is no longer available.", false);
        if (observed.DestinationLength != record.DestinationLength ||
            observed.DestinationModifiedUtc != record.DestinationModifiedUtc)
            return new(Verdict.DestinationChanged,
                "Destination metadata differs from the recorded operation.", false);

        return new(Verdict.CandidateRequiresVerification,
            "Metadata matches, but identity and content must still be verified immediately before any undo.", false);
    }
}
