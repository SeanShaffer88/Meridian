# Changelog

## Unreleased

- Dual-pane browsing, paired workspace tabs, bookmarks, and local workspace persistence.
- Folder DNA, Drop Shelf, read-only copy preview, and explicitly confirmed sequential batch copy with per-item progress and cancellation.
- New-folder creation, file/folder rename (including case-only changes), basic properties, cross-pane copy/move, and Recycle Bin requests.
- Name/type filtering in directory panes (with optional full-path matching retained in the core API); independent sorting.
- Navigation history now commits path/history changes after a successful directory read (PR #40).
- Windows CI builds x64 and ARM64 publish outputs and per-user Inno Setup installers.
- Bounded UTF-8/UTF-16/hex previews, image preview and Windows media controls without autoplay; playback/resource verification remains open.
- TimeVault WMI discovery, volume matching and limited read-only historical listing; backend-only single-file export and receipt contracts, without a recovery UI.
- ZIP/TAR/TGZ inspection, creation from selected files/folders and staged whole-tree extraction; entry policy, byte/count limits, CRC/trailer validation and cancellation/cleanup regressions.
- Bounded Foresight hints/JSON reference extraction, Consequence Engine plan/receipt contracts and read-only Storage Reclaimer inventory/plans; no AI, cleanup executor or universal undo.
- Reject existing linked ancestors in copy/move/rename; preserve the originating archive-extraction pane.
- Fix published-app startup by including its XAML resource index; include WinUI resources and valid toolbar symbols. Record latest startup/unhandled error locally.
- Remove unsafe cross-volume move copy/delete fallback. Move/rename now use Windows rename without replacement or copy fallback, support extended paths and let Windows resolve volume identity. Four regressions and local C: to E: file/folder refusal checks pass; source data remains intact and destination empty.

**Status:** early preview, not a stable release. Source-level safeguards and CI results do not establish complete on-device validation, native ARM64 execution, installer/uninstaller correctness, accessibility coverage, or transactional rollback. See [verification evidence](docs/VERIFICATION_REPORT.md) and [release gate](docs/RELEASE_GATE.md). More extensive capabilities in open PRs are not part of `main` until merged.
