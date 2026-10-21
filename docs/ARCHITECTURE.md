# Architecture

- **Meridian.App** — WinUI 3 presentation, pane navigation, file launching, and user actions.
- **Meridian.Core** — filesystem listing and conservative copy logic without UI dependencies.
- **Meridian.Core.Tests** — unit tests for ordering, formatting, copy behavior, and overwrite protection.

The UI performs directory enumeration on a background task and marshals the results back to the UI thread. A cancellation token is used for superseded directory reads. Copy operations stage a file or directory in the destination folder and rename it into place only after the copy completes; this reduces exposure to incomplete final names but is not a full transactional guarantee. File data is copied in bounded chunks so cancellation can be checked between reads and writes, and staged data is removed on failure where possible. Selected source file timestamps are preserved by copying; attributes, directory timestamps, ACLs and other metadata are not guaranteed to be preserved.

The UI exposes cancellation for the active copy, dialog-based new-folder creation, and in-place file/folder rename. Rename rejects invalid names and reparse points and refuses to replace an existing item. It does not yet report byte-level progress, support multiple queued transfers, or provide pause/resume. Do not treat this initial design as a completed safety review.

## Workspace restoration and bookmarks

Meridian stores the last successfully browsed left- and right-pane paths and named folder bookmarks in `%LOCALAPPDATA%/Meridian/workspace.json`. The file contains saved paths, tab data, bookmark labels, and active-tab selection; no file contents. Workspace writes use a temporary file followed by a replacement move; malformed, missing, or unreadable saved state falls back to the default Documents/Downloads locations. Bookmarks are user-managed and remain local to the current Windows user. Persistence is best-effort and must not prevent file browsing if the preference file cannot be read or written.

Pane filtering uses filename and type matching; the core filtering method retains optional full-path matching for other callers. Navigation commits the current path after successful enumeration. Batch copy is sequential, non-transactional, and does not roll back earlier successful items. Cancellation during its initial UI preflight remains proposed in PR #41, not implemented in `main`.

## Workspace tabs

The UI maintains workspace tabs as paired left/right folder locations. Switching tabs refreshes both panes; tab names and locations, plus the active tab index, are stored in the existing local workspace JSON. Older workspace files without tab data fall back to a single default tab. This is a first step toward fuller tab/session management; tab reordering, locking, pinning, and layout restoration are not implemented yet.
