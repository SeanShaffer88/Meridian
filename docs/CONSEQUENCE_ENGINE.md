# Meridian Consequence Engine

**Status: initial contracts and advisory copy planner; not connected to the UI.**

The Consequence Engine separates *intent*, *preview*, *execution* and *observed result*.

1. **Intent:** a user selects sources and an operation.
2. **Preview:** an immutable plan describes known destinations, collisions and blockers.
3. **Confirmation:** the user approves the exact intended action.
4. **Revalidation:** execution independently checks source identity, permissions, reparse points, collisions and current destination state.
5. **Execution:** the existing conservative operation engine performs approved actions with progress and cancellation.
6. **Receipt:** record actual outcomes, not assumptions or hypothetical rollbacks.

The first implementation is a read-only batch-copy planning model. It is **advisory** and does not execute or authorize any file modification. The plan's `CanExecute` property only means no *known plan-level blockers* were found; it is not a guarantee that execution is possible or safe.

## Future gates

- Explicit provider capability model for local, network, archive, snapshot and device paths.
- No-follow and reparse-point policies, volume boundaries, case behavior and canonical identity checks.
- Race-resistant destination creation, no-overwrite defaults and conflict reporting.
- Immutable operation receipts with opt-in local retention and privacy controls.
- Carefully constrained reversal for verified operations only.
- Windows x64/ARM64 tests, including permissions, disappearing files, links and collision races.

**No operation is atomic across arbitrary filesystem providers.** A receipt is not a backup.

## Existing UI is a separate path

Drop Shelf already has an integrated advisory preflight and confirmed sequential executor through `Services/OperationPreview.cs` and `Services/BatchCopy.cs`. It displays partial results and supports cooperative cancellation. That is not UI integration of the `Consequences` contracts described here. `OperationReceipt` records outcome fields and counts; it is not persisted automatically and does not implement inverse actions. See [current architecture](ARCHITECTURE.md), [status](STATUS.md) and [user guide](USER_GUIDE.md#drop-shelf-and-folder-dna).
