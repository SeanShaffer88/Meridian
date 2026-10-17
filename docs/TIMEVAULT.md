# Meridian TimeVault

> Explore yesterday without risking today.

## Status

**Read-only snapshot discovery UI implemented in the development branch; Windows runtime verification pending.** This branch contains immutable snapshot metadata, a drive-letter-scoped path resolver, and a Windows WMI-based snapshot catalog. The catalog has not yet passed Windows CI or real-device validation. A toolbar button opens a metadata-only discovery dialog. A separate, nonrecursive snapshot directory reader now exists in core, but is not yet connected to the dialog. It does **not** browse historical files, export recovery data, or create snapshots. WMI VolumeName often uses volume GUID paths; drive-letter GUID matching has now been added but has not been verified on Windows; mount-point folders and volumes without drive letters remain unsupported.

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
- [ ] WinUI read-only snapshot explorer and timeline (metadata-only discovery dialog and backend directory reader added; browsing UI pending).
- [ ] Read-only metadata preview, search and compare.
- [ ] Export with collision-safe naming and checksum verification.
- [ ] Windows x64/ARM64 integration tests against controlled test snapshots.
- [ ] Accessibility, localization, security audit, and end-user documentation.

This document is a roadmap, not a claim that these features already work.
