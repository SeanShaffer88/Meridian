# Changelog

## Unreleased

- Dual-pane browsing, folder navigation, bookmarks, and local workspace persistence.
- New-folder creation and validated in-place file/folder rename, including case-only renames.
- Conservative cross-pane copy with staging, cancellation, timestamp preservation, and no-overwrite behavior.
- Windows CI publishes downloadable self-contained portable outputs for x64 and ARM64.
- Paired workspace tabs and bookmarks restore local session paths and tab selection; interactive restoration validation remains open.
- Cross-pane move, confirmed Recycle Bin requests, properties, independent sorting/filtering, Folder DNA and in-memory Drop Shelf batch preview/copy with cancellation and partial outcomes.
- Development branch: bounded UTF-8/UTF-16/hex previews, image preview and Windows media controls without autoplay; playback/resource verification remains open.
- Development branch: TimeVault WMI discovery, volume matching and limited read-only historical listing; backend-only single-file export and receipt contracts, without a recovery UI.
- Development branch: ZIP/TAR/TGZ inspection, creation from selected files/folders and staged whole-tree extraction; entry policy, byte/count limits, CRC/trailer validation and cancellation/cleanup regressions.
- Development branch: bounded Foresight hints/JSON reference extraction, Consequence Engine plan/receipt contracts and read-only Storage Reclaimer inventory/plans; no AI, cleanup executor or universal undo.
- Reject existing linked ancestors in copy/move/rename; preserve the originating archive-extraction pane.
- Fix published-app startup by including its XAML resource index; include WinUI resources and valid toolbar symbols. Record latest startup/unhandled error locally.
- CI checks published resource indexes, starts the x64 app and generates unsigned per-user installers plus portable x64/ARM64 artifacts.
- Application baseline `2eef02a`: 339 local core tests pass; all Windows CI jobs pass, including x64 startup. These are preview verification results, not a release announcement.
- Documentation: dated capability/evidence inventory, user guide, expanded architecture/threat model/build guidance and corrected stale implementation/release claims.
- Remove unsafe cross-volume move copy/delete fallback. Move/rename now use Windows rename without replacement or copy fallback, support extended paths and let Windows resolve volume identity. Four regressions and local C: to E: file/folder refusal checks pass; source data remains intact and destination empty.
- Release blockers remain: a future verified cross-volume engine, full workflow/failure testing, clean install/upgrade/uninstall, ARM64 runtime, accessibility, performance and dependency/license/security audit. See [verification](docs/VERIFICATION_REPORT.md).
