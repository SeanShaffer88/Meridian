# Changelog

## Unreleased

## 0.3.0

- New look: a blue-gray Fluent shell with a left sidebar (Favorites, This PC, Drives, Settings), a Home page with drive cards showing free space and a recent-folders list, and the details strip reshaped into a right-side panel with file facts, EXIF summary, metadata editing and a permission viewer.
- The default accent colour is now blue (Sky); the accent setting keeps eight presets plus a custom colour, and any colour saved before still applies exactly as saved.
- Miller Columns layout: folders cascade into the next column as you browse, and selecting a file shows it in a preview column. Each pane switches between the standard list and Columns (Ctrl+Shift+V), and the choice is remembered per workspace tab.
- Grid and Tiles view modes with thumbnails (video files show a poster frame). The Details view gains an Age column with relative times, folder sizes measured in the background and cached, columns that adapt to the pane's width, and optional grouping by type, date or size.
- Every folder remembers its own layout, view mode, sort and grouping.
- Fix: the program failed to open in 0.2.0 (an invalid toolbar icon name in the main window). The toolbar now uses a valid icon, and a test checks every icon name in the program against the valid set.

## 0.2.0

- New dark Fluent theme based on near-black layered surfaces with white text; the light theme is derived from the same design tokens.
- Accent colour is now a setting: eight preset colours plus a custom colour, applied immediately without restarting. Following the Windows accent colour remains available as an option.
- Spacebar quick preview: Space opens a preview of the selected file (text, images, audio/video, bounded hex), arrow keys move through the folder while it stays open, Space or Esc closes it.
- Details strip (Tools menu or Ctrl+Shift+D): thumbnail, file facts, and folder sizes measured in the background with progress; the setting is remembered.
- Image details now include an EXIF summary (dimensions, camera, date taken; GPS coordinates shown as text).
- Metadata editing for writable properties (title, comment, keywords, rating) on supported file types, always reviewed and confirmed before anything is written; properties a file cannot accept are shown read-only with the reason.
- Permission viewer: a read-only view of a file's owner and access entries, with a handoff to the Windows security dialog for changes.

## 0.1.0

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
