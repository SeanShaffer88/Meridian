# Changelog

## Unreleased — development branch

- Dual-pane browsing, paired workspace tabs, bookmarks, and local workspace persistence.
- Folder DNA, Drop Shelf, read-only copy preview, and explicitly confirmed sequential batch copy with per-item progress and cancellation.
- New-folder creation, file/folder rename (including case-only changes), basic properties, cross-pane copy/move, and Recycle Bin requests.
- Name/type filtering in directory panes (with optional full-path matching retained in the core API); independent sorting.
- Navigation history now commits path/history changes after a successful directory read (PR #40).
- Windows CI builds x64 and ARM64 publish outputs and per-user Inno Setup installers.

**Status:** early preview, not a stable release. Source-level safeguards and CI results do not establish complete on-device validation, native ARM64 execution, installer/uninstaller correctness, accessibility coverage, or transactional rollback. See [verification evidence](docs/VERIFICATION_REPORT.md) and [release gate](docs/RELEASE_GATE.md). More extensive capabilities in open PRs are not part of `main` until merged.
