# Recovery Readiness — explanation-only prototype

**Status: core-only policy prototype, not a journal, backup, Time Machine or undo feature.**

`RecoveryReadiness.Evaluate(record, observation)` examines a caller-provided record of a file move against caller-provided current observations. It explicitly refuses to authorize reversal when the original path is occupied, the recorded destination is missing, or its size or modification time differs.

Even when metadata matches, the result is `CandidateRequiresVerification` and `AuthorizesUndo` is **always false**. Metadata equality is not proof of identity; filesystem objects can be replaced between checks. The evaluator neither reads nor modifies filesystem objects.

```csharp
var recorded = new RecoveryReadiness.RecordedMove(
    @"C:\\Original\\note.txt", @"D:\\Archive\\note.txt",
    128, DateTime.UtcNow);
var observed = new RecoveryReadiness.CurrentObservation(
    OriginalExists: false, DestinationExists: true,
    DestinationLength: 128, DestinationModifiedUtc: recorded.DestinationModifiedUtc);
var assessment = RecoveryReadiness.Evaluate(recorded, observed);
// CandidateRequiresVerification does NOT authorize undo.
```

## Next safety gates

1. Design a versioned, durable append-only operation journal with crash-recovery tests; do not treat it as a backup.
2. Record robust filesystem identity and optional content hashes, where the storage provider supports them.
3. Acquire/validate filesystem objects at action time with non-overwrite semantics; refuse if ownership or identity is uncertain.
4. Test case changes, concurrent edits, path reuse, cross-volume moves, interrupted operations, offline drives, and rights changes on disposable Windows disks.
5. Only then expose a UI that can perform a narrowly scoped reverse action with explicit confirmation and auditable results.

Core-only synthetic tests establish policy logic, not Windows filesystem safety.
