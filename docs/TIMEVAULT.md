# Meridian TimeVault

> Explore yesterday without risking today.

## Status

**Read-only snapshot discovery UI implemented in the development branch; Windows runtime verification pending.** This branch contains immutable snapshot metadata, a drive-letter-scoped path resolver, and a Windows WMI-based snapshot catalog. The catalog has not yet passed Windows CI or real-device validation. A toolbar button opens a snapshot selection dialog with an opt-in read-only listing of the active pane's corresponding historical folder. The listing is nonrecursive, limited to 250 displayed entries, and does not open or export files. Windows runtime verification remains pending. The UI does **not** open historical files, export recovery data, or create snapshots; a separate backend-only export prototype exists. WMI VolumeName often uses volume GUID paths; drive-letter GUID matching has now been added but has not been verified on Windows; mount-point folders and volumes without drive letters remain unsupported.

## Product direction

1. **Snapshot Explorer:** discover existing VSS snapshots and browse them read-only.
2. **File Time Machine:** inspect historical versions for a selected file or folder.
3. **Recovery Studio:** preview and export recovered content to a user-selected destination, never overwrite by default.
4. **Snapshot Search:** scoped, cancellable search with clear progress and limits.
5. **Difference Lens:** compare file hashes, metadata and directory trees across snapshots.
6. **Recovery Receipt:** record exported paths, source snapshot IDs and optional SHA-256 checksums.
7. **Snapshot Health:** display volume coverage, snapshot age, VSS service errors and access restrictions.
8. **Timeline Collections:** group snapshots by volume and date; indicate gaps without implying backups exist.

## Safety architecture

- Browsing must not require elevation when Windows permits access; privileged operations require explicit consent.
- Never create/delete snapshots implicitly; do not use destructive VSS administration commands.
- Treat snapshot contents as untrusted: no automatic execution, script previews or shell activation.
- Revalidate canonical paths and destinations before every export. Reject traversal, reparse-point escapes, and snapshot-to-live path confusion.
- Avoid parsing localized console output. Prefer Windows VSS APIs or appropriately constrained WMI/CIM calls.
- Handle expired snapshots, inaccessible volumes, permissions, BitLocker and network paths explicitly.
- Snapshot copies are **not backups**: Windows may purge them without notice.
- Large scans must be bounded, cancellable, and never block the UI thread.

## Implementation milestones

- [x] Define snapshot metadata and scoped path resolution foundation.
- [x] Implement a WMI-based read-only Windows VSS catalog behind an interface (Windows validation pending).
- [ ] Complete tests for path normalization, volume GUID mapping, volume boundaries, and malformed device objects (additional tests added; Windows validation pending).
- [ ] WinUI read-only snapshot explorer and timeline (snapshot selection and limited read-only folder listing added; full explorer and timeline pending).
- [ ] Read-only metadata preview, search and compare.
- [ ] Export with collision-safe naming and checksum verification.
- [ ] Windows x64/ARM64 integration tests against controlled test snapshots.
- [ ] Accessibility, localization, security audit, and end-user documentation.

This document is a roadmap, not a claim that these features already work.

## Recovery export foundation (unverified)

`SnapshotFileExporter` provides a single-file, streaming, cancellation-aware copy from an existing snapshot to an explicitly supplied destination. The exporter now writes to a uniquely named staging file in the destination directory, then publishes with `File.Move(..., overwrite: false)` after the copy succeeds. Existing destination files are not overwritten and failed copies do not publish partial results. It rejects reparse-point components in the snapshot path and attempts to remove staging files on failure. It is **not connected to the UI** and must not be represented as a released recovery feature. Destination-parent reparse checks and a basic export receipt model are implemented, but neither prevents time-of-check/time-of-use races or proves successful recovery on real Windows systems. Before exposing export to users, add a confirmation dialog, handle-based file identity/reparse race defenses, integration tests, checksum verification, and UI receipt presentation.

## Current verification and release gates

| Area | Current state | Still required |
| --- | --- | --- |
| Snapshot catalog | WMI implementation on feature branch | Windows 11 x64/ARM64 execution and access-denied scenarios |
| Snapshot browsing | Toolbar picker and limited read-only listing | Actual VSS volumes, inaccessible/expired snapshots, accessibility |
| Export engine | Backend-only staged, no-overwrite single-file copy | Handle-based reparse safety, cancellation/cleanup integration tests |
| Export receipt | Backend data contract | UI presentation, optional verified checksum |
| CI | Windows build/test workflow configured; test-result artifacts configured | Confirm successful current-branch jobs and inspect reports |

**Security boundary:** Path-component checks and reparse-attribute inspection are best-effort; filesystem objects can change between inspection and opening. No guarantee of race-free export or recovery is made. VSS is not a substitute for a separate backup.
