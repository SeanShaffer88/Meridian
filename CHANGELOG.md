# Changelog

## Unreleased

- The dark theme is darker: the window background, panels and raised areas are now near-black tones, still one visible step apart so the sidebar, panes and header stay distinct. The light theme is unchanged.
- Red is the default accent colour again. If your accent was still the previous Sky default, it switches to red once; Sky blue remains available in Settings, and any accent you picked yourself is kept exactly.
- The selection highlight is now a dark red-gray (a pale red-gray in the light theme). It no longer follows the accent colour, so changing the accent never changes how selected files look.
- Meridian now has its own app icon — two panes forming an M around a red meridian line — shown on the taskbar, on shortcuts and in Settings instead of the generic program icon, and the same mark appears in the title bar.

- Copy and Move now run in a Transfers queue: a strip below the panes shows each job's progress, speed and estimated time left, with Pause, Resume and Cancel. Jobs run one at a time and are saved as they go; a job left unfinished when Meridian closes comes back paused and never restarts on its own.
- When a copy or move reaches a file whose name already exists at the destination, the job waits in the strip and you choose per file — Replace, Keep both (the incoming file gets a numbered name), or Skip — with an option to apply the choice to all remaining files.
- Finished transfers are kept in a History view in the same strip. A finished Copy or Move can be undone from History while the files it produced are unchanged; the Undo button explains itself when undo is not available. Deletes cannot be undone from History.
- Drag and drop: drag selected files onto the other pane, a folder inside a pane, a sidebar location, or a Home drive or folder card to copy or move them. Dropping moves within the same volume and copies across volumes; Ctrl forces Copy and Shift forces Move, and the drag label names the operation and destination before you let go. Dragging files in from Explorer works the same way.
- New Tools command "File hash…": shows the SHA-256 fingerprint of each selected file with a button to copy it, plus a compare field that tells you whether a pasted hash matches one of the files.
- New Settings choices for transfers: "When a name already exists" (Ask me each time, Skip, Keep both, Replace, or Replace only if newer) and "Verify copies with a checksum (slower)", which reads each copy back and compares its SHA-256 with the original before putting it in place.
- Drop Shelf copies run through the Transfers queue after their preview and confirmation, like other copies.
- Every interactive control in the main window and in Settings now carries a plain-words tooltip; controls that overwrite files or cancel work say what will happen.
- Favorites can be organized into named groups in the sidebar: right-click a favorite and choose "Move to group" to file it under a group, create a new group, or move it back to the ungrouped favorites. Group headers collapse and expand, and the collapsed state is remembered. A group header's right-click menu creates, renames, or deletes groups; deleting a group never deletes its favorites — they move back to the ungrouped list. Favorites saved by earlier versions appear ungrouped, unchanged.
- Each pane gains an optional folder tree (the "Tree" toggle beside the layout buttons): a folders-only column rooted at the pane's current drive, loaded as you expand it. The tree follows the pane, expanding to and selecting the current folder after every navigation; selecting a folder in the tree opens it in the pane, and files can be dropped onto a tree folder to copy or move them there. Whether the tree is shown is remembered per pane.
- Sidebar drives gain a right-click menu: "Inspect drive…" shows the drive's volume label, type, file system, capacity and free space as Windows reports them, and removable drives offer "Eject", which safely removes the drive through Windows and explains in plain words when a program is still using it.
- The sidebar is reordered: Drives come first, and each drive now shows a slim usage bar with its percent used and total size beside it; then Places (Home, Desktop, Documents, Downloads, Music, Pictures, Videos); then Favorites. Settings stays pinned at the bottom.
- Each pane's path box is now a breadcrumb bar: one button per level from the drive root (or network share) to the current folder, and clicking a level opens it. Clicking the empty stretch of the bar — or pressing Ctrl+L — swaps in a text box holding the full path; Enter opens it and Escape returns to the breadcrumbs. Long folder names compress with an ellipsis and show the full path as a tooltip. Dropping files on a segment targets that level's folder; dropping on the bar's empty stretch targets the current folder.
- The right-side details panel is replaced by a preview block at the bottom of each pane. The block shows a large preview of the selected item (an image preview, the opening of a text file, or a folder treatment with its measured size and file/folder counts), its name, type, created and modified dates, and size, plus photo details (dimensions, camera, date taken, recorded location) and the "Opens in" app where they exist; with nothing selected it describes the folder the pane is standing in. Its header line shows the folder's item count and total size — folders count only once measured, never walked just for the line — plus the selection's count and size while anything is selected. Each pane's block collapses on its own chevron and the state is remembered per pane; if the old details panel was open, both blocks start expanded. The metadata editor and permission viewer that lived in the old panel are removed with it.

## 0.4.0

- The window now uses a custom title bar: the header row is the title bar, with the minimize, maximize and close buttons at its top-right corner, and the operations toolbar (Copy, Move, New folder, Rename, and the archive and view actions) sits in the header beside the Meridian wordmark. On narrow windows the toolbar's extra actions move into its "…" menu instead of crowding the header.
- Workspace tabs are removed. Meridian keeps a single session — the two panes reopen where you left them, with the same layouts, bookmarks and saved locations as before. A settings file saved by an earlier version with several workspace tabs restores the tab that was active and ignores the rest.
- Layout now fills the window at any size: the panes, sidebar and file lists stretch with the window with no empty bands, and the window keeps a sensible minimum size. The right pane's Home page now renders in the right pane (it was declared inside the left pane's layout).

## 0.3.1

- Fix: the program failed to open in 0.3.0 — a grouping control reported its starting value while the window was still being built, before the panes existed. The grouping and filter paths now guard against that, and a test checks every control that can report during window construction.

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
