# Storage Reclaimer — development notes

**Status:** Backend prototype in PR #37. No cleanup UI, automated deletion, scheduled cleanup, Windows servicing, or elevated actions exist.

## Current prototype

- `CleanupPlan` captures immutable findings, estimated sizes, explanations and risk categories; duplicate targets are rejected.
- `CleanupSelection` refuses protected, unknown or duplicate selections.
- `CleanupPreviewBuilder` produces an explicit, non-executable scope and risk summary.
- `TemporaryFileInventory.ScanAsync` inventories immediate regular files in one caller-selected directory.
- The scan is nonrecursive, has a configurable entry limit (1–100,000), supports cancellation and skips link entries and checks directory ancestors for reparse points before enumeration (best-effort, not race-free).
- Every discovered file is classified **ReviewRequired**, never automatically safe to delete.
- Size estimates are advisory; files may change during or after scanning.
- The scanner does not delete, change permissions, modify Windows settings or request elevation.

## Security and release gates

1. Establish explicit trusted scan roots, path-boundary rules and protections for sensitive system/user folders.
2. Prevent link and path-swap races with handle-based checks where supported.
3. Distinguish user caches, operating-system-managed files and personal data; never classify arbitrary folders as safe.
4. Add isolated tests for enumeration limits, cancellation, permission failures, reparse points and concurrent changes.
5. Implement preview-first cleanup with explicit user confirmation and a receipt, only after Windows x64/ARM64 integration tests.
6. Prefer supported Windows cleanup/servicing APIs. Avoid unsupported registry cleaners and manual component-store removal.

See [roadmap](ROADMAP.md#proposed-system-utilities--restore-center-storage-reclaimer-and-meridian-vaults).
